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
using Elastic.Clients.Elasticsearch;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Testcontainers.Elasticsearch;
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
using RunReplayService = processor::TokenBurn.Processor.Commands.RunReplayService;
using AgentMessage = processor::TokenBurn.Processor.Domain.AgentMessage;
using AgentMessageEnvelopeMapper = processor::TokenBurn.Processor.Domain.AgentMessageEnvelopeMapper;
using AgentRunUpserter = processor::TokenBurn.Processor.Persistence.AgentRunUpserter;
using AgentMessageUpserter = processor::TokenBurn.Processor.Persistence.AgentMessageUpserter;
using ClaudeCodeTranscriptAdapter = processor::TokenBurn.Processor.Adapters.ClaudeCodeTranscriptAdapter;
using PricingEngine = processor::TokenBurn.Processor.Pricing.PricingEngine;
using WasteFinding = processor::TokenBurn.Processor.Domain.WasteFinding;
using WasteFindingKind = processor::TokenBurn.Processor.Domain.WasteFindingKind;
using System.Data;
using Npgsql;
using AggregateRebuildService = processor::TokenBurn.Processor.Aggregation.AggregateRebuildService;
using CostCalculator = processor::TokenBurn.Processor.Pricing.CostCalculator;
using MetricBucket = processor::TokenBurn.Processor.Domain.MetricBucket;
using PriceMultiplier = processor::TokenBurn.Processor.Pricing.PriceMultiplier;

namespace TokenBurn.EndToEnd.Tests;

/// <summary>
///     Phase 6 gate — privacy-boundary rule 5 leak test against the <c>metrics.aggregate</c>
///     public-safe projection. Seeds known secret-shaped strings into the private corpus
///     (agent-run identity fields, message content, and an Elasticsearch <c>traces</c> document),
///     then proves the aggregate table and the <c>metrics.aggregate</c> Kafka topic never carry
///     them. The probe and secret path ride on the SURVIVING positive-control bucket (6 priced
///     runs, ≥ MinSize 5) — in both message content and identity/workspace fields — so the
///     absence assertions are falsifiable for every rule-2 field class that actually reaches the
///     projection, and a sub-N bucket (claude-opus-5) carries them as an additional probe.
/// </summary>
public sealed class AggregateLeakTests : IClassFixture<TelemetryPipelineE2EFixture>
{
    private const string Probe = "sk-aggregate-probe-9f8e7d6c5b4a3210";
    private const string SecretPath = "/var/secrets/tokenburn/trading-key.pem";
    private const string AggregateTopic = "metrics.aggregate";
    private const string TracesIndex = "traces";
    private const string PositiveControlModel = "deepseek-v4-flash";
    private const string SubMinSizeModel = "claude-sonnet-5";
    private const string ProbeCarrierModel = "claude-opus-5";
    private const string DelegateLedgerService = "delegate-ledger";
    private const int PositiveControlCount = 6;
    private const int SubMinSizeCount = 3;

