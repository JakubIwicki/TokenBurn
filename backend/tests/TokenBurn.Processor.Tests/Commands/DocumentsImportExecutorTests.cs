using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using TokenBurn.Processor.Commands;
using TokenBurn.Processor.Documents;
using TokenBurn.Processor.Documents.Indexing;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Infrastructure.Embeddings;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Data;
using TokenBurn.Testing.Common.Mocking;

namespace TokenBurn.Processor.Tests.Commands;

[Collection("elasticsearch")]
public sealed class DocumentsImportExecutorTests : TelemetryHandlerTestBase, IAsyncLifetime
{
    private const string IndexName = "documents";
    private const int EmbeddingDims = 384;
    private static readonly FakeTimeProvider Clock = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private static readonly float[] EmbeddingVector = Enumerable.Repeat(0.5f, EmbeddingDims).ToArray();
    private static readonly EmbeddingsOptions EmbeddingsOptions = new(Uri: null, BatchSize: 64, Dims: EmbeddingDims, Timeout: TimeSpan.FromSeconds(120), MaxRunChars: 4000);
    private static readonly DocumentsOptions Options = new(ChunkMaxTokens: 20, TokenCharsPerToken: 4, MaxFileBytes: 1000, EmbeddingBatchSize: 4);
    private static readonly string LongText = string.Join("\n\n", Enumerable.Range(0, 8)
        .Select(i => $"Paragraph {i} with a reasonably long line of words to produce several chunks."));

