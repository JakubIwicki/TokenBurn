using System.Text.Json;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TokenBurn.Contracts;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Infrastructure.Embeddings;
using TokenBurn.Processor.Infrastructure.Indexing;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;
using TokenBurn.Testing.Common.Data;
using ContractsPricingStatus = TokenBurn.Contracts.PricingStatus;
using ContractsRunStatus = TokenBurn.Contracts.RunStatus;
using DomainRunStatus = TokenBurn.Processor.Domain.RunStatus;

namespace TokenBurn.Processor.Tests.Infrastructure;

[Collection("elasticsearch")]
public sealed class RunEmbedderTests : TelemetryHandlerTestBase, IAsyncLifetime
{
    private const string IndexName = ElasticsearchFixture.TracesIndex;
    private const string EmbeddingText = "user: what is the weather?\nassistant: sunny";
    private static readonly float[] EmbeddingVector = Enumerable.Repeat(0.5f, 384).ToArray();
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 30, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddMinutes(1);

    private ElasticsearchClient _client = null!;
    private SearchIndexTemplateInitializer _templateInitializer = null!;
    private ElasticsearchRunIndexer _indexer = null!;
    private EmbeddingsOptions _options = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        await base.InitializeAsync();
        ElasticsearchFixture fixture = await SharedElasticsearch.GetFixtureAsync();
        _client = fixture.CreateClient();
        _options = EmbeddingsOptions.FromConfiguration(new ConfigurationBuilder().Build());
        _templateInitializer = new SearchIndexTemplateInitializer(_client, NullLogger<SearchIndexTemplateInitializer>.Instance, _options);
        _indexer = new ElasticsearchRunIndexer(_client, _templateInitializer);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
    }

    [Fact]
    public async Task EmbedsRun_WithMessages_WritesBothFields_LeavingIndexerFieldsIntact()
    {
        await DeleteIndexAsync(CancellationToken.None);
        Guid runId = await SeedIndexedRunAsync();
        FakeEmbeddingClient embeddings = FakeEmbeddingClient.Returning(EmbeddingVector);

        await CreateSut(embeddings).EmbedAsync(runId, CancellationToken.None);

        embeddings.LastTexts.Should().Equal(EmbeddingText);
        Dictionary<string, JsonElement> source = await GetSourceAsync(runId);
        source["embedding"].EnumerateArray().Select(element => element.GetSingle()).Should().Equal(EmbeddingVector);
        source["embedding_text"].GetString().Should().Be(EmbeddingText);
        source["session_id"].GetString().Should().Be("sess-embed");
        source["input_tokens"].GetInt64().Should().Be(1000);
        source["searchable_text"].GetString().Should().Be("acme explore ext-1 sess-embed");
    }

    [Fact]
    public async Task EmbedsRun_Twice_OverwritesSameFields_WithoutDuplicatingTheDocument()
    {
        await DeleteIndexAsync(CancellationToken.None);
        Guid runId = await SeedIndexedRunAsync();
        RunEmbedder sut = CreateSut(FakeEmbeddingClient.Returning(EmbeddingVector));

        await sut.EmbedAsync(runId, CancellationToken.None);
        await sut.EmbedAsync(runId, CancellationToken.None);
        await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);

        CountResponse count = await _client.CountAsync(
            IndexName,
            c => c.Query(q => q.Term(t => t.Field("id").Value(runId.ToString("D")))),
            CancellationToken.None);
        count.IsValidResponse.Should().BeTrue(count.DebugInformation);
        count.Count.Should().Be(1);
        Dictionary<string, JsonElement> source = await GetSourceAsync(runId);
        source["embedding"].EnumerateArray().Should().HaveCount(EmbeddingVector.Length);
        source["embedding_text"].GetString().Should().Be(EmbeddingText);
    }

    [Fact]
    public async Task EnsureVectorMapping_WhenLiveIndexLacksEmbedding_AddsBothFields()
    {
        await DeleteIndexAsync(CancellationToken.None);
        try
        {
            await _client.Indices.CreateAsync(
                new CreateIndexRequest(IndexName)
                {
                    Mappings = new TypeMapping
                    {
                        Dynamic = DynamicMapping.False,
                        Properties = new Properties { ["searchable_text"] = new TextProperty() }
                    }
                },
                CancellationToken.None);

            await _templateInitializer.EnsureVectorMappingAsync(CancellationToken.None);

            GetMappingResponse response = await _client.Indices.GetMappingAsync(IndexName, CancellationToken.None);
            response.IsValidResponse.Should().BeTrue(response.DebugInformation);
            TypeMapping mappings = response.Mappings[IndexName].Mappings;
            IReadOnlyCollection<string> mappedFields = ((IDictionary<PropertyName, IProperty>)mappings.Properties!)
                .Keys.Select(propertyName => propertyName.Name!)
                .ToList();
            mappedFields.Should().Contain("embedding");
            mappedFields.Should().Contain("embedding_text");
        }
        finally
        {
            // Restore the no-index state so sibling tests recreate the index from the
            // (full-mapping) template instead of inheriting this minimal one.
            await DeleteIndexAsync(CancellationToken.None);
        }
    }

    private RunEmbedder CreateSut(FakeEmbeddingClient embeddingClient)
        => new(_client, Context, embeddingClient, new RunEmbeddingTextBuilder(), _options, _templateInitializer,
            NullLogger<RunEmbedder>.Instance);

    private async Task<Guid> SeedIndexedRunAsync()
    {
        AgentRun run = AgentRun.Create(
            "sess-embed", "agent-1", "delegate-ledger", null, "explore", "deepseek-v4-flash",
            DomainRunStatus.Completed, Start, End, 1000, 0, 0, 500, null, workspace: "acme");
        Db.Store(run);
        Guid runId = run.Id;
        Db.StoreAll(
            AgentMessage.Create(runId, 1, "user", "what is the weather?", null, null, 10, 0, 0, 5, Start),
            AgentMessage.Create(runId, 2, "assistant", "sunny", null, "deepseek-v4-flash", 20, 0, 0, 10, End));

        PricedRun priced = new()
        {
            Id = runId,
            SessionId = "sess-embed",
            Source = "delegate-ledger",
            ExternalId = "ext-1",
            Workspace = "acme",
            Persona = "explore",
            ModelSlug = "deepseek-v4-flash",
            Status = ContractsRunStatus.Completed,
            StartedAt = Start,
            EndedAt = End,
            InputTokens = 1000,
            CacheReadTokens = 0,
            CacheWriteTokens = 0,
            OutputTokens = 500,
            PricingStatus = ContractsPricingStatus.Priced,
            Version = 1
        };
        await _indexer.IndexAsync(priced, CancellationToken.None);
        return runId;
    }

    private async Task<Dictionary<string, JsonElement>> GetSourceAsync(Guid runId)
    {
        GetResponse<Dictionary<string, JsonElement>> response = await _client.GetAsync<Dictionary<string, JsonElement>>(
            IndexName, runId.ToString("D"), CancellationToken.None);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
        return response.Source!;
    }

    private async Task DeleteIndexAsync(CancellationToken cancellationToken)
    {
        var exists = await _client.Indices.ExistsAsync(IndexName, cancellationToken);
        if (!exists.Exists)
            return;

        DeleteIndexResponse response = await _client.Indices.DeleteAsync(IndexName, cancellationToken);
        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Failed to delete the traces index: {response.DebugInformation}");
    }

    private sealed class FakeEmbeddingClient : IEmbeddingClient
    {
        private readonly IReadOnlyList<float> _vector;

        private FakeEmbeddingClient(IReadOnlyList<float> vector) => _vector = vector;

        public IReadOnlyList<string>? LastTexts { get; private set; }

        public static FakeEmbeddingClient Returning(IReadOnlyList<float> vector) => new(vector);

        public Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
        {
            LastTexts = texts;
            return Task.FromResult(_vector);
        }
    }
}
