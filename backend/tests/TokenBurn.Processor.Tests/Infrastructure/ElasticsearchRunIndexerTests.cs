using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using Elastic.Clients.Elasticsearch.Mapping;
using Microsoft.Extensions.Logging.Abstractions;
using TokenBurn.Contracts;
using TokenBurn.Processor.Infrastructure.Embeddings;
using TokenBurn.Processor.Infrastructure.Indexing;
using TokenBurn.Testing.Common.Data;

namespace TokenBurn.Processor.Tests.Infrastructure;

[Collection("elasticsearch")]
public sealed class ElasticsearchRunIndexerTests : IAsyncLifetime
{
    private const string IndexName = ElasticsearchFixture.TracesIndex;

    private ElasticsearchClient _client = null!;
    private SearchIndexTemplateInitializer _templateInitializer = null!;
    private ElasticsearchRunIndexer _sut = null!;

    [Fact]
    public async Task EnsuresTracesTemplate_WhenClusterReachable()
    {
        await ClearIndexAsync(CancellationToken.None);

        await _templateInitializer.EnsureTemplateAsync(CancellationToken.None);

        GetIndexTemplateResponse response = await _client.Indices.GetIndexTemplateAsync(IndexName, CancellationToken.None);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    [Fact]
    public async Task MapsEveryRunIndexDocumentField_WhenTemplateInstalled()
    {
        await _templateInitializer.EnsureTemplateAsync(CancellationToken.None);

        GetIndexTemplateResponse response = await _client.Indices.GetIndexTemplateAsync(IndexName, CancellationToken.None);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
        var template = response.IndexTemplates.Should().ContainSingle().Which;
        TypeMapping mappings = template.IndexTemplate!.Template!.Mappings!;
        IReadOnlyCollection<string> mappedFields = ((IDictionary<PropertyName, IProperty>)mappings.Properties!)
            .Keys
            .Select(propertyName => propertyName.Name!)
            .ToList();

        mappedFields.Should().Contain("workspace");
        mappedFields.Should().Contain("embedding");
        mappedFields.Should().Contain("embedding_text");

        string[] documentFields = typeof(RunIndexDocument)
            .GetProperties()
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name)
            .Where(name => name is not null)
            .Cast<string>()
            .ToArray();

        // The template maps exactly the RunIndexDocument fields PLUS the two embedding fields
        // the Phase 5 embedder writes via partial update — those are not part of the index-time
        // document shape, so parity admits them explicitly.
        mappedFields.Should().BeEquivalentTo(documentFields.Concat(["embedding", "embedding_text"]));
    }

    [Fact]
    public async Task IndexesRun_RetrievableByRunId_WithSnakeCaseFields()
    {
        await ClearIndexAsync(CancellationToken.None);
        PricedRun run = BuildRun(new Guid("01234567-89ab-cdef-0123-456789abcdef"), sessionId: "sess-index", inputTokens: 53695);

        await _sut.IndexAsync(run, CancellationToken.None);

        GetResponse<Dictionary<string, JsonElement>> response = await _client.GetAsync<Dictionary<string, JsonElement>>(IndexName, run.Id.ToString("D"), CancellationToken.None);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
        response.Id.Should().Be(run.Id.ToString("D"));
        Dictionary<string, JsonElement> source = response.Source!;
        source["session_id"].GetString().Should().Be("sess-index");
        source["model_slug"].GetString().Should().Be("deepseek-v4-flash");
        source["pricing_status"].GetString().Should().Be("Priced");
        source["status"].GetString().Should().Be("Completed");
        source["input_tokens"].GetInt64().Should().Be(53695);
        source.ContainsKey("sessionId").Should().BeFalse();
        source.ContainsKey("modelSlug").Should().BeFalse();
        source.ContainsKey("ended_at").Should().BeFalse();
    }

