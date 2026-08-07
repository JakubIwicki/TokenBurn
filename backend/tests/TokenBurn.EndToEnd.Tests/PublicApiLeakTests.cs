extern alias insights;
extern alias processor;

using System.Linq;
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TelemetryDb = processor::TokenBurn.Processor.Persistence.TelemetryDbContext;
using AgentRun = processor::TokenBurn.Processor.Domain.AgentRun;
using AgentMessage = processor::TokenBurn.Processor.Domain.AgentMessage;
using RunStatus = processor::TokenBurn.Processor.Domain.RunStatus;
using PricingStatus = processor::TokenBurn.Processor.Domain.PricingStatus;
using AgentRunUpserter = processor::TokenBurn.Processor.Persistence.AgentRunUpserter;
using AgentMessageUpserter = processor::TokenBurn.Processor.Persistence.AgentMessageUpserter;
using PricingEngine = processor::TokenBurn.Processor.Pricing.PricingEngine;
using CostCalculator = processor::TokenBurn.Processor.Pricing.CostCalculator;
using PriceMultiplier = processor::TokenBurn.Processor.Pricing.PriceMultiplier;
using AggregateRebuildService = processor::TokenBurn.Processor.Aggregation.AggregateRebuildService;

namespace TokenBurn.EndToEnd.Tests;

/// <summary>
///     Phase 8 gate — privacy-boundary rule 5 leak test against the two anonymous model
///     endpoints (<c>/api/models</c> and <c>/api/models/stats</c>). Seeds known secret-shaped
///     strings into the private corpus (agent-run identity fields, workspace, and message
///     content) on the SURVIVING positive-control bucket (6 priced deepseek-v4-flash runs), so
///     the absence assertions are falsifiable: the bucket reaches the projection (the stats
///     entry must show runCount ≥ 6), yet neither the probe nor the secret path may appear
///     anywhere in either response body. Also pins the allow-listed JSON shape of every entry,
///     the five-minute Cache-Control on both responses, and that <c>/api/runs</c> stays
///     authorization-gated (401 anonymous).
/// </summary>
public sealed class PublicApiLeakTests : IClassFixture<TelemetryPipelineE2EFixture>
{
    private const string Probe = "sk-aggregate-probe-9f8e7d6c5b4a3210";
    private const string SecretPath = "/var/secrets/tokenburn/trading-key.pem";
    private const string PositiveControlModel = "deepseek-v4-flash";
    private const string DelegateLedgerService = "delegate-ledger";
    private const int PositiveControlCount = 6;
    private const string CacheControlValue = "public, max-age=300";

    // Fixed UTC instant → bucket_day 2026-08-06. No live clock anywhere in the seed.
    private static readonly DateTimeOffset SeedInstant = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BucketDay = new(2026, 8, 6);
    private static readonly TimeSpan CacheWarmTimeout = TimeSpan.FromSeconds(60);

    // Allow-listed JSON keys of the public projections (privacy-boundary rule 8). Every entry
    // must carry EXACTLY these keys — a new field is a leak surface and fails this shape check.
    private static readonly string[] DirectoryEntryKeys =
    [
        "slug", "provider", "contextWindow", "inputPerMtok", "cacheReadPerMtok",
        "cacheWritePerMtok", "outputPerMtok"
    ];

    private static readonly string[] StatsEntryKeys =
    [
        "modelSlug", "service", "runCount", "pricedRunCount", "messageCount", "inputTokens",
        "cacheReadTokens", "cacheWriteTokens", "outputTokens", "costUsd"
    ];

    // Root objects carry exactly the allow-listed collection key — a top-level field added to
    // either response type is a leak surface and fails this shape check.
    private static readonly string[] DirectoryRootKeys = ["models"];
    private static readonly string[] StatsRootKeys = ["stats"];

    private readonly TelemetryPipelineE2EFixture _fixture;

