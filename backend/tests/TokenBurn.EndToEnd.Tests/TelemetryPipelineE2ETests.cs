extern alias ingest;
extern alias processor;

using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Confluent.Kafka;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using IngestDb = ingest::Api.TokenBurn.Ingest.IngestDbContext;
using TelemetryDb = processor::TokenBurn.Processor.Persistence.TelemetryDbContext;
using AgentRun = processor::TokenBurn.Processor.Domain.AgentRun;
using AgentRunEnvelopeMapper = processor::TokenBurn.Processor.Domain.AgentRunEnvelopeMapper;
using OtlpGenAiAdapter = processor::TokenBurn.Processor.Adapters.OtlpGenAiAdapter;
using RunStatus = processor::TokenBurn.Processor.Domain.RunStatus;
using PricingStatus = processor::TokenBurn.Processor.Domain.PricingStatus;
using LedgerStatus = processor::TokenBurn.Processor.Adapters.LedgerStatus;

namespace TokenBurn.EndToEnd.Tests;

public sealed class TelemetryPipelineE2ETests : IClassFixture<TelemetryPipelineE2EFixture>, IAsyncLifetime
{
    private const string FixtureRelativePath = "Fixtures/delegate-ledger.otlp.json";
    private const string SharedSessionFixtureRelativePath = "Fixtures/delegate-ledger-shared-session.otlp.json";
    private const string ProgressSessionId = "shared-sess-2";
    private const string ProgressHandle = "20260802-delegate-progress-h1";
    private const string CompletedSurvivesSessionId = "shared-sess-3";
    private const string CompletedSurvivesHandle = "20260802-delegate-completed-h1";
    private const string TieSessionId = "shared-sess-4";
    private const string TieHandle = "20260802-delegate-tie-h1";
    private const string TopicName = "telemetry.raw";
    // endTimeUnixNano 1785629100000000000 / 1_000_000, the fixed completion time the
    // payload builder stamps on completed runs.
    private static readonly DateTimeOffset ProgressEndTime = DateTimeOffset.FromUnixTimeMilliseconds(1_785_629_100_000);
    private static readonly TimeSpan PipelineTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RedeliveryWatchWindow = TimeSpan.FromSeconds(6);

    private readonly TelemetryPipelineE2EFixture _fixture;
    private HttpClient _client = null!;

    public TelemetryPipelineE2ETests(TelemetryPipelineE2EFixture fixture)
    {
        _fixture = fixture;
    }