    // Fixed UTC instant → bucket_day 2026-08-06. No live clock anywhere in the seed.
    private static readonly DateTimeOffset SeedInstant = new(2026, 8, 6, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly BucketDay = new(2026, 8, 6);

    private readonly TelemetryPipelineE2EFixture _fixture;

    public AggregateLeakTests(TelemetryPipelineE2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task AggregateProjection_DoesNotLeakProbeOrSecretPath()
    {
        await SeedPrivateCorpusAsync(CancellationToken.None);

        await AssertPrivateCorpusHoldsProbeAsync(CancellationToken.None);

        int bucketCount = await RebuildAsync(CancellationToken.None);
        Assert.Equal(1, bucketCount);

        MetricBucket positiveControl = await LoadBucketAsync(BucketDay, PositiveControlModel, DelegateLedgerService, CancellationToken.None);
        Assert.Equal(PositiveControlCount, positiveControl.RunCount);
        Assert.Equal(PositiveControlCount, positiveControl.PricedRunCount);

        string aggregateTableText = await ReadAggregateTableTextAsync(CancellationToken.None);
        Assert.DoesNotContain(Probe, aggregateTableText);
        Assert.DoesNotContain(SecretPath, aggregateTableText);

        List<string> topicPayloads = await DrainTopicAsync(_fixture.KafkaBootstrapServers, AggregateTopic, CancellationToken.None);
        Assert.NotEmpty(topicPayloads);
        Assert.Contains(topicPayloads, payload => payload.Contains("\"bucketDay\"", StringComparison.Ordinal));
        Assert.All(topicPayloads, payload => Assert.DoesNotContain(Probe, payload));
        Assert.All(topicPayloads, payload => Assert.DoesNotContain(SecretPath, payload));
        string positivePayload = topicPayloads.Single(payload => payload.Contains("\"bucketDay\"", StringComparison.Ordinal));
        using (JsonDocument document = JsonDocument.Parse(positivePayload))
        {
            Assert.Equal(PositiveControlModel, document.RootElement.GetProperty("modelSlug").GetString());
            Assert.Equal(DelegateLedgerService, document.RootElement.GetProperty("service").GetString());
            Assert.Equal(PositiveControlCount, document.RootElement.GetProperty("runCount").GetInt64());
            Assert.Equal(PositiveControlCount, document.RootElement.GetProperty("pricedRunCount").GetInt64());
        }

        Assert.False(await HasBucketForModelAsync(SubMinSizeModel, CancellationToken.None));
        Assert.False(await HasBucketForModelAsync(ProbeCarrierModel, CancellationToken.None));

        long countBeforeRebuild = await CountAggregateRowsAsync(CancellationToken.None);
        await RebuildAsync(CancellationToken.None);
        long countAfterRebuild = await CountAggregateRowsAsync(CancellationToken.None);
        Assert.Equal(countBeforeRebuild, countAfterRebuild);
        Assert.Equal(1L, countAfterRebuild);
    }

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

        for (int index = 1; index <= SubMinSizeCount; index++)
        {
            AgentRun run = AgentRun.Create(
                sessionId: $"seed-sub-{index}", agentId: "", source: DelegateLedgerService,
                externalId: null, persona: "engineering", modelSlug: SubMinSizeModel,
                status: RunStatus.Completed, startedAt: SeedInstant, endedAt: SeedInstant.AddMinutes(5),
                inputTokens: 500 + index, cacheReadTokens: 50, cacheWriteTokens: 100,
                outputTokens: 200 + index, reportedCostUsd: null, service: DelegateLedgerService);
            await SeedPricedRunAsync(db, run, ct);
        }

        AgentRun probeCarrier = AgentRun.Create(
            sessionId: Probe, agentId: "", source: DelegateLedgerService,
            externalId: Probe, persona: Probe, modelSlug: ProbeCarrierModel,
            status: RunStatus.Completed, startedAt: SeedInstant, endedAt: SeedInstant.AddMinutes(5),
            inputTokens: 100, cacheReadTokens: 10, cacheWriteTokens: 20, outputTokens: 50,
            reportedCostUsd: null, service: DelegateLedgerService, workspace: SecretPath);
        await SeedPricedRunAsync(db, probeCarrier, ct);
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

    private async Task AssertPrivateCorpusHoldsProbeAsync(CancellationToken ct)
    {
        string esDocId = Guid.NewGuid().ToString("D");
        IndexResponse indexResponse = await _fixture.ElasticsearchClient.IndexAsync(
            new Dictionary<string, string> { ["session_id"] = Probe },
            TracesIndex, esDocId, ct);
        Assert.True(indexResponse.IsValidResponse, $"ES index into '{TracesIndex}' failed: {indexResponse.DebugInformation}");
        await _fixture.ElasticsearchClient.Indices.RefreshAsync(TracesIndex, ct);

        GetResponse<Dictionary<string, JsonElement>> getResponse = await _fixture.ElasticsearchClient.GetAsync<Dictionary<string, JsonElement>>(
            TracesIndex, esDocId, ct);
        Assert.True(getResponse.IsValidResponse, $"ES get from '{TracesIndex}' failed: {getResponse.DebugInformation}");
        Assert.NotNull(getResponse.Source);
        Assert.Equal(Probe, getResponse.Source!["session_id"].GetString());

        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        int probeMessages = await db.AgentMessages.CountAsync(message => message.Content == Probe, ct);
        Assert.True(probeMessages >= PositiveControlCount,
            $"Expected at least {PositiveControlCount} agent_messages carrying the probe, found {probeMessages}.");
    }

    private async Task<int> RebuildAsync(CancellationToken ct)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        AggregateRebuildService rebuild = scope.ServiceProvider.GetRequiredService<AggregateRebuildService>();
        return await rebuild.RebuildAsync(ct);
    }

    private async Task<MetricBucket> LoadBucketAsync(DateOnly bucketDay, string modelSlug, string service, CancellationToken ct)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        MetricBucket? row = await db.MetricBuckets.AsNoTracking().SingleOrDefaultAsync(
            bucket => bucket.BucketDay == bucketDay && bucket.ModelSlug == modelSlug && bucket.Service == service, ct);
        Assert.True(row is not null,
            $"No metrics.aggregate row for ({bucketDay:yyyy-MM-dd}, {modelSlug}, {service}).");
        return row!;
    }

    /// <summary>
    ///     The whole aggregate table serialized to one text blob — covers every present and future
    ///     column of the public-safe projection. Raw SQL over the DbContext's own connection, the
    ///     only credential path that authenticates in tests.
    /// </summary>
    private async Task<string> ReadAggregateTableTextAsync(CancellationToken ct)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync(ct);
        await using NpgsqlCommand command = new("SELECT to_jsonb(a)::text FROM metrics.aggregate a", connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
        StringBuilder text = new();
        while (await reader.ReadAsync(ct))
            text.AppendLine(reader.GetString(0));
        return text.ToString();
    }

    private async Task<bool> HasBucketForModelAsync(string modelSlug, CancellationToken ct)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        return await db.MetricBuckets.AsNoTracking().AnyAsync(bucket => bucket.ModelSlug == modelSlug, ct);
    }

    private async Task<long> CountAggregateRowsAsync(CancellationToken ct)
    {
        using IServiceScope scope = _fixture.ProcessorFactory.Services.CreateScope();
        TelemetryDb db = scope.ServiceProvider.GetRequiredService<TelemetryDb>();
        return await db.MetricBuckets.AsNoTracking().LongCountAsync(ct);
    }

    /// <summary>
    ///     Reads every message currently in the topic from a fresh consumer group (Earliest), ending
    ///     after a quiet period. A fresh unique GroupId guarantees this consumer reads the whole
    ///     topic, not just the new tail.
    /// </summary>
    private static async Task<List<string>> DrainTopicAsync(string bootstrapServers, string topic, CancellationToken ct)
    {
        ConsumerConfig config = new()
        {
            BootstrapServers = bootstrapServers,
            GroupId = $"aggregate-leak-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        var payloads = new List<string>();
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);
        int quietPolls = 0;
        while (quietPolls < 10)
        {
            ct.ThrowIfCancellationRequested();
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromMilliseconds(750));
            }
            catch (ConsumeException)
            {
                continue;
            }
            if (result is null)
            {
                quietPolls++;
                continue;
            }
            quietPolls = 0;
            payloads.Add(result.Message.Value);
        }
        consumer.Close();
        return payloads;
    }
}