    public PublicApiLeakTests(TelemetryPipelineE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AnonymousModelEndpoints_DoNotLeakProbeOrSecretPath()
    {
        await SeedPrivateCorpusAsync(CancellationToken.None);

        int bucketCount = await RebuildAsync(CancellationToken.None);
        Assert.Equal(1, bucketCount);

        using WebApplicationFactory<insights::Program> insightsFactory = BuildInsightsHost();
        using HttpClient client = insightsFactory.CreateClient();

        bool warmed = await WaitUntilAsync(
            () => StatsHasDeepSeekEntryAsync(client, CancellationToken.None),
            CacheWarmTimeout);
        Assert.True(warmed,
            "The metrics.aggregate cache did not warm to a deepseek-v4-flash stats entry within the timeout.");

        using HttpResponseMessage directoryResponse = await client.GetAsync("/api/models", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, directoryResponse.StatusCode);
        string directoryBody = await directoryResponse.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.DoesNotContain(Probe, directoryBody, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretPath, directoryBody, StringComparison.Ordinal);
        AssertCacheControl(directoryResponse);
        bool directoryHasPositiveControl = false;
        using (JsonDocument directoryDocument = JsonDocument.Parse(directoryBody))
        {
            AssertDirectoryRootAllowList(directoryDocument.RootElement);
            foreach (JsonElement entry in directoryDocument.RootElement.GetProperty("models").EnumerateArray())
            {
                AssertDirectoryEntryAllowList(entry);
                directoryHasPositiveControl |= entry.GetProperty("slug").GetString() == PositiveControlModel;
            }
        }
        Assert.True(directoryHasPositiveControl,
            $"/api/models must list the {PositiveControlModel} price-registry row.");

        using HttpResponseMessage statsResponse = await client.GetAsync("/api/models/stats", CancellationToken.None);
        Assert.Equal(HttpStatusCode.OK, statsResponse.StatusCode);
        string statsBody = await statsResponse.Content.ReadAsStringAsync(CancellationToken.None);
        Assert.DoesNotContain(Probe, statsBody, StringComparison.Ordinal);
        Assert.DoesNotContain(SecretPath, statsBody, StringComparison.Ordinal);
        AssertCacheControl(statsResponse);
        long positiveControlRunCount = 0;
        using (JsonDocument statsDocument = JsonDocument.Parse(statsBody))
        {
            AssertStatsRootAllowList(statsDocument.RootElement);
            foreach (JsonElement entry in statsDocument.RootElement.GetProperty("stats").EnumerateArray())
            {
                AssertStatsEntryAllowList(entry);
                if (entry.GetProperty("modelSlug").GetString() == PositiveControlModel
                    && entry.GetProperty("service").GetString() == DelegateLedgerService)
                {
                    positiveControlRunCount = entry.GetProperty("runCount").GetInt64();
                }
            }
        }
        Assert.True(positiveControlRunCount >= PositiveControlCount,
            $"The {PositiveControlModel}/{DelegateLedgerService} stats row must show at least " +
            $"{PositiveControlCount} runs; found {positiveControlRunCount}.");

        // Regression: the anonymous surface is exactly the two model endpoints — /api/runs
        // remains authorization-gated and must reject an unauthenticated caller.
        using HttpResponseMessage runsResponse = await client.GetAsync("/api/runs", CancellationToken.None);
        Assert.Equal(HttpStatusCode.Unauthorized, runsResponse.StatusCode);
    }

    private WebApplicationFactory<insights::Program> BuildInsightsHost()
    {
        return new WebApplicationFactory<insights::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Insights", ReadInsightsConnectionString());
            builder.UseSetting("Kafka:BootstrapServers", _fixture.KafkaBootstrapServers);
            // The client is resolved lazily and never constructed on this test path, so the
            // real container address is set for fidelity; username/password are empty because
            // the fixture's Elasticsearch runs with xpack.security.enabled=false.
            builder.UseSetting("Elasticsearch:Uri", ReadElasticsearchUri());
            builder.UseSetting("Elasticsearch:Username", "");
            builder.UseSetting("Elasticsearch:Password", "");
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            builder.UseSetting("Ask:Provider", "fake");
            builder.UseSetting("Ask:DeepSeekEndpoint", "http://localhost:9999");
            builder.UseSetting("Embeddings:Uri", "");
        });
    }

    private string ReadInsightsConnectionString()
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        return db.Database.GetDbConnection().ConnectionString;
    }

    private string ReadElasticsearchUri()
        => _fixture.ElasticsearchClient.ElasticsearchClientSettings.NodePool.Nodes.First().Uri.ToString();

    private async Task SeedPrivateCorpusAsync(CancellationToken ct)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();

        for (int index = 1; index <= PositiveControlCount; index++)
        {
            AgentRun run = AgentRun.Create(
                sessionId: $"seed-pos-{index}-{Probe}", agentId: "", source: DelegateLedgerService,
                externalId: Probe, persona: Probe, modelSlug: PositiveControlModel,
                status: RunStatus.Completed, startedAt: SeedInstant, endedAt: SeedInstant.AddMinutes(5),
                inputTokens: 1000 + index, cacheReadTokens: 100, cacheWriteTokens: 200,
                outputTokens: 300 + index, reportedCostUsd: null, service: DelegateLedgerService,
                workspace: SecretPath);
            Guid storedId = await SeedPricedRunAsync(db, run, ct);

            AgentMessage probeMessage = AgentMessage.Create(
                runId: storedId, sequence: 0, role: "user", content: Probe, toolName: null,
                modelSlug: run.ModelSlug, inputTokens: 10, cacheReadTokens: 0, cacheWriteTokens: 0,
                outputTokens: 5, occurredAt: SeedInstant);
            await SeedPricedMessageAsync(db, run, probeMessage, ct);
        }
    }

    private async Task<int> RebuildAsync(CancellationToken ct)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        AggregateRebuildService rebuild = scope.ServiceProvider.GetRequiredService<AggregateRebuildService>();
        return await rebuild.RebuildAsync(ct);
    }

    /// <summary>
    ///     Prices the run against the real price registry, then upserts it. The registry is keyed
    ///     per provider (deepseek/anthropic/...) — never "delegate-ledger" — so a run carrying
    ///     Service = "delegate-ledger" resolves to no row and PriceRunAsync would leave it
    ///     Quarantined. Resolve with a null service (the no-service path a run without a service
    ///     takes — which is how the E2E seed prices) and mark the run priced with the real registry
    ///     cost. This is exactly the computation PriceRunAsync would perform for that run; only the
    ///     service argument differs.
    /// </summary>
    private static async Task<Guid> SeedPricedRunAsync(TelemetryDb db, AgentRun run, CancellationToken ct)
    {
        var resolved = await new PricingEngine(db).ResolveAsync(run.ModelSlug, null, SeedInstant, ct);
        Assert.True(resolved.IsSuccess, $"No price row for model slug '{run.ModelSlug}' as of {SeedInstant:O}.");
        decimal multiplier = PriceMultiplier.For(SeedInstant);
        decimal cost = CostCalculator.Compute(
            run.InputTokens, run.CacheReadTokens, run.CacheWriteTokens, run.OutputTokens,
            resolved.Value!, multiplier);
        var marked = run.TryMarkPriced(cost, multiplier);
        Assert.True(marked.IsSuccess, $"Pricing run '{run.SessionId}' failed: {marked.ErrorMessage}");
        Assert.Equal(PricingStatus.Priced, run.PricingStatus);

        (Guid storedId, bool applied) = await new AgentRunUpserter(db).UpsertAsync(run, ct);
        Assert.True(applied, $"Upserting run '{run.SessionId}' must apply on first insert.");
        return storedId;
    }

    private static async Task SeedPricedMessageAsync(TelemetryDb db, AgentRun run, AgentMessage message, CancellationToken ct)
    {
        var messagePricing = await new PricingEngine(db).PriceMessagesAsync(run, [message], ct);
        Assert.True(messagePricing.IsSuccess, $"Pricing messages for run '{run.SessionId}' failed: {messagePricing.ErrorMessage}");
        await new AgentMessageUpserter(db).UpsertAsync(message.RunId, [message], ct);
    }

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan timeout)
    {
        using CancellationTokenSource timeoutSource = new(timeout);
        while (!timeoutSource.IsCancellationRequested)
        {
            if (await condition())
                return true;

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), timeoutSource.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
        return false;
    }

    private static async Task<bool> StatsHasDeepSeekEntryAsync(HttpClient client, CancellationToken ct)
    {
        using HttpResponseMessage response = await client.GetAsync("/api/models/stats", ct);
        if (response.StatusCode != HttpStatusCode.OK)
            return false;

        string body = await response.Content.ReadAsStringAsync(ct);
        using JsonDocument document = JsonDocument.Parse(body);
        foreach (JsonElement entry in document.RootElement.GetProperty("stats").EnumerateArray())
        {
            if (entry.GetProperty("modelSlug").GetString() == PositiveControlModel)
                return true;
        }
        return false;
    }

    private static void AssertCacheControl(HttpResponseMessage response)
    {
        string cacheControl = Assert.Single(response.Headers.GetValues("Cache-Control"));
        Assert.Equal(CacheControlValue, cacheControl);
    }

    private static void AssertDirectoryRootAllowList(JsonElement root)
    {
        HashSet<string> actual = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(actual.SetEquals(DirectoryRootKeys),
            $"Model directory response root carries non-allow-listed keys: {string.Join(", ", actual)}");
    }

    private static void AssertStatsRootAllowList(JsonElement root)
    {
        HashSet<string> actual = root.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(actual.SetEquals(StatsRootKeys),
            $"Model stats response root carries non-allow-listed keys: {string.Join(", ", actual)}");
    }

    private static void AssertDirectoryEntryAllowList(JsonElement entry)
    {
        HashSet<string> actual = entry.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(actual.SetEquals(DirectoryEntryKeys),
            $"Model directory entry carries non-allow-listed keys: {string.Join(", ", actual)}");
    }

    private static void AssertStatsEntryAllowList(JsonElement entry)
    {
        HashSet<string> actual = entry.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        Assert.True(actual.SetEquals(StatsEntryKeys),
            $"Model stats entry carries non-allow-listed keys: {string.Join(", ", actual)}");
    }
}