    public Task InitializeAsync()
    {
        _client = _fixture.IngestFactory.CreateClient();
        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task FullPipe_PostOtlp_PropagatesToLedger()
    {
        FixtureData fixture = LoadFixture();

        using HttpResponseMessage response = await PostAsync(fixture.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        bool converged = await WaitUntilAsync(async () => await CountAgentRunsForSessionsAsync(fixture.SessionIds) == fixture.Expected, PipelineTimeout);
        Assert.True(converged, $"Ledger did not converge to {fixture.Expected} agent runs within the timeout.");

        Assert.Equal(1, await CountEnvelopesForBodyAsync(fixture.Json));
        Assert.Equal(1, await CountOutboxForKeyAsync(fixture.FirstSessionId));
        await AssertLedgerSpotCheckAsync(fixture);
    }

    [Fact]
    public async Task Ingest_DedupesIdenticalBody()
    {
        FixtureData fixture = LoadFixture();

        using HttpResponseMessage first = await PostAsync(fixture.Json);
        using HttpResponseMessage second = await PostAsync(fixture.Json);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        bool converged = await WaitUntilAsync(async () => await CountAgentRunsForSessionsAsync(fixture.SessionIds) == fixture.Expected, PipelineTimeout);
        Assert.True(converged, $"Ledger did not converge to {fixture.Expected} agent runs within the timeout.");

        Assert.Equal(1, await CountEnvelopesForBodyAsync(fixture.Json));
        Assert.Equal(1, await CountOutboxForKeyAsync(fixture.FirstSessionId));
    }

    [Fact]
    public async Task Processor_IsIdempotentUnderRedelivery()
    {
        FixtureData fixture = LoadFixture();

        using HttpResponseMessage seed = await PostAsync(fixture.Json);
        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
        bool seeded = await WaitUntilAsync(async () => await CountAgentRunsForSessionsAsync(fixture.SessionIds) == fixture.Expected, PipelineTimeout);
        Assert.True(seeded, $"Ledger did not converge to {fixture.Expected} agent runs before redelivery.");
        Assert.Equal(fixture.Expected, await CountAgentRunsForSessionsAsync(fixture.SessionIds));

        using IProducer<string, string> producer = BuildRawProducer(_fixture.KafkaBootstrapServers);
        await producer.ProduceAsync(TopicName, new Message<string, string>
        {
            Key = fixture.FirstSessionId,
            Value = fixture.Json
        });
        producer.Flush(TimeSpan.FromSeconds(10));

        DateTimeOffset watchDeadline = TimeProvider.System.GetUtcNow().Add(RedeliveryWatchWindow);
        int count;
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            count = await CountAgentRunsForSessionsAsync(fixture.SessionIds);
            Assert.Equal(fixture.Expected, count);
        } while (TimeProvider.System.GetUtcNow() < watchDeadline);
        Assert.Equal(fixture.Expected, count);
    }

    [Fact]
    public async Task MultipleHandlesInOneSession_MergeIntoOneRun()
    {
        SharedSessionFixture fixture = LoadSharedSessionFixture();
        Assert.Equal(2, fixture.DistinctHandles.Count);

        using HttpResponseMessage response = await PostAsync(fixture.Json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        bool converged = await WaitUntilAsync(
            async () => await CountAgentRunsForSessionsAsync([fixture.SessionId]) == 1,
            PipelineTimeout);
        Assert.True(converged, $"Ledger did not converge to a single merged run for shared session {fixture.SessionId}.");

        Assert.Equal(1, await CountEnvelopesForBodyAsync(fixture.Json));
        Assert.Equal(1, await CountOutboxForKeyAsync(fixture.SessionId));
        AgentRun run = await LoadAgentRunAsync(fixture.SessionId, "");
        Assert.Equal("", run.AgentId);
        Assert.Equal(fixture.ExpectedExternalId, run.ExternalId);
        Assert.Equal(fixture.ExpectedModel, run.ModelSlug);
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(fixture.ExpectedInputTokens, run.InputTokens);
        Assert.Equal(fixture.ExpectedCacheReadTokens, run.CacheReadTokens);
        Assert.Equal(fixture.ExpectedCacheWriteTokens, run.CacheWriteTokens);
        Assert.Equal(fixture.ExpectedOutputTokens, run.OutputTokens);
        Assert.Equal(fixture.ExpectedReportedCostUsd, run.ReportedCostUsd);
        Assert.Equal(PricingStatus.Priced, run.PricingStatus);
        Assert.Equal(1.0m, run.PriceMultiplier);
    }

    [Fact]
    public async Task SameSessionRedelivered_StaysOneRun()
    {
        SharedSessionFixture fixture = LoadSharedSessionFixture();

        using HttpResponseMessage seed = await PostAsync(fixture.Json);
        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
        bool seeded = await WaitUntilAsync(
            async () => await CountAgentRunsForSessionsAsync([fixture.SessionId]) == 1,
            PipelineTimeout);
        Assert.True(seeded, $"Ledger did not converge to a single merged run before redelivery.");

        using IProducer<string, string> producer = BuildRawProducer(_fixture.KafkaBootstrapServers);
        await producer.ProduceAsync(TopicName, new Message<string, string>
        {
            Key = fixture.SessionId,
            Value = fixture.Json
        });
        producer.Flush(TimeSpan.FromSeconds(10));

        DateTimeOffset watchDeadline = TimeProvider.System.GetUtcNow().Add(RedeliveryWatchWindow);
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            // Redelivering the same (session_id, "") key must not grow the run count.
            Assert.Equal(1, await CountAgentRunsForSessionsAsync([fixture.SessionId]));
            Assert.Equal(1, await CountAgentRunsForHandleAsync(fixture.SessionId, ""));
        } while (TimeProvider.System.GetUtcNow() < watchDeadline);
        Assert.Equal(1, await CountAgentRunsForSessionsAsync([fixture.SessionId]));
    }

    [Fact]
    public async Task RunProgressesFromInProgressToCompleted()
    {
        string inProgress = BuildDelegateLedgerPayload(ProgressSessionId, ProgressHandle, includeEndTime: false, status: null);
        string completed = BuildDelegateLedgerPayload(ProgressSessionId, ProgressHandle, includeEndTime: true, status: "ok");

        using HttpResponseMessage first = await PostAsync(inProgress);
        using HttpResponseMessage second = await PostAsync(completed);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        bool converged = await WaitUntilAsync(
            async () => await CountAgentRunsForSessionsAsync([ProgressSessionId]) == 1,
            PipelineTimeout);
        Assert.True(converged, $"Progress run did not converge to a single row for session {ProgressSessionId}.");

        AgentRun run = await LoadAgentRunAsync(ProgressSessionId, "");
        Assert.Equal(RunStatus.Completed, run.Status);
    }

    [Fact]
    public async Task CompletedRun_SurvivesInProgressRedelivery()
    {
        string completed = BuildDelegateLedgerPayload(CompletedSurvivesSessionId, CompletedSurvivesHandle, includeEndTime: true, status: "ok");
        string inProgress = BuildDelegateLedgerPayload(CompletedSurvivesSessionId, CompletedSurvivesHandle, includeEndTime: false, status: null);

        using HttpResponseMessage first = await PostAsync(completed);
        using HttpResponseMessage second = await PostAsync(inProgress);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        bool converged = await WaitUntilAsync(
            async () => await CountAgentRunsForSessionsAsync([CompletedSurvivesSessionId]) == 1,
            PipelineTimeout);
        Assert.True(converged, $"Completed run did not converge to a single row for session {CompletedSurvivesSessionId}.");

        // Watch the whole redelivery window: an in-progress redelivery of a completed key
        // must be rejected by the upsert guard, leaving the completion marker untouched.
        DateTimeOffset watchDeadline = TimeProvider.System.GetUtcNow().Add(RedeliveryWatchWindow);
        do
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250));
            AgentRun run = await LoadAgentRunAsync(CompletedSurvivesSessionId, "");
            Assert.Equal(RunStatus.Completed, run.Status);
            Assert.Equal(ProgressEndTime, run.EndedAt);
        } while (TimeProvider.System.GetUtcNow() < watchDeadline);
    }

    [Fact]
    public async Task SameEndedAtRedelivered_LaterMessageWins()
    {
        string completed = BuildDelegateLedgerPayload(TieSessionId, TieHandle, includeEndTime: true, status: "ok");
        string tieRedelivery = BuildDelegateLedgerPayload(TieSessionId, TieHandle, includeEndTime: true, status: "ok", inputTokens: 9999);

        using HttpResponseMessage seed = await PostAsync(completed);
        Assert.Equal(HttpStatusCode.OK, seed.StatusCode);
        bool seeded = await WaitUntilAsync(
            async () => await CountAgentRunsForSessionsAsync([TieSessionId]) == 1,
            PipelineTimeout);
        Assert.True(seeded, $"Tie run did not converge to a single row for session {TieSessionId}.");

        using IProducer<string, string> producer = BuildRawProducer(_fixture.KafkaBootstrapServers);
        await producer.ProduceAsync(TopicName, new Message<string, string>
        {
            Key = TieSessionId,
            Value = tieRedelivery
        });
        producer.Flush(TimeSpan.FromSeconds(10));

        bool applied = await WaitUntilAsync(
            async () => (await LoadAgentRunAsync(TieSessionId, "")).InputTokens == 9999,
            PipelineTimeout);
        Assert.True(applied, "A same-ended_at redelivery must be applied by the >= tie branch without regressing the completion marker.");

        AgentRun run = await LoadAgentRunAsync(TieSessionId, "");
        Assert.Equal(RunStatus.Completed, run.Status);
        Assert.Equal(ProgressEndTime, run.EndedAt);
        Assert.Equal(1, await CountAgentRunsForSessionsAsync([TieSessionId]));
    }

    private Task<HttpResponseMessage> PostAsync(string json)
        => _client.PostAsync("/v1/traces", new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<int> CountEnvelopesForBodyAsync(string body)
    {
        string contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        using IServiceScope scope = _fixture.IngestFactory.Services.CreateScope();
        IngestDb db = scope.ServiceProvider.GetRequiredService<IngestDb>();
        return await db.Envelopes.CountAsync(envelope => envelope.ContentHash == contentHash);
    }

    private async Task<int> CountOutboxForKeyAsync(string key)
    {
        using IServiceScope scope = _fixture.IngestFactory.Services.CreateScope();
        IngestDb db = scope.ServiceProvider.GetRequiredService<IngestDb>();
        return await db.OutboxMessages.CountAsync(message => message.Key == key);
    }

    private async Task<int> CountAgentRunsForSessionsAsync(IReadOnlyList<string> sessionIds)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        return await db.AgentRuns.CountAsync(run => sessionIds.Contains(run.SessionId));
    }

    private async Task<int> CountAgentRunsForHandleAsync(string sessionId, string agentId)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        return await db.AgentRuns.CountAsync(run => run.SessionId == sessionId && run.AgentId == agentId);
    }

    private async Task<AgentRun> LoadAgentRunAsync(string sessionId, string agentId)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        return await db.AgentRuns.SingleAsync(run => run.SessionId == sessionId && run.AgentId == agentId);
    }

    private async Task AssertLedgerSpotCheckAsync(FixtureData fixture)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        AgentRun run = await db.AgentRuns.SingleAsync(r => r.SessionId == fixture.FirstSessionId);

        Assert.Equal(fixture.SpotPersona, run.Persona);
        Assert.Equal(fixture.SpotInputTokens, run.InputTokens);
        Assert.Equal(fixture.SpotCacheWriteTokens, run.CacheWriteTokens);
        Assert.Equal(AgentRunEnvelopeMapper.ToRunStatus(LedgerStatus.FromLedger(fixture.SpotStatus)), run.Status);
        Assert.Equal(PricingStatus.Priced, run.PricingStatus);
        Assert.Equal(1.0m, run.PriceMultiplier);
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

    private static IProducer<string, string> BuildRawProducer(string bootstrapServers)
        => new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = bootstrapServers }).Build();

    private static FixtureData LoadFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FixtureRelativePath);
        return ParseFixture(File.ReadAllText(path));
    }

    private static SharedSessionFixture LoadSharedSessionFixture()
    {
        string path = Path.Combine(AppContext.BaseDirectory, SharedSessionFixtureRelativePath);
        return ParseSharedSessionFixture(File.ReadAllText(path));
    }

    private static FixtureData ParseFixture(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        HashSet<string> sessions = new(StringComparer.Ordinal);
        string? firstSessionId = null;
        string? spotPersona = null;
        long? spotInputTokens = null;
        long? spotCacheWriteTokens = null;
        string? spotStatus = null;

        foreach (JsonElement resourceSpan in document.RootElement.GetProperty("resourceSpans").EnumerateArray())
        {
            string? sessionId = ReadAttribute(resourceSpan.GetProperty("resource"), "session.id", "stringValue");
            string source = ReadAttribute(resourceSpan.GetProperty("resource"), "tokenburn.source", "stringValue") ?? "delegate-ledger";
            if (!string.Equals(source, "delegate-ledger", StringComparison.Ordinal))
                continue;

            foreach (JsonElement scope in resourceSpan.GetProperty("scopeSpans").EnumerateArray())
            foreach (JsonElement span in scope.GetProperty("spans").EnumerateArray())
            {
                string? handle = ReadAttribute(span, "tokenburn.handle", "stringValue");
                if (OtlpGenAiAdapter.IsTestHandle(handle))
                    continue;

                string resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? handle ?? "" : sessionId;
                firstSessionId ??= resolvedSessionId;
                sessions.Add(resolvedSessionId);
                spotPersona ??= ReadAttribute(span, "tokenburn.persona", "stringValue");
                spotInputTokens ??= ReadLong(span, "gen_ai.usage.input_tokens");
                spotCacheWriteTokens ??= ReadLong(span, "gen_ai.usage.cache_write_tokens");
                spotStatus ??= ReadAttribute(span, "tokenburn.status", "stringValue");
            }
        }

        return new FixtureData(
            json,
            sessions.Count,
            firstSessionId ?? throw new InvalidOperationException("Fixture contains no delegate-ledger session."),
            sessions.ToArray(),
            spotPersona ?? throw new InvalidOperationException("Fixture contains no spot-check persona."),
            spotInputTokens ?? throw new InvalidOperationException("Fixture contains no spot-check input tokens."),
            spotCacheWriteTokens ?? throw new InvalidOperationException("Fixture contains no spot-check cache-write tokens."),
            spotStatus ?? throw new InvalidOperationException("Fixture contains no spot-check status."));
    }

    private static SharedSessionFixture ParseSharedSessionFixture(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement resourceSpan = document.RootElement.GetProperty("resourceSpans").EnumerateArray().Single();
        string sessionId = ReadAttribute(resourceSpan.GetProperty("resource"), "session.id", "stringValue")!;
        HashSet<string> handles = new(StringComparer.Ordinal);
        long inputTokens = 0;
        long cacheReadTokens = 0;
        long cacheWriteTokens = 0;
        long outputTokens = 0;
        decimal reportedCostUsd = 0m;
        decimal maxCost = -1m;
        string? maxCostHandle = null;
        string? maxCostModel = null;
        foreach (JsonElement scope in resourceSpan.GetProperty("scopeSpans").EnumerateArray())
        foreach (JsonElement span in scope.GetProperty("spans").EnumerateArray())
        {
            string? handle = ReadAttribute(span, "tokenburn.handle", "stringValue");
            if (OtlpGenAiAdapter.IsTestHandle(handle))
                continue;
            if (handle is not null)
            {
                handles.Add(handle);
            }
            decimal? spanCost = ReadDecimal(span, "tokenburn.cost_usd");
            if ((spanCost ?? 0) > maxCost)
            {
                maxCost = spanCost ?? 0;
                maxCostHandle = handle;
                maxCostModel = ReadAttribute(span, "gen_ai.request.model", "stringValue");
            }
            inputTokens += ReadLong(span, "gen_ai.usage.input_tokens") ?? 0;
            cacheReadTokens += ReadLong(span, "gen_ai.usage.cache_read_tokens") ?? 0;
            cacheWriteTokens += ReadLong(span, "gen_ai.usage.cache_write_tokens") ?? 0;
            outputTokens += ReadLong(span, "gen_ai.usage.output_tokens") ?? 0;
            reportedCostUsd += spanCost ?? 0m;
        }
        return new SharedSessionFixture(
            json,
            sessionId,
            handles.ToArray(),
            maxCostHandle ?? throw new InvalidOperationException("Shared-session fixture contains no delegate-ledger span."),
            maxCostModel ?? throw new InvalidOperationException("Shared-session fixture contains no model."),
            inputTokens,
            cacheReadTokens,
            cacheWriteTokens,
            outputTokens,
            reportedCostUsd);
    }

    private static string? ReadAttribute(JsonElement owner, string key, string valueProperty)
    {
        if (!owner.TryGetProperty("attributes", out JsonElement attributes))
            return null;

        foreach (JsonElement attribute in attributes.EnumerateArray())
        {
            if (!string.Equals(attribute.GetProperty("key").GetString(), key, StringComparison.Ordinal))
                continue;
            if (!attribute.TryGetProperty("value", out JsonElement value) ||
                !value.TryGetProperty(valueProperty, out JsonElement result))
                return null;
            return result.ToString();
        }
        return null;
    }

    private static long? ReadLong(JsonElement span, string key)
        => long.TryParse(ReadAttribute(span, key, "intValue"), out long value) ? value : null;

    private static decimal? ReadDecimal(JsonElement span, string key)
        => decimal.TryParse(ReadAttribute(span, key, "doubleValue"), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    private static string BuildDelegateLedgerPayload(string sessionId, string handle, bool includeEndTime, string? status, long inputTokens = 4200, long cacheWriteTokens = 0)
    {
        JsonArray spanAttributes =
        [
            StringAttribute("tokenburn.handle", handle),
            StringAttribute("tokenburn.persona", "engineering"),
            StringAttribute("gen_ai.request.model", "deepseek-v4-flash"),
            IntAttribute("gen_ai.usage.input_tokens", inputTokens)
        ];
        if (cacheWriteTokens > 0)
        {
            spanAttributes.Add(IntAttribute("gen_ai.usage.cache_write_tokens", cacheWriteTokens));
        }
        if (status is not null)
        {
            spanAttributes.Add(StringAttribute("tokenburn.status", status));
        }

        JsonObject span = new()
        {
            ["traceId"] = "AAAAAAAAAAAAAAAAAAAAAA==",
            ["spanId"] = "AAAAAAAAAAA=",
            ["name"] = "delegate progress",
            ["startTimeUnixNano"] = "1785628800000000000",
            ["attributes"] = spanAttributes
        };
        if (includeEndTime)
        {
            span["endTimeUnixNano"] = "1785629100000000000";
        }

        JsonObject resourceSpan = new()
        {
            ["resource"] = new JsonObject
            {
                ["attributes"] = new JsonArray(
                    StringAttribute("session.id", sessionId),
                    StringAttribute("tokenburn.source", "delegate-ledger"))
            },
            ["scopeSpans"] = new JsonArray(
                new JsonObject
                {
                    ["scope"] = new JsonObject { ["name"] = "delegate-ledger" },
                    ["spans"] = new JsonArray(span)
                })
        };
        return new JsonObject { ["resourceSpans"] = new JsonArray(resourceSpan) }.ToJsonString();
    }

    private static JsonObject StringAttribute(string key, string value)
        => new() { ["key"] = key, ["value"] = new JsonObject { ["stringValue"] = value } };

    private static JsonObject IntAttribute(string key, long value)
        => new() { ["key"] = key, ["value"] = new JsonObject { ["intValue"] = value } };

    private sealed record FixtureData(
        string Json,
        int Expected,
        string FirstSessionId,
        IReadOnlyList<string> SessionIds,
        string SpotPersona,
        long SpotInputTokens,
        long SpotCacheWriteTokens,
        string SpotStatus);

    private sealed record SharedSessionFixture(
        string Json,
        string SessionId,
        IReadOnlyList<string> DistinctHandles,
        string ExpectedExternalId,
        string ExpectedModel,
        long ExpectedInputTokens,
        long ExpectedCacheReadTokens,
        long ExpectedCacheWriteTokens,
        long ExpectedOutputTokens,
        decimal ExpectedReportedCostUsd);
}

