using System.Net;
using System.Text;
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

public sealed class FindingsEndpointTests : IAsyncLifetime
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
    public async Task PagesAllFindings_WhenMoreThanLimit()
    {
        WasteFinding[] findings = Enumerable.Range(0, 25)
            .Select(i => CreateFinding(
                PickKind(i),
                WasteFindingSeverity.Minor,
                BaseTime.AddMinutes(i / 4),
                seed: i))
            .ToArray();
        HttpClient client = await CreateSutAsync(findings);

        var seen = new List<Guid>();
        string? cursor = null;
        do
        {
            string query = "/api/findings?limit=5" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using HttpResponseMessage response = await client.GetAsync(query);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            IReadOnlyList<JsonElement> page = body.RootElement.GetProperty("findings").EnumerateArray().ToList();
            // Privacy boundary: the summary surface never carries evidence content.
            foreach (JsonElement finding in page)
                finding.TryGetProperty("evidence", out _).Should().BeFalse();
            seen.AddRange(page.Select(finding => finding.GetProperty("id").GetGuid()));
            JsonElement nextCursor = body.RootElement.GetProperty("nextCursor");
            cursor = nextCursor.ValueKind == JsonValueKind.Null ? null : nextCursor.GetString();
        } while (cursor is not null);

        seen.Should().OnlyHaveUniqueItems();
        seen.Should().BeEquivalentTo(findings.Select(finding => finding.Id));
        seen.Should().HaveCount(findings.Length);
    }

    [Fact]
    public async Task FiltersByKind()
    {
        WasteFinding contextReplay = CreateFinding(WasteFindingKind.ContextReplay, WasteFindingSeverity.Minor, BaseTime.AddHours(1), seed: 1);
        HttpClient client = await CreateSutAsync(
            CreateFinding(WasteFindingKind.Loop, WasteFindingSeverity.Minor, BaseTime.AddHours(1), seed: 2),
            CreateFinding(WasteFindingKind.CostThreshold, WasteFindingSeverity.Minor, BaseTime.AddHours(1), seed: 3),
            contextReplay);

        using HttpResponseMessage response = await client.GetAsync("/api/findings?kind=ContextReplay");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadFindingIdsAsync(response)).Should().ContainSingle().Which.Should().Be(contextReplay.Id);
    }

    [Fact]
    public async Task FiltersBySeverity()
    {
        WasteFinding critical = CreateFinding(WasteFindingKind.ContextReplay, WasteFindingSeverity.Critical, BaseTime.AddHours(1), seed: 1);
        HttpClient client = await CreateSutAsync(
            CreateFinding(WasteFindingKind.Loop, WasteFindingSeverity.Minor, BaseTime.AddHours(1), seed: 2),
            critical);

        using HttpResponseMessage response = await client.GetAsync("/api/findings?severity=Critical");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadFindingIdsAsync(response)).Should().ContainSingle().Which.Should().Be(critical.Id);
    }

    [Fact]
    public async Task FiltersByAcknowledged()
    {
        WasteFinding acknowledged = CreateFinding(WasteFindingKind.ContextReplay, WasteFindingSeverity.Minor, BaseTime.AddHours(1), seed: 1);
        acknowledged.TryAcknowledge(BaseTime.AddHours(2)).IsSuccess.Should().BeTrue();
        WasteFinding open = CreateFinding(WasteFindingKind.Loop, WasteFindingSeverity.Minor, BaseTime.AddHours(1), seed: 2);
        HttpClient client = await CreateSutAsync(acknowledged, open);

        using HttpResponseMessage trueResponse = await client.GetAsync("/api/findings?acknowledged=true");

        trueResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadFindingIdsAsync(trueResponse)).Should().ContainSingle().Which.Should().Be(acknowledged.Id);

        using HttpResponseMessage falseResponse = await client.GetAsync("/api/findings?acknowledged=false");

        falseResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadFindingIdsAsync(falseResponse)).Should().ContainSingle().Which.Should().Be(open.Id);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenCursorMalformed()
    {
        HttpClient client = await CreateSutAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/findings?cursor=garbage");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenCursorDecodesToNullKey()
    {
        HttpClient client = await CreateSutAsync();
        string nullKeyCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes($"|{Guid.NewGuid():N}"));

        using HttpResponseMessage response = await client.GetAsync($"/api/findings?cursor={Uri.EscapeDataString(nullKeyCursor)}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsBadRequest_WhenKindInvalid()
    {
        HttpClient client = await CreateSutAsync();

        using HttpResponseMessage response = await client.GetAsync("/api/findings?kind=Bogus");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await AssertErrorsAsync(response);
    }

    [Fact]
    public async Task ReturnsUnauthorized_WhenAnonymous()
    {
        HttpClient client = await CreateSutAsync(CreateFinding(WasteFindingKind.ContextReplay, WasteFindingSeverity.Minor, BaseTime.AddHours(1), seed: 1));
        using HttpRequestMessage message = new(HttpMethod.Get, "/api/findings");
        message.Headers.Add(InsightsTestHost.NoAuthHeader, "true");

        using HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<HttpClient> CreateSutAsync(params WasteFinding[] findings)
    {
        string connectionString = await SharedPostgres.CloneAsync(_template);
        _cloneDatabases.Add(new NpgsqlConnectionStringBuilder(connectionString).Database!);

        var options = new DbContextOptionsBuilder<TelemetryDbContext>().UseNpgsql(connectionString).Options;
        await using (TelemetryDbContext db = new(options))
        {
            if (findings.Length > 0)
                await new FindingsUpserter(db).UpsertAsync(findings, CancellationToken.None);
        }

        WebApplicationFactory<InsightsDbContext> factory = InsightsTestHost.Create(connectionString);
        _factories.Add(factory);
        HttpClient client = factory.CreateClient();
        _clients.Add(client);
        return client;
    }

    private static WasteFinding CreateFinding(
        WasteFindingKind kind, WasteFindingSeverity severity, DateTimeOffset detectedAt,
        int seed, decimal? wastedCostUsd = null, Guid? runId = null)
        => WasteFinding.Create(
            runId ?? Guid.NewGuid(),
            kind,
            severity,
            new { seed },
            wastedCostUsd,
            detectedAt);

    private static WasteFindingKind PickKind(int i)
        => i % 3 == 0 ? WasteFindingKind.ContextReplay
            : i % 3 == 1 ? WasteFindingKind.Loop
            : WasteFindingKind.CostThreshold;

    private static async Task<IReadOnlyList<Guid>> ReadFindingIdsAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("findings")
            .EnumerateArray()
            .Select(finding => finding.GetProperty("id").GetGuid())
            .ToList();
    }

    private static async Task AssertErrorsAsync(HttpResponseMessage response)
    {
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("errors").GetArrayLength().Should().BeGreaterThan(0);
    }
}
