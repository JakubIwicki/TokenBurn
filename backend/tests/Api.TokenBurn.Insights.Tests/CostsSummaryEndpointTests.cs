using System.Net;
using System.Text.Json;
using Api.TokenBurn.Insights.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Testing.Common.Data;

namespace Api.TokenBurn.Insights.Tests;

public sealed class CostsSummaryEndpointTests : IAsyncLifetime
{
    private static readonly DateTimeOffset BaseTime = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly List<string> _cloneDatabases = [];
    private readonly List<HttpClient> _clients = [];
    private readonly List<WebApplicationFactory<InsightsDbContext>> _factories = [];
    private string _template = null!;

    public async Task InitializeAsync()
    {
        _template = await SharedPostgres.GetOrCreateTemplateAsync("telemetry", InsightsTestHost.MigrateTelemetryAsync);
    }

    public async Task DisposeAsync()
    {
        foreach (HttpClient client in _clients)
            client.Dispose();
        foreach (WebApplicationFactory<InsightsDbContext> factory in _factories)
            await factory.DisposeAsync();
        foreach (string database in _cloneDatabases)
            await SharedPostgres.DropDatabaseAsync(database);
    }

    [Fact]
    public async Task ReturnsTotals_WhenGroupByOmitted()
    {
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-opus", "claude-opus", "researcher", BaseTime.AddHours(1), 1000, 500, 0.10m),
            CreateRun("sess-haiku", "claude-haiku", "researcher", BaseTime.AddHours(2), 2000, 1000, 0.05m));

        using HttpResponseMessage response = await client.GetAsync("/api/costs/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        JsonElement totals = body.RootElement.GetProperty("totals");
        totals.GetProperty("runCount").GetInt64().Should().Be(2);
        totals.GetProperty("inputTokens").GetInt64().Should().Be(3000);
        totals.GetProperty("outputTokens").GetInt64().Should().Be(1500);
        totals.GetProperty("costUsd").GetDecimal().Should().Be(0.15m);
        body.RootElement.GetProperty("buckets").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ReturnsBuckets_WhenGroupedByModel()
    {
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-opus", "claude-opus", "researcher", BaseTime.AddHours(1), 1000, 500, 0.10m),
            CreateRun("sess-haiku", "claude-haiku", "researcher", BaseTime.AddHours(2), 2000, 1000, 0.05m));

        using HttpResponseMessage response = await client.GetAsync("/api/costs/summary?groupBy=model");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        IReadOnlyList<JsonElement> buckets = body.RootElement.GetProperty("buckets").EnumerateArray().ToList();
        buckets.Select(bucket => bucket.GetProperty("key").GetString())
            .Should().BeEquivalentTo("claude-opus", "claude-haiku");
        JsonElement opus = buckets.Single(bucket => bucket.GetProperty("key").GetString() == "claude-opus");
        opus.GetProperty("runCount").GetInt64().Should().Be(1);
        opus.GetProperty("inputTokens").GetInt64().Should().Be(1000);
        opus.GetProperty("outputTokens").GetInt64().Should().Be(500);
        opus.GetProperty("costUsd").GetDecimal().Should().Be(0.10m);
        JsonElement haiku = buckets.Single(bucket => bucket.GetProperty("key").GetString() == "claude-haiku");
        haiku.GetProperty("runCount").GetInt64().Should().Be(1);
        haiku.GetProperty("inputTokens").GetInt64().Should().Be(2000);
        haiku.GetProperty("outputTokens").GetInt64().Should().Be(1000);
        haiku.GetProperty("costUsd").GetDecimal().Should().Be(0.05m);
        JsonElement totals = body.RootElement.GetProperty("totals");
        totals.GetProperty("runCount").GetInt64().Should().Be(2);
        totals.GetProperty("inputTokens").GetInt64().Should().Be(3000);
        totals.GetProperty("outputTokens").GetInt64().Should().Be(1500);
        totals.GetProperty("costUsd").GetDecimal().Should().Be(0.15m);
        body.RootElement.GetProperty("pricingCoverage").GetDouble().Should().Be(1.0);
    }

    [Fact]
    public async Task ReturnsUnknownBucket_ForNullGroupingKeys()
    {
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-null-persona", null, null, BaseTime.AddHours(1), 500, 250, 0.02m));

        using HttpResponseMessage response = await client.GetAsync("/api/costs/summary?groupBy=persona");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("buckets").EnumerateArray()
            .Select(bucket => bucket.GetProperty("key").GetString())
            .Should().Contain("(unknown)");
    }

