using System.Net;
using System.Text.Json;
using Api.TokenBurn.Insights.Persistence;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.IndexManagement;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using TokenBurn.Contracts;
using TokenBurn.Processor.Infrastructure.Indexing;
using TokenBurn.Testing.Common.Data;

namespace Api.TokenBurn.Insights.Tests;

public sealed class SearchEndpointTests : IAsyncLifetime
{
    private const string IndexName = ElasticsearchFixture.TracesIndex;
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private ElasticsearchClient _es = null!;
    private ElasticsearchRunIndexer _indexer = null!;
    private WebApplicationFactory<InsightsDbContext> _factory = null!;
    private HttpClient _client = null!;
    private string _cloneDatabaseName = null!;

    public async Task InitializeAsync()
    {
        ElasticsearchFixture fixture = await SharedElasticsearch.GetFixtureAsync();
        _es = fixture.CreateClient();
        _indexer = new ElasticsearchRunIndexer(
            _es, new SearchIndexTemplateInitializer(_es, NullLogger<SearchIndexTemplateInitializer>.Instance));

        string template = await SharedPostgres.GetOrCreateTemplateAsync("telemetry", InsightsTestHost.MigrateTelemetryAsync);
        string connectionString = await SharedPostgres.CloneAsync(template);
        _cloneDatabaseName = new NpgsqlConnectionStringBuilder(connectionString).Database!;

        _factory = InsightsTestHost.Create(connectionString, fixture.Uri.ToString());
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await SharedPostgres.DropDatabaseAsync(_cloneDatabaseName);
    }

    [Fact]
    public async Task ReturnsHits_WithHighlights_WhenKeywordMatches()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid runId = new("01234567-89ab-cdef-0123-456789abcdef");
        await _indexer.IndexAsync(BuildRun(runId, "sess-highlight", workspace: "acme-widgets"), CancellationToken.None);
        await RefreshAsync(CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=acme");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().ContainSingle().Which.Should().Be(runId);
        IReadOnlyList<IReadOnlyList<string>> highlights = await ReadHighlightsAsync(response);
        highlights.Should().ContainSingle()
            .Which.Should().Contain(fragment => fragment.Contains("<em>acme</em>"));
    }