    [Fact]
    public async Task KeepsSingleDocument_WhenSameRunIndexedTwice()
    {
        await ClearIndexAsync(CancellationToken.None);
        PricedRun run = BuildRun(new Guid("12345678-9abc-def0-1234-56789abcdef0"), inputTokens: 500);

        await _sut.IndexAsync(run, CancellationToken.None);
        await _sut.IndexAsync(run, CancellationToken.None);
        await _client.Indices.RefreshAsync(IndexName, CancellationToken.None);

        CountResponse count = await _client.CountAsync(
            IndexName,
            c => c.Query(q => q.Term(t => t.Field("id").Value(run.Id.ToString("D")))),
            CancellationToken.None);
        count.IsValidResponse.Should().BeTrue(count.DebugInformation);
        count.Count.Should().Be(1);

        GetResponse<RunIndexDocument> response = await _client.GetAsync<RunIndexDocument>(IndexName, run.Id.ToString("D"), CancellationToken.None);
        RunIndexDocument stored = response.Source!;
        stored.Id.Should().Be(run.Id);
        stored.SessionId.Should().Be(run.SessionId);
        stored.InputTokens.Should().Be(500);
    }

    [Fact]
    public async Task RoundTripsValues_ThroughRealSerializer()
    {
        await ClearIndexAsync(CancellationToken.None);
        var startedAt = new DateTimeOffset(2026, 7, 24, 21, 57, 51, TimeSpan.Zero);
        PricedRun run = BuildRun(new Guid("23456789-abcd-ef01-2345-6789abcdef01"), sessionId: "sess-roundtrip", startedAt: startedAt, inputTokens: 53695, costUsd: 0.0121480408m);

        await _sut.IndexAsync(run, CancellationToken.None);

        GetResponse<RunIndexDocument> response = await _client.GetAsync<RunIndexDocument>(IndexName, run.Id.ToString("D"), CancellationToken.None);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
        RunIndexDocument doc = response.Source!;
        doc.Id.Should().Be(run.Id);
        doc.SessionId.Should().Be("sess-roundtrip");
        doc.Status.Should().Be("Completed");
        doc.StartedAt.Should().Be(startedAt);
        doc.CostUsd.Should().Be(0.0121480408m);
        doc.InputTokens.Should().Be(53695);
    }

    public async Task InitializeAsync()
    {
        ElasticsearchFixture fixture = await SharedElasticsearch.GetFixtureAsync();
        _client = fixture.CreateClient();
        _templateInitializer = new SearchIndexTemplateInitializer(
            _client,
            NullLogger<SearchIndexTemplateInitializer>.Instance,
            new EmbeddingsOptions(Uri: null, BatchSize: 64, Dims: 384, Timeout: TimeSpan.FromSeconds(120), MaxRunChars: 4000));
        _sut = new ElasticsearchRunIndexer(_client, _templateInitializer);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task ClearIndexAsync(CancellationToken cancellationToken)
    {
        var exists = await _client.Indices.ExistsAsync(IndexName, cancellationToken);
        if (!exists.Exists)
            return;

        DeleteByQueryResponse response = await _client.DeleteByQueryAsync(
            IndexName,
            d => d.Query(q => q.MatchAll()).Refresh(true),
            cancellationToken);
        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Failed to clear the traces index: {response.DebugInformation}");
    }

    private static PricedRun BuildRun(
        Guid id,
        string sessionId = "sess-1",
        string modelSlug = "deepseek-v4-flash",
        DateTimeOffset? startedAt = null,
        DateTimeOffset? endedAt = null,
        long? inputTokens = null,
        decimal? costUsd = null) => new()
    {
        Id = id,
        SessionId = sessionId,
        Source = "delegate-ledger",
        ModelSlug = modelSlug,
        Status = RunStatus.Completed,
        StartedAt = startedAt,
        EndedAt = endedAt,
        InputTokens = inputTokens,
        CostUsd = costUsd,
        PricingStatus = PricingStatus.Priced,
        Version = 1
    };
}
