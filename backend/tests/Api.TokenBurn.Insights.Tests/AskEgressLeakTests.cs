using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Api.TokenBurn.Insights.Extensions.Embeddings;
using Api.TokenBurn.Insights.Persistence;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TokenBurn.Contracts;
using TokenBurn.Processor.Documents.Indexing;
using TokenBurn.Processor.Infrastructure.Indexing;
using TokenBurn.Testing.Common.Data;

namespace Api.TokenBurn.Insights.Tests;

/// <summary>
///     THE privacy gate (privacy-boundary rule 7): a real corpus secret-shaped probe and an
///     absolute path are seeded into a trace's searchable text AND a document chunk, the ask
///     endpoint runs against the real DeepSeek client, and the test asserts on the CAPTURED
///     OUTBOUND request body — not the API response. The recording handler completes the
///     response, so no network call is made.
/// </summary>
[Collection("insights-search")]
public sealed class AskEgressLeakTests : IAsyncLifetime
{
    private const string TracesIndex = ElasticsearchFixture.TracesIndex;
    private const string DocumentsIndex = "documents";
    private const int Dims = 384;

    private const string Probe = "sk-testprobeab12";
    private const string TraceSecretPath = "/home/jakub/private-repo/acme-widgets";
    private const string DocumentSecretPath = "/data/private/secret.txt";
    // Real corpora store the document uri as an absolute filesystem path — it must never reach
    // the model, and the gate seeds it in BOTH path-shaped and URL-shaped forms.
    private const string PathUri = "/home/jakub/TokenBurn/backend/src/TokenBurn.Processor/Commands/DocumentsImportExecutor.cs";
    private const string UrlUri = "https://docs.acme.example/guide";
    private const string TestApiKey = "test-deepseek-key";
    private const string DeepSeekEndpoint = "http://deepseek.test/";

    private ElasticsearchClient _es = null!;
    private ElasticsearchRunIndexer _indexer = null!;
    private DocumentIndexTemplateInitializer _documentTemplate = null!;
    private string _cloneDatabaseName = null!;
    private string _connectionString = null!;
    private string _elasticsearchUri = null!;