    [Fact]
    public async Task ReturnsBuckets_WhenGroupedByDay()
    {
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-day-one", "claude-opus", "researcher", BaseTime.AddHours(23), 1000, 500, 0.10m),
            CreateRun("sess-day-two", "claude-opus", "researcher", BaseTime.AddDays(1).AddHours(1), 2000, 1000, 0.20m));

        using HttpResponseMessage response = await client.GetAsync("/api/costs/summary?groupBy=day");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("buckets").EnumerateArray()
            .Select(bucket => bucket.GetProperty("key").GetString())
            .Should().Equal("2026-01-01", "2026-01-02");
    }

    [Fact]
    public async Task ReturnsTokenWeightedCoverage()
    {
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-priced", "claude-opus", "researcher", BaseTime.AddHours(1), 1000, 0, 0.10m),
            CreateRun("sess-unpriceable", "deepseek-v4-flash", "researcher", BaseTime.AddHours(2), 3000, 0, null, pricingStatus: "Unpriceable"));

        using HttpResponseMessage response = await client.GetAsync("/api/costs/summary");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("totals").GetProperty("runCount").GetInt64().Should().Be(2);
        body.RootElement.GetProperty("totals").GetProperty("inputTokens").GetInt64().Should().Be(4000);
        body.RootElement.GetProperty("pricingCoverage").GetDouble().Should().Be(0.25);
    }

    [Fact]
    public async Task ReturnsRunsInRange_WhenFromToProvided()
    {
        AgentRunReadModel inside = CreateRun("sess-inside", "claude-opus", "researcher", BaseTime.AddHours(10), 1000, 500, 0.10m);
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-before", "claude-opus", "researcher", BaseTime.AddHours(8), 1000, 500, 0.10m),
            inside);

        using HttpResponseMessage response = await client.GetAsync("/api/costs/summary?from=2026-01-01T09:30:00Z&to=2026-01-01T10:30:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("totals").GetProperty("runCount").GetInt64().Should().Be(1);
        body.RootElement.GetProperty("totals").GetProperty("inputTokens").GetInt64().Should().Be(1000);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenGroupByInvalid()
    {
        HttpClient client = await CreateSutAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/costs/summary?groupBy=bogus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenToBeforeFrom()
    {
        HttpClient client = await CreateSutAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/costs/summary?from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsUnauthorized_WhenAnonymous()
    {
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-anon", "claude-opus", "researcher", BaseTime.AddHours(1), 1000, 500, 0.10m));
        using HttpRequestMessage message = new(HttpMethod.Get, "/api/costs/summary");
        message.Headers.Add(InsightsTestHost.NoAuthHeader, "true");

        using HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> CreateSutAsync(params AgentRunReadModel[] runs)
    {
        string connectionString = await SharedPostgres.CloneAsync(_template);
        _cloneDatabases.Add(new NpgsqlConnectionStringBuilder(connectionString).Database!);

        var options = new DbContextOptionsBuilder<InsightsDbContext>().UseNpgsql(connectionString).Options;
        await using (InsightsDbContext db = new(options))
        {
            var testDb = new TestDb(db);
            foreach (AgentRunReadModel run in runs)
                testDb.Store(run);
        }

        WebApplicationFactory<InsightsDbContext> factory = InsightsTestHost.Create(connectionString);
        _factories.Add(factory);
        HttpClient client = factory.CreateClient();
        _clients.Add(client);
        return client;
    }

    private static AgentRunReadModel CreateRun(
        string sessionId,
        string? model,
        string? persona,
        DateTimeOffset? startedAt,
        long inputTokens,
        long outputTokens,
        decimal? costUsd,
        decimal? reportedCostUsd = null,
        string pricingStatus = "Priced",
        long cacheReadTokens = 0,
        long cacheWriteTokens = 0)
    {
        return new AgentRunReadModel
        {
            Id = Guid.NewGuid(),
            SessionId = sessionId,
            Source = "delegate-ledger",
            Status = "Completed",
            PricingStatus = pricingStatus,
            StartedAt = startedAt,
            Persona = persona,
            ModelSlug = model,
            InputTokens = inputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
            OutputTokens = outputTokens,
            CostUsd = costUsd,
            ReportedCostUsd = reportedCostUsd
        };
    }

    private static async Task AssertErrorsAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }
}