    private ElasticsearchClient _client = null!;
    private DocumentIndexTemplateInitializer _templateInitializer = null!;
    private FakeEmbeddingClient _embeddings = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await base.InitializeAsync();
        ElasticsearchFixture fixture = await SharedElasticsearch.GetFixtureAsync();
        _client = fixture.CreateClient();
        _templateInitializer = new DocumentIndexTemplateInitializer(
            new Lazy<ElasticsearchClient>(() => _client),
            NullLogger<DocumentIndexTemplateInitializer>.Instance,
            new EmbeddingsOptions(Uri: null, BatchSize: 64, Dims: EmbeddingDims, Timeout: TimeSpan.FromSeconds(120), MaxRunChars: 4000));
        _embeddings = FakeEmbeddingClient.Returning(EmbeddingVector);
    }

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();

    [Fact]
    public async Task ImportsDocuments_WritesRowsAndChunks_AndIndexesElasticsearch()
    {
        await DeleteIndexAsync();
        string tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "a.txt"), LongText);

            await CreateSut().ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);

            SearchDocument document = (await Context.SearchDocuments.AsNoTracking().ToListAsync()).Should().ContainSingle().Which;
            SearchDocumentChunk[] chunks = (await Context.SearchDocumentChunks.AsNoTracking().ToListAsync()).ToArray();
            chunks.Should().NotBeEmpty();
            chunks.Should().OnlyContain(chunk => chunk.DocumentId == document.StoredId);
            chunks.Select(chunk => chunk.Ordinal).Should().OnlyHaveUniqueItems();
            _embeddings.CallCount.Should().Be(chunks.Length);

            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);
            (await CountAllAsync()).Should().Be(chunks.Length);

            Dictionary<string, JsonElement> first = await GetSourceAsync($"{document.StoredId}:{chunks[0].Ordinal}");
            first["chunk_text"].GetString().Should().Be(chunks[0].ChunkText);
            first["document_id"].GetInt64().Should().Be(document.StoredId);
            first["ordinal"].GetInt32().Should().Be(chunks[0].Ordinal);
            first["uri"].GetString().Should().Be(document.Uri);
            first["embedding"].EnumerateArray().Should().HaveCount(EmbeddingDims);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ReRun_SameContent_IsANoOp_NoDuplicateRowsOrEsDocs()
    {
        await DeleteIndexAsync();
        string tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "a.txt"), LongText);
            DocumentsImportExecutor sut = CreateSut();

            await sut.ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);
            int documentsAfterFirst = await Context.SearchDocuments.CountAsync();
            int chunksAfterFirst = await Context.SearchDocumentChunks.CountAsync();
            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);
            long esAfterFirst = await CountAllAsync();
            int embeddingCalls = _embeddings.CallCount;

            await sut.ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);

            (await Context.SearchDocuments.CountAsync()).Should().Be(documentsAfterFirst);
            (await Context.SearchDocumentChunks.CountAsync()).Should().Be(chunksAfterFirst);
            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);
            (await CountAllAsync()).Should().Be(esAfterFirst);
            _embeddings.CallCount.Should().Be(embeddingCalls);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ChangedFile_AddsANewDocumentRow_AndNewEsDocs()
    {
        await DeleteIndexAsync();
        string tempDir = CreateTempDir();
        try
        {
            string filePath = Path.Combine(tempDir, "a.txt");
            File.WriteAllText(filePath, LongText);
            DocumentsImportExecutor sut = CreateSut();

            await sut.ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);
            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);
            long esAfterFirst = await CountAllAsync();

            File.WriteAllText(filePath, "Completely different content that hashes to a new document.");

            await sut.ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);

            (await Context.SearchDocuments.CountAsync()).Should().Be(2);
            SearchDocumentChunk[] newChunks = (await Context.SearchDocumentChunks.AsNoTracking().ToListAsync()).ToArray();
            newChunks.Select(chunk => chunk.DocumentId).Distinct().Should().HaveCount(2);
            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);
            (await CountAllAsync()).Should().Be(esAfterFirst + newChunks.Count(chunk => chunk.DocumentId == newChunks[^1].DocumentId));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task BadFiles_AreSkipped_WithoutAbortingTheBatch()
    {
        await DeleteIndexAsync();
        string tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "good.txt"), LongText);
            File.WriteAllText(Path.Combine(tempDir, "bad.bin"), new string('\0', 4) + "binary payload");
            File.WriteAllText(Path.Combine(tempDir, "huge.txt"), new string('x', 2000));

            var progress = new List<string>();
            await CreateSut().ExecuteAsync(CreateCommand(tempDir), (json, _) => { progress.Add(json); return Task.CompletedTask; }, CancellationToken.None);

            (await Context.SearchDocuments.CountAsync()).Should().Be(1);
            SearchDocumentChunk[] chunks = (await Context.SearchDocumentChunks.AsNoTracking().ToListAsync()).ToArray();
            chunks.Should().NotBeEmpty();
            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);
            (await CountAllAsync()).Should().Be(chunks.Length);
            progress[^1].Should().Contain("\"filesSkipped\":2");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AllProcessedFilesSkipped_Throws()
    {
        await DeleteIndexAsync();
        string tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "bad.bin"), new string('\0', 4) + "binary payload");
            File.WriteAllText(Path.Combine(tempDir, "huge.txt"), new string('x', 2000));

            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().ExecuteAsync(
                CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None));

            (await Context.SearchDocuments.CountAsync()).Should().Be(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ReRun_WhenEsDocsDeleted_RestoresThemWithoutDuplicatingRows()
    {
        await DeleteIndexAsync();
        string tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "a.txt"), LongText);
            DocumentsImportExecutor sut = CreateSut();

            await sut.ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);
            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);
            (await CountAllAsync()).Should().BeGreaterThan(0);

            // Simulate the crash window: PG rows exist but the ES projection was lost.
            await DeleteIndexAsync();
            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);

            await sut.ExecuteAsync(CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None);

            (await Context.SearchDocuments.CountAsync()).Should().Be(1);
            int chunkCount = await Context.SearchDocumentChunks.CountAsync();
            await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);
            (await CountAllAsync()).Should().Be(chunkCount);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task EmbeddingFailure_FailsTheRun_AndLeavesNoCommittedRows()
    {
        await DeleteIndexAsync();
        string tempDir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "a.txt"), LongText);

            // The second chunk's embedding call fails; because embedding happens BEFORE the
            // document insert, the run throws and nothing is persisted.
            await Assert.ThrowsAsync<EmbeddingException>(() => CreateSut(new ThrowingEmbeddingClient()).ExecuteAsync(
                CreateCommand(tempDir), (_, _) => Task.CompletedTask, CancellationToken.None));

            (await Context.SearchDocuments.CountAsync()).Should().Be(0);
            (await Context.SearchDocumentChunks.CountAsync()).Should().Be(0);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task MalformedSince_Throws()
    {
        string tempDir = CreateTempDir();
        try
        {
            // A present-but-unparseable `since` must fail the command, never silently degrade a
            // filtered import into a full import.
            await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut().ExecuteAsync(
                ImportCommand.Create("documents", JsonSerializer.Serialize(new { path = tempDir, since = "not-a-date" }), Clock.GetUtcNow()),
                (_, _) => Task.CompletedTask, CancellationToken.None));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    private DocumentsImportExecutor CreateSut(IEmbeddingClient? embeddings = null) => new(
        new TextChunker(Options),
        new DocumentsUpserter(Context),
        new DocumentChunkUpserter(Context),
        Context,
        new Lazy<IEmbeddingClient>(() => embeddings ?? _embeddings),
        new Lazy<ElasticsearchClient>(() => _client),
        _templateInitializer,
        EmbeddingsOptions,
        Options,
        Clock,
        MockLogger<DocumentsImportExecutor>.GetSuccessful().Object);

    private static ImportCommand CreateCommand(string path)
        => ImportCommand.Create("documents", JsonSerializer.Serialize(new { path }), Clock.GetUtcNow());

    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private async Task<long> CountAllAsync()
    {
        CountResponse response = await _client.CountAsync(IndexName, c => c.Query(q => q.MatchAll()), CancellationToken.None);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
        return response.Count;
    }

    private async Task<Dictionary<string, JsonElement>> GetSourceAsync(string id)
    {
        GetResponse<Dictionary<string, JsonElement>> response = await _client.GetAsync<Dictionary<string, JsonElement>>(IndexName, id, CancellationToken.None);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
        response.Found.Should().BeTrue();
        return response.Source!;
    }

    private async Task DeleteIndexAsync()
    {
        var exists = await _client.Indices.ExistsAsync(IndexName, CancellationToken.None);
        if (!exists.Exists)
            return;

        DeleteIndexResponse response = await _client.Indices.DeleteAsync(IndexName, CancellationToken.None);
        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Failed to delete the documents index: {response.DebugInformation}");
    }

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        private readonly IReadOnlyList<float> _vector;

        private FakeEmbeddingClient(IReadOnlyList<float> vector) => _vector = vector;

        public int CallCount { get; private set; }

        public static FakeEmbeddingClient Returning(IReadOnlyList<float> vector) => new(vector);

        public Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_vector);
        }
    }

    private sealed class ThrowingEmbeddingClient : IEmbeddingClient
    {
        private int _calls;

        public Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            _calls++;
            if (_calls == 2)
                throw new EmbeddingException("Embedding endpoint unavailable.");
            return Task.FromResult<IReadOnlyList<float>>(Enumerable.Repeat(0.5f, 384).ToArray());
        }
    }
}