    [Fact]
    public async Task OutboundBody_ContainsNoProbePathsOrDeniedFields()
    {
        await ClearTracesAsync(CancellationToken.None);
        await ClearDocumentsAsync(CancellationToken.None);
        Guid runId = new("01234567-89ab-cdef-0123-456789abcdef");
        await SeedTraceWithEmbeddingAsync(runId, "sess-leak", workspace: TraceSecretPath, externalId: Probe, cancellationToken: CancellationToken.None);
        // Two document shapes — a PATH-shaped uri (real corpora store the file path) and a
        // URL-shaped uri. NEITHER may reach the outbound body; the uri surfaces only on the
        // authed API response, never in the prompt.
        await SeedDocumentChunkAsync("1:0", 1, PathUri, "DocumentsImportExecutor", 0, "acme documents import code", UnitVectorWithX(1.0), CancellationToken.None);
        await SeedDocumentChunkAsync("1:1", 1, UrlUri, "Acme Guide", 1, $"acme document content {DocumentSecretPath} {Probe}", UnitVectorWithX(1.0), CancellationToken.None);

        var recorder = new RecordingHandler();
        var settings = new Dictionary<string, string?>
        {
            ["Ask:Provider"] = "deepseek",
            ["Ask:DeepSeekApiKey"] = TestApiKey,
            ["Ask:DeepSeekEndpoint"] = DeepSeekEndpoint
        };
        await using WebApplicationFactory<InsightsDbContext> factory = InsightsTestHost.Create(
            _connectionString,
            _elasticsearchUri,
            new FakeEmbeddingClient(UnitVectorWithX(1.0)),
            extraSettings: settings,
            configureServices: services => services.AddHttpClient("deepseek").AddHttpMessageHandler(() => recorder));
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await SendAskAsync(client, new { question = "acme" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        recorder.RequestBody.Should().NotBeNull("the DeepSeek client must have sent a body");
        recorder.RequestBody!.Should().NotContain(Probe);
        recorder.RequestBody.Should().NotContain(TraceSecretPath);
        recorder.RequestBody.Should().NotContain(DocumentSecretPath);
        // The document uri — path-shaped or URL-shaped — must never reach the model; it is
        // exposed only on the authed API surface.
        recorder.RequestBody.Should().NotContain(PathUri);
        recorder.RequestBody.Should().NotContain(UrlUri);
        recorder.RequestBody.Should().NotContain("external_id");
        recorder.RequestBody.Should().NotContain("workspace");
        // Positive control: the body demonstrably carries the retrieved context (the run id,
        // document titles and excerpt text), so the absence assertions above cannot pass
        // vacuously against an empty body — a regression that re-adds uri is caught.
        recorder.RequestBody.Should().Contain("sess-leak");
        recorder.RequestBody.Should().Contain("DocumentsImportExecutor");
        recorder.RequestBody.Should().Contain("acme document content");
        recorder.RequestBody.Should().Contain("[REDACTED]");

        recorder.RequestUri.Should().Be(DeepSeekEndpoint + "chat/completions");
        recorder.Authorization.Should().NotBeNull();
        recorder.Authorization!.Scheme.Should().Be("Bearer");
        recorder.Authorization.Parameter.Should().Be(TestApiKey);
    }

    public async Task InitializeAsync()
    {
        ElasticsearchFixture fixture = await SharedElasticsearch.GetFixtureAsync();
        _elasticsearchUri = fixture.Uri.ToString();
        _es = fixture.CreateClient();
        _indexer = new ElasticsearchRunIndexer(
            _es, new SearchIndexTemplateInitializer(_es, NullLogger<SearchIndexTemplateInitializer>.Instance));
        _documentTemplate = new DocumentIndexTemplateInitializer(
            new Lazy<ElasticsearchClient>(() => _es), NullLogger<DocumentIndexTemplateInitializer>.Instance);
        await _documentTemplate.EnsureTemplateAsync(CancellationToken.None);
        await _documentTemplate.EnsureVectorMappingAsync(CancellationToken.None);

        string template = await SharedPostgres.GetOrCreateTemplateAsync("telemetry", InsightsTestHost.MigrateTelemetryAsync);
        _connectionString = await SharedPostgres.CloneAsync(template);
        _cloneDatabaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;
    }

    public async Task DisposeAsync()
        => await SharedPostgres.DropDatabaseAsync(_cloneDatabaseName);

    private static async Task<HttpResponseMessage> SendAskAsync(HttpClient client, object body)
    {
        using HttpRequestMessage message = new(HttpMethod.Post, "/api/ask")
        {
            Content = new StringContent(System.Text.Json.JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        return await client.SendAsync(message);
    }

    private async Task SeedTraceWithEmbeddingAsync(
        Guid id,
        string sessionId,
        string? workspace = null,
        string? externalId = null,
        CancellationToken cancellationToken = default)
    {
        await _indexer.IndexAsync(BuildRun(id, sessionId, workspace, externalId), cancellationToken);
        UpdateResponse<RunIndexDocument> update = await _es.UpdateAsync<RunIndexDocument, RunEmbeddingPartial>(
            TracesIndex,
            id.ToString("D"),
            descriptor => descriptor.Doc(new RunEmbeddingPartial { Embedding = UnitVectorWithX(1.0), EmbeddingText = "unused by hybrid search" }),
            cancellationToken);
        update.IsValidResponse.Should().BeTrue(update.DebugInformation);
        await RefreshTracesAsync(cancellationToken);
    }

    private async Task SeedDocumentChunkAsync(
        string id,
        long documentId,
        string uri,
        string title,
        int ordinal,
        string chunkText,
        float[] embedding,
        CancellationToken cancellationToken)
    {
        IndexResponse response = await _es.IndexAsync(new DocumentChunkDocument
        {
            Id = id,
            DocumentId = documentId,
            Uri = uri,
            Title = title,
            Source = "documents",
            ContentHash = $"hash-{id}",
            IndexedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Ordinal = ordinal,
            ChunkText = chunkText,
            Embedding = embedding
        }, DocumentsIndex, id, cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
        await RefreshDocumentsAsync(cancellationToken);
    }

    private static PricedRun BuildRun(Guid id, string sessionId, string? workspace, string? externalId) => new()
    {
        Id = id,
        SessionId = sessionId,
        Source = "delegate-ledger",
        Workspace = workspace,
        ExternalId = externalId,
        Persona = "engineer",
        ModelSlug = "claude-opus-5",
        Status = RunStatus.Completed,
        PricingStatus = PricingStatus.Priced,
        StartedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        InputTokens = 100,
        OutputTokens = 50,
        Version = 1
    };

    private async Task ClearTracesAsync(CancellationToken cancellationToken)
    {
        var exists = await _es.Indices.ExistsAsync(TracesIndex, cancellationToken);
        if (!exists.Exists)
            return;
        DeleteByQueryResponse response = await _es.DeleteByQueryAsync(
            TracesIndex, d => d.Query(q => q.MatchAll()).Refresh(true), cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private async Task ClearDocumentsAsync(CancellationToken cancellationToken)
    {
        var exists = await _es.Indices.ExistsAsync(DocumentsIndex, cancellationToken);
        if (!exists.Exists)
            return;
        DeleteByQueryResponse response = await _es.DeleteByQueryAsync(
            DocumentsIndex, d => d.Query(q => q.MatchAll()).Refresh(true), cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private async Task RefreshTracesAsync(CancellationToken cancellationToken)
    {
        var response = await _es.Indices.RefreshAsync(TracesIndex, cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private async Task RefreshDocumentsAsync(CancellationToken cancellationToken)
    {
        var response = await _es.Indices.RefreshAsync(DocumentsIndex, cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private static float[] UnitVectorWithX(double x)
    {
        var vector = new float[Dims];
        vector[0] = (float)x;
        vector[1] = (float)Math.Sqrt(Math.Max(0, 1 - x * x));
        return vector;
    }

    private sealed class FakeEmbeddingClient(IReadOnlyList<float> vector) : IEmbeddingClient
    {
        public Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => Task.FromResult(vector);
    }

    private sealed class RecordingHandler : DelegatingHandler
    {
        public string? RequestBody { get; private set; }
        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"cmpl-test","object":"chat.completion","created":0,"model":"deepseek-chat","choices":[{"index":0,"message":{"role":"assistant","content":"DeepSeek test answer."},"finish_reason":"stop"}]}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