public sealed class TelemetryPipelineE2EFixture : IAsyncLifetime
{
    private const string NoAuthHeader = "X-Test-No-Auth";

    private readonly PostgreSqlContainer _database = new PostgreSqlBuilder().Build();
    private readonly KafkaContainer _kafka = new KafkaBuilder().Build();

    public WebApplicationFactory<ingest::Program> IngestFactory { get; private set; } = null!;
    public WebApplicationFactory<processor::Program> ProcessorFactory { get; private set; } = null!;
    public string KafkaBootstrapServers => _kafka.GetBootstrapAddress();

    public async Task InitializeAsync()
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromMinutes(3));
        await Task.WhenAll(_database.StartAsync(timeout.Token), _kafka.StartAsync(timeout.Token));

        string pgConnection = _database.GetConnectionString();
        string kafkaAddress = _kafka.GetBootstrapAddress();

        IngestFactory = new WebApplicationFactory<ingest::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Ingest", pgConnection);
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            builder.UseSetting("Kafka:BootstrapServers", kafkaAddress);
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication("Test")
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("Test", _ => { });
                services.AddSingleton<IConfigureOptions<AuthenticationOptions>>(
                    new ConfigureNamedOptions<AuthenticationOptions>(Options.DefaultName, options =>
                    {
                        options.DefaultScheme = "Test";
                        options.DefaultAuthenticateScheme = "Test";
                        options.DefaultChallengeScheme = "Test";
                        options.DefaultForbidScheme = "Test";
                    }));
            });
        });

        ProcessorFactory = new WebApplicationFactory<processor::Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Processor", pgConnection);
            builder.UseSetting("Jwt:Authority", "http://localhost/connect");
            builder.UseSetting("Kafka:BootstrapServers", kafkaAddress);
        });

        // Starting each host runs its EF migrations and boots the background services:
        // the ingest OutboxPublisher (creates the topic, drains the outbox) and the
        // processor TelemetryRawConsumer (AutoOffsetReset.Earliest).
        _ = IngestFactory.Services;
        _ = ProcessorFactory.Services;
    }

    public async Task DisposeAsync()
    {
        await IngestFactory.DisposeAsync();
        await ProcessorFactory.DisposeAsync();
        await _database.DisposeAsync();
        await _kafka.DisposeAsync();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.ContainsKey(NoAuthHeader))
                return Task.FromResult(AuthenticateResult.NoResult());

            ClaimsIdentity identity = new(
                [new Claim("scope", "telemetry.write"), new Claim("sub", "test-client")],
                Scheme.Name);
            ClaimsPrincipal principal = new(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
