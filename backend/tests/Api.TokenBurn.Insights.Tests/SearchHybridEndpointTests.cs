using System.Net;
using System.Text.Json;
using Api.TokenBurn.Insights.Extensions.Embeddings;
using Api.TokenBurn.Insights.Persistence;
using Elastic.Clients.Elasticsearch;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TokenBurn.Contracts;
using TokenBurn.Processor.Infrastructure.Indexing;
using TokenBurn.Testing.Common.Data;

namespace Api.TokenBurn.Insights.Tests;

[Collection("insights-search")]
public sealed class SearchHybridEndpointTests : IAsyncLifetime
{
    private const string IndexName = ElasticsearchFixture.TracesIndex;
    private const int Dims = 384;

    private ElasticsearchClient _es = null!;
    private ElasticsearchRunIndexer _indexer = null!;
    private WebApplicationFactory<InsightsDbContext> _factory = null!;
    private HttpClient _client = null!;
    private string _cloneDatabaseName = null!;
    private string _connectionString = null!;
    private string _elasticsearchUri = null!;

    [Fact]
    public async Task ReturnsHits_OrderedByFusedScore()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid docA = new("00000001-0000-0000-0000-000000000001");
        Guid docB = new("00000002-0000-0000-0000-000000000002");
        Guid docC = new("00000003-0000-0000-0000-000000000003");
        // Identical searchable_text ties the keyword leg, which breaks by id DESC
        // (C, B, A); the vector leg ranks the same three docs B, A, C by similarity.
        // RRF fuses the two to B, C, A — B tops both legs, C beats A on its keyword rank.
        await SeedWithEmbeddingAsync(docA, "acme-hybrid-tie", UnitVectorWithX(0.6), cancellationToken: CancellationToken.None);
        await SeedWithEmbeddingAsync(docB, "acme-hybrid-tie", UnitVectorWithX(1.0), cancellationToken: CancellationToken.None);
        await SeedWithEmbeddingAsync(docC, "acme-hybrid-tie", UnitVectorWithX(0.0), cancellationToken: CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=acme&mode=hybrid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().Equal(docB, docC, docA);
    }

    [Fact]
    public async Task ReturnsOnlyMatchingDocs_WhenTopVectorHitIsExcludedByFilter()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid matching = new("20000000-0000-0000-0000-000000000000");
        Guid excluded = new("10000000-0000-0000-0000-000000000000");
        // The excluded doc is the closest vector to the query but does not match the model
        // filter; the vector leg must apply the filter, or it would surface as the top hit.
        await SeedWithEmbeddingAsync(excluded, "acme-filt-x", UnitVectorWithX(1.0), modelSlug: "deepseek-v4-flash", cancellationToken: CancellationToken.None);
        await SeedWithEmbeddingAsync(matching, "zzz-filt-y", UnitVectorWithX(0.0), modelSlug: "claude-opus-5", cancellationToken: CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=acme&mode=hybrid&model=claude-opus-5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().ContainSingle().Which.Should().Be(matching);
    }

    [Fact]
    public async Task PagesAllFusedHits_WithNoOverlapOrGap()
    {
        await ClearIndexAsync(CancellationToken.None);
        IReadOnlyList<Guid> expected = await SeedFusedSequenceAsync(7, CancellationToken.None);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            string query = "?q=acme&mode=hybrid&limit=3" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using HttpResponseMessage response = await ActAsync(query);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            seen.AddRange(await ReadHitIdsAsync(response));
            cursor = await ReadNextCursorAsync(response);
        } while (cursor is not null);