    [Fact]
    public async Task ReturnsEmptyHits_WhenNoTermMatches()
    {
        await ClearIndexAsync(CancellationToken.None);
        await _indexer.IndexAsync(BuildRun(new Guid("01234567-89ab-cdef-0123-456789abcdef"), "sess-nomatch", workspace: "acme-widgets"), CancellationToken.None);
        await RefreshAsync(CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=zzz-absent");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadTotalAsync(response)).Should().Be(0);
        (await ReadHitIdsAsync(response)).Should().BeEmpty();
        (await ReadHighlightsAsync(response)).Should().BeEmpty();
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenQueryIsEmpty()
    {
        using HttpResponseMessage response = await ActAsync("?q=");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenFromIsMalformed()
    {
        using HttpResponseMessage response = await ActAsync("?q=acme&from=2026-13-99");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenCursorIsMalformed()
    {
        using HttpResponseMessage response = await ActAsync("?q=acme&cursor=garbage");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsEmptyHits_WhenIndexDoesNotExist()
    {
        await _es.Indices.DeleteAsync(IndexName, CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=acme");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadTotalAsync(response)).Should().Be(0);
        (await ReadHitIdsAsync(response)).Should().BeEmpty();
        (await ReadNextCursorAsync(response)).Should().BeNull();
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenModeIsHybrid()
    {
        using HttpResponseMessage response = await ActAsync("?q=acme&mode=hybrid");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task PagesAllDocuments_WhenMoreThanLimit()
    {
        await ClearIndexAsync(CancellationToken.None);
        var seeded = new List<Guid>();
        for (int i = 0; i < 25; i++)
        {
            Guid id = new($"{i:00000000}-0000-0000-0000-000000000000");
            seeded.Add(id);
            await _indexer.IndexAsync(BuildRun(id, $"sess-page-{i:00}", startedAt: BaseTime.AddMinutes(i)), CancellationToken.None);
        }
        await RefreshAsync(CancellationToken.None);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            string query = "?q=page&limit=5" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using HttpResponseMessage response = await ActAsync(query);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            seen.AddRange(await ReadHitIdsAsync(response));
            cursor = await ReadNextCursorAsync(response);
        } while (cursor is not null);

        seen.Distinct().Should().BeEquivalentTo(seeded);
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().HaveCount(seeded.Count);
    }

    [Fact]
    public async Task PagesEveryDocument_WhenNullStartedAtSpansPageBoundary()
    {
        await ClearIndexAsync(CancellationToken.None);
        var seeded = new List<Guid>();
        for (int i = 0; i < 6; i++)
        {
            Guid id = new($"{i:00000000}-0000-0000-0000-000000000000");
            seeded.Add(id);
            await _indexer.IndexAsync(BuildRun(id, $"sess-null-boundary-{i:00}", startedAt: null), CancellationToken.None);
        }
        for (int i = 6; i < 12; i++)
        {
            Guid id = new($"{i:00000000}-0000-0000-0000-000000000000");
            seeded.Add(id);
            await _indexer.IndexAsync(BuildRun(id, $"sess-null-boundary-{i:00}", startedAt: BaseTime.AddHours(i)), CancellationToken.None);
        }
        await RefreshAsync(CancellationToken.None);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            string query = "?q=null-boundary&limit=5" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using HttpResponseMessage response = await ActAsync(query);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            (await ReadTotalAsync(response)).Should().Be(seeded.Count);
            seen.AddRange(await ReadHitIdsAsync(response));
            cursor = await ReadNextCursorAsync(response);
        } while (cursor is not null);

        seen.Distinct().Should().BeEquivalentTo(seeded);
        seen.Should().OnlyHaveUniqueItems();
        seen.Should().HaveCount(seeded.Count);
    }

    [Fact]
    public async Task ReturnsOnlyMatchingDocs_WhenFilteredByModel()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid matching = new("20000000-0000-0000-0000-000000000000");
        await _indexer.IndexAsync(BuildRun(new Guid("10000000-0000-0000-0000-000000000000"), "filt-other", modelSlug: "deepseek-v4-flash"), CancellationToken.None);
        await _indexer.IndexAsync(BuildRun(matching, "filt-match", modelSlug: "claude-opus-5"), CancellationToken.None);
        await RefreshAsync(CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=filt&model=claude-opus-5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().ContainSingle().Which.Should().Be(matching);
    }

    [Fact]
    public async Task ReturnsOnlyMatchingDocs_WhenFilteredByPersona()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid matching = new("40000000-0000-0000-0000-000000000000");
        await _indexer.IndexAsync(BuildRun(new Guid("30000000-0000-0000-0000-000000000000"), "filt-persona-other", persona: "researcher"), CancellationToken.None);
        await _indexer.IndexAsync(BuildRun(matching, "filt-persona-match", persona: "engineer"), CancellationToken.None);
        await RefreshAsync(CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=filt&persona=engineer");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().ContainSingle().Which.Should().Be(matching);
    }

    [Fact]
    public async Task ReturnsOnlyDocsInRange_WhenBoundedByFromAndTo()
    {
        await ClearIndexAsync(CancellationToken.None);
        Guid inside = new("60000000-0000-0000-0000-000000000000");
        await _indexer.IndexAsync(BuildRun(new Guid("50000000-0000-0000-0000-000000000000"), "filt-range-early", startedAt: BaseTime.AddHours(8)), CancellationToken.None);
        await _indexer.IndexAsync(BuildRun(inside, "filt-range-inside", startedAt: BaseTime.AddHours(10)), CancellationToken.None);
        await _indexer.IndexAsync(BuildRun(new Guid("70000000-0000-0000-0000-000000000000"), "filt-range-late", startedAt: BaseTime.AddHours(11)), CancellationToken.None);
        await RefreshAsync(CancellationToken.None);

        using HttpResponseMessage response = await ActAsync("?q=filt&from=2026-01-01T09:30:00Z&to=2026-01-01T10:30:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadHitIdsAsync(response)).Should().ContainSingle().Which.Should().Be(inside);
    }

    [Fact]
    public async Task ReturnsUnauthorized_WhenAnonymous()
    {
        using HttpRequestMessage message = new(HttpMethod.Get, "/api/search?q=acme");
        message.Headers.Add(InsightsTestHost.NoAuthHeader, "true");

        using HttpResponseMessage response = await _client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private Task<HttpResponseMessage> ActAsync(string queryString) => _client.GetAsync($"/api/search{queryString}");

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

    private static async Task<IReadOnlyList<IReadOnlyList<string>>> ReadHighlightsAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("highlights")
            .EnumerateArray()
            .Select(fragments => (IReadOnlyList<string>)fragments.EnumerateArray().Select(fragment => fragment.GetString()!).ToList())
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

    private static async Task AssertErrorsAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }
}
