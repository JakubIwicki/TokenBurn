using System.Net;
using System.Text.Json;
using Api.TokenBurn.Insights.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Testing.Common.Data;

namespace Api.TokenBurn.Insights.Tests;

public sealed class RunsEndpointTests : IAsyncLifetime
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
    public async Task PagesAllRuns_WhenMoreThanLimit()
    {
        AgentRun[] runs = Enumerable.Range(0, 25)
            .Select(i => CreateRun($"sess-{i:00}", "deepseek-v4-flash", "researcher", BaseTime.AddMinutes(i / 4)))
            .ToArray();
        HttpClient client = await CreateSutAsync(runs);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            string query = "/api/runs?limit=5" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using HttpResponseMessage response = await client.GetAsync(query);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            seen.AddRange(await ReadRunIdsAsync(response));
            cursor = await ReadNextCursorAsync(response);
        } while (cursor is not null);

        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(runs.Select(run => run.Id));
        seen.Should().HaveCount(runs.Length);
    }

    [Fact]
    public async Task PagesEveryRun_WhenNullStartedRunsExceedPageLimit()
    {
        AgentRun[] nullRuns = Enumerable.Range(0, 5)
            .Select(i => CreateRun($"sess-null-{i:00}", "deepseek-v4-flash", "researcher", null))
            .ToArray();
        AgentRun[] datedRuns = Enumerable.Range(0, 7)
            .Select(i => CreateRun($"sess-dated-{i:00}", "deepseek-v4-flash", "researcher", BaseTime.AddHours(i)))
            .ToArray();
        AgentRun[] seeded = [.. nullRuns, .. datedRuns];
        HttpClient client = await CreateSutAsync(seeded);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            string query = "/api/runs?limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using HttpResponseMessage response = await client.GetAsync(query);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            seen.AddRange(await ReadRunIdsAsync(response));
            cursor = await ReadNextCursorAsync(response);
        } while (cursor is not null);

        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(seeded.Select(run => run.Id));
        seen.Should().HaveCount(seeded.Length);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenFromIsMalformed()
    {
        HttpClient client = await CreateSutAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/runs?from=2026-13-99");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenCursorIsMalformed()
    {
        HttpClient client = await CreateSutAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/runs?cursor=garbage");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsOnlyRunsInRange_WhenBoundedByFromAndTo()
    {
        AgentRun inside = CreateRun("sess-range-inside", "deepseek-v4-flash", "researcher", BaseTime.AddHours(10));
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-range-early", "deepseek-v4-flash", "researcher", BaseTime.AddHours(8)),
            inside,
            CreateRun("sess-range-late", "deepseek-v4-flash", "researcher", BaseTime.AddHours(11)));

        using HttpResponseMessage response = await client.GetAsync("/api/runs?from=2026-01-01T09:30:00Z&to=2026-01-01T10:30:00Z");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadRunIdsAsync(response)).Should().ContainSingle().Which.Should().Be(inside.Id);
    }

    [Fact]
    public async Task ReturnsOnlyRuns_WhenFilteredByModel()
    {
        AgentRun matching = CreateRun("sess-model-match", "claude-opus-5", "researcher", BaseTime.AddHours(1));
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-model-other", "deepseek-v4-flash", "researcher", BaseTime.AddHours(1)),
            matching);

        using HttpResponseMessage response = await client.GetAsync("/api/runs?model=claude-opus-5");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadRunIdsAsync(response)).Should().ContainSingle().Which.Should().Be(matching.Id);
    }

    [Fact]
    public async Task ReturnsOnlyRuns_WhenFilteredByPersona()
    {
        AgentRun matching = CreateRun("sess-persona-match", "deepseek-v4-flash", "engineer", BaseTime.AddHours(1));
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-persona-other", "deepseek-v4-flash", "researcher", BaseTime.AddHours(1)),
            matching);

        using HttpResponseMessage response = await client.GetAsync("/api/runs?persona=engineer");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadRunIdsAsync(response)).Should().ContainSingle().Which.Should().Be(matching.Id);
    }

    [Fact]
    public async Task ReturnsOnlyRunsAtOrAboveCost_WhenMinCostSpecified()
    {
        AgentRun priced = CreateRun("sess-cost-priced", "deepseek-v4-flash", "researcher", BaseTime.AddHours(1), costUsd: 2.0m);
        HttpClient client = await CreateSutAsync(
            CreateRun("sess-cost-cheap", "deepseek-v4-flash", "researcher", BaseTime.AddHours(1), costUsd: 1.0m),
            CreateRun("sess-cost-unpriced", "deepseek-v4-flash", "researcher", BaseTime.AddHours(1)),
            priced);

        using HttpResponseMessage response = await client.GetAsync("/api/runs?minCost=2");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadRunIdsAsync(response)).Should().ContainSingle().Which.Should().Be(priced.Id);
    }

    [Fact]
    public async Task ReturnsRunDetail_WithEmptyMessagesAndFindings_WhenRunExists()
    {
        AgentRun run = CreateRun("sess-detail", "deepseek-v4-flash", "researcher", BaseTime.AddHours(1), costUsd: 1.5m);
        HttpClient client = await CreateSutAsync(run);

        using HttpResponseMessage response = await client.GetAsync($"/api/runs/{run.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("run").GetProperty("id").GetGuid().Should().Be(run.Id);
        body.RootElement.GetProperty("run").GetProperty("sessionId").GetString().Should().Be(run.SessionId);
        body.RootElement.GetProperty("messages").GetArrayLength().Should().Be(0);
        body.RootElement.GetProperty("findings").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task ReturnsNotFound_WhenRunDoesNotExist()
    {
        HttpClient client = await CreateSutAsync();

        using HttpResponseMessage response = await client.GetAsync($"/api/runs/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReturnsUnauthorized_WhenAnonymous()
    {
        HttpClient client = await CreateSutAsync(CreateRun("sess-anon", "deepseek-v4-flash", "researcher", BaseTime.AddHours(1)));
        using HttpRequestMessage message = new(HttpMethod.Get, "/api/runs");
        message.Headers.Add(InsightsTestHost.NoAuthHeader, "true");

        using HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> CreateSutAsync(params AgentRun[] runs)
    {
        string connectionString = await SharedPostgres.CloneAsync(_template);
        _cloneDatabases.Add(new NpgsqlConnectionStringBuilder(connectionString).Database!);

        var options = new DbContextOptionsBuilder<TelemetryDbContext>().UseNpgsql(connectionString).Options;
        await using (TelemetryDbContext db = new(options))
        {
            var testDb = new TestDb(db);
            foreach (AgentRun run in runs)
                testDb.Store(run);
        }

        WebApplicationFactory<InsightsDbContext> factory = InsightsTestHost.Create(connectionString);
        _factories.Add(factory);
        HttpClient client = factory.CreateClient();
        _clients.Add(client);
        return client;
    }

    private static AgentRun CreateRun(string sessionId, string model, string persona, DateTimeOffset? startedAt, decimal? costUsd = null)
    {
        AgentRun run = AgentRun.Create(
            sessionId, "agent-1", "delegate-ledger", externalId: null, persona, model,
            RunStatus.Completed, startedAt, startedAt?.AddMinutes(1),
            inputTokens: 100, cacheReadTokens: 0, cacheWriteTokens: 0, outputTokens: 50,
            reportedCostUsd: null);
        if (costUsd is not null)
            run.TryMarkPriced(costUsd.Value, 1.0m).IsSuccess.Should().BeTrue();
        return run;
    }

    private static async Task<IReadOnlyList<Guid>> ReadRunIdsAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("runs")
            .EnumerateArray()
            .Select(run => run.GetProperty("id").GetGuid())
            .ToList();
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