        seen.Should().Equal(expected);
    }

    [Fact]
    public async Task KeepsTotalStable_AcrossPages()
    {
        await ClearIndexAsync(CancellationToken.None);
        await SeedFusedSequenceAsync(5, CancellationToken.None);

        string? cursor = null;
        for (int page = 0; page < 3; page++)
        {
            string query = "?q=acme&mode=hybrid&limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using HttpResponseMessage response = await ActAsync(query);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ReadTotalAsync(response)).Should().Be(5);
            cursor = await ReadNextCursorAsync(response);
        }

        cursor.Should().BeNull();
    }

    [Fact]
    public async Task ReturnsKeywordResults_WhenNoTracesHaveEmbeddings()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid first = new("00000001-0000-0000-0000-000000000001");
        Guid second = new("00000002-0000-0000-0000-000000000002");
        await _indexer.IndexAsync(BuildRun(first, "acme-empty-1"), CancellationToken.None);
        await _indexer.IndexAsync(BuildRun(second, "acme-empty-2"), CancellationToken.None);
        await RefreshAsync(CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=acme&mode=hybrid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().BeEquivalentTo([first, second]);
    }

    [Fact]
    public async Task ReturnsKeywordResults_WhenEmbeddingFails()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid id = new("01234567-89ab-cdef-0123-456789abcdef");
        await _indexer.IndexAsync(BuildRun(id, "acme-fail"), CancellationToken.None);
        await RefreshAsync(CancellationToken.None);

        using WebApplicationFactory<InsightsDbContext> failingFactory = InsightsTestHost.Create(_connectionString, _elasticsearchUri, new ThrowingEmbeddingClient());
        using HttpClient failingClient = failingFactory.CreateClient();

        using HttpResponseMessage response = await failingClient.GetAsync("/api/search?q=acme&mode=hybrid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().ContainSingle().Which.Should().Be(id);
    }

    [Fact]
    public async Task ReturnsKeywordResults_WhenEmbeddingTimesOut()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid id = new("01234567-89ab-cdef-0123-456789abcdef");
        await _indexer.IndexAsync(BuildRun(id, "acme-timeout"), CancellationToken.None);
        await RefreshAsync(CancellationToken.None);

        // The fake raises TaskCanceledException directly while the request token is NOT
        // cancelled — mimicking HttpClient's own timeout — so the vector leg must degrade
        // to keyword-only instead of failing the whole hybrid request.
        using WebApplicationFactory<InsightsDbContext> timeoutFactory = InsightsTestHost.Create(_connectionString, _elasticsearchUri, new TimeoutThrowingEmbeddingClient());
        using HttpClient timeoutClient = timeoutFactory.CreateClient();

        using HttpResponseMessage response = await timeoutClient.GetAsync("/api/search?q=acme&mode=hybrid");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().ContainSingle().Which.Should().Be(id);
    }

    public async Task InitializeAsync()
    {
        ElasticsearchFixture fixture = await SharedElasticsearch.GetFixtureAsync();
        _elasticsearchUri = fixture.Uri.ToString();
        _es = fixture.CreateClient();
        _indexer = new ElasticsearchRunIndexer(
            _es, new SearchIndexTemplateInitializer(_es, NullLogger<SearchIndexTemplateInitializer>.Instance));

        string template = await SharedPostgres.GetOrCreateTemplateAsync("telemetry", InsightsTestHost.MigrateTelemetryAsync);
        _connectionString = await SharedPostgres.CloneAsync(template);
        _cloneDatabaseName = new NpgsqlConnectionStringBuilder(_connectionString).Database!;

        _factory = InsightsTestHost.Create(_connectionString, _elasticsearchUri, new FakeEmbeddingClient(UnitVectorWithX(1.0)));
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await SharedPostgres.DropDatabaseAsync(_cloneDatabaseName);
    }

    private Task<HttpResponseMessage> ActAsync(string queryString) => _client.GetAsync($"/api/search{queryString}");

    private async Task<IReadOnlyList<Guid>> SeedFusedSequenceAsync(int count, CancellationToken cancellationToken)
    {
        Guid keywordDoc = new("00000009-0000-0000-0000-000000000009");
        await SeedWithEmbeddingAsync(keywordDoc, "acme-vector", UnitVectorWithX(1.0), cancellationToken: cancellationToken);
        var expected = new List<Guid> { keywordDoc };
        for (int index = 0; index < count - 1; index++)
        {
            Guid id = new($"{index:00000000}-0000-0000-0000-000000000000");
            expected.Add(id);
            await SeedWithEmbeddingAsync(id, $"zzz-vector-{index:00}", UnitVectorWithX(0.95 - 0.05 * index), cancellationToken: cancellationToken);
        }
        return expected;
    }

    private async Task SeedWithEmbeddingAsync(Guid id, string sessionId, float[] embedding, string? modelSlug = null, CancellationToken cancellationToken = default)
    {
        await _indexer.IndexAsync(BuildRun(id, sessionId, modelSlug: modelSlug), cancellationToken);
        UpdateResponse<RunIndexDocument> update = await _es.UpdateAsync<RunIndexDocument, RunEmbeddingPartial>(
            IndexName,
            id.ToString("D"),
            descriptor => descriptor.Doc(new RunEmbeddingPartial { Embedding = embedding, EmbeddingText = "unused by hybrid search" }),
            cancellationToken);
        update.IsValidResponse.Should().BeTrue(update.DebugInformation);
        await RefreshAsync(cancellationToken);
    }

    private static PricedRun BuildRun(
        Guid id,
        string sessionId,
        string? workspace = null,
        string? persona = null,
        string? modelSlug = null,
        DateTimeOffset? startedAt = null,
        string source = "delegate-ledger") => new()
    {
        Id = id,
        SessionId = sessionId,
        Source = source,
        Workspace = workspace,
        Persona = persona,
        ModelSlug = modelSlug,
        Status = RunStatus.Completed,
        PricingStatus = PricingStatus.Priced,
        StartedAt = startedAt,
        InputTokens = 100,
        OutputTokens = 50,
        Version = 1
    };

    private async Task ClearIndexAsync(CancellationToken cancellationToken)
    {
        var exists = await _es.Indices.ExistsAsync(IndexName, cancellationToken);
        if (!exists.Exists)
            return;

        DeleteByQueryResponse response = await _es.DeleteByQueryAsync(
            IndexName,
            d => d.Query(q => q.MatchAll()).Refresh(true),
            cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        var response = await _es.Indices.RefreshAsync(IndexName, cancellationToken);
        response.IsValidResponse.Should().BeTrue(response.DebugInformation);
    }

    private static async Task<IReadOnlyList<Guid>> ReadHitIdsAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("hits")
            .EnumerateArray()
            .Select(hit => hit.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task<long> ReadTotalAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("total").GetInt64();
    }

    private static async Task<string?> ReadNextCursorAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement cursor = body.RootElement.GetProperty("nextCursor");
        return cursor.ValueKind == JsonValueKind.Null ? null : cursor.GetString();
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

    private sealed class ThrowingEmbeddingClient : IEmbeddingClient
    {
        public Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => throw new EmbeddingException("simulated embedding failure");
    }

    private sealed class TimeoutThrowingEmbeddingClient : IEmbeddingClient
    {
        public Task<IReadOnlyList<float>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken)
            => throw new TaskCanceledException("simulated TEI timeout");
    }
}
