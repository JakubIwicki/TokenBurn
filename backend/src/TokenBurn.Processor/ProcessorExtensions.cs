using Confluent.Kafka;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ElasticsearchClient = Elastic.Clients.Elasticsearch.ElasticsearchClient;
using TokenBurn.Common.Primitives;
using TokenBurn.Common.Security;
using TokenBurn.Contracts;
using TokenBurn.Processor.Adapters;
using TokenBurn.Processor.Aggregation;
using TokenBurn.Processor.Commands;
using TokenBurn.Processor.Documents;
using TokenBurn.Processor.Documents.Indexing;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Features.Imports;
using PricingStatus = TokenBurn.Processor.Domain.PricingStatus;
using TokenBurn.Processor.Infrastructure;
using TokenBurn.Processor.Infrastructure.Embeddings;
using TokenBurn.Processor.Infrastructure.Indexing;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;
using TokenBurn.Processor.SelfTelemetry;
using TokenBurn.Processor.WasteDetection;

namespace TokenBurn.Processor;

public static class ProcessorExtensions
{
    public static WebApplicationBuilder AddProcessorServices(this WebApplicationBuilder builder)
    {
        string connectionString = builder.Configuration.GetConnectionString("Processor")
            ?? throw new InvalidOperationException("ConnectionStrings:Processor must be configured.");
        builder.Services.AddHealthChecks();
        builder.AddTokenBurnJwtAuth();
        builder.Services.AddDbContext<TelemetryDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(TelemetryDbContext).Assembly.FullName)
                .MigrationsHistoryTable("__EFMigrationsHistory", "telemetry")));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<OtlpGenAiAdapter>();
        builder.Services.AddSingleton<DelegateLedgerAdapter>();
        builder.Services.AddSingleton<DelegateRunLogAdapter>();
        builder.Services.AddSingleton<ClaudeCodeTranscriptAdapter>();
        builder.Services.AddSingleton<JiCachingAdapter>();
        builder.Services.AddSingleton<SourceDispatcher>();
        builder.Services.AddScoped<PricingSeeder>();
        builder.Services.AddScoped<PricingEngine>();
        builder.Services.AddScoped<AgentRunUpserter>();
        builder.Services.AddScoped<AgentMessageUpserter>();
        builder.Services.AddScoped<FindingsUpserter>();
        builder.Services.AddSingleton(WasteDetectionOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddScoped<WasteDetectionService>();
        builder.Services.AddScoped<IImportCommandExecutor, ClaudeCodeTranscriptImportExecutor>();
        // Documents import pipeline: deterministic chunker, content-hash dedupe, and the
        // executor riding the import_commands lifecycle. The documents ES index has exactly
        // one writer (the executor), so no fan-out coordination is needed. The Lazy client
        // defers Elasticsearch:Uri validation to execution, so the imports endpoint (which
        // enumerates every executor) keeps working on hosts without Elasticsearch configured.
        builder.Services.AddSingleton(sp => new Lazy<ElasticsearchClient>(sp.GetRequiredService<ElasticsearchClient>));
        builder.Services.AddSingleton(sp => new Lazy<IEmbeddingClient>(sp.GetRequiredService<IEmbeddingClient>));
        builder.Services.AddSingleton(DocumentsOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddScoped<DocumentsUpserter>();
        builder.Services.AddScoped<DocumentChunkUpserter>();
        builder.Services.AddSingleton<TextChunker>();
        builder.Services.AddSingleton<DocumentIndexTemplateInitializer>();
        builder.Services.AddScoped<IImportCommandExecutor, DocumentsImportExecutor>();
        builder.Services.AddSingleton<KafkaTopicInitializer>();
        builder.Services.AddProcessorElasticsearchClient(builder.Configuration);
        builder.Services.AddSingleton<SearchIndexTemplateInitializer>();
        builder.Services.AddSingleton<IRunIndexer, ElasticsearchRunIndexer>();
        builder.Services.AddScoped<RunReplayService>();
        // Aggregate recompute: options + replay-completion bridge + the publisher, then the
        // scoped upserter/rebuild service. The trigger is registered after RunReplayTrigger so it
        // starts after replay's start (the notifier makes order-tolerant anyway).
        builder.Services.AddSingleton(AggregateOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddSingleton<ReplayCompletionNotifier>();
        builder.Services.AddSingleton<IAggregatePublisher, KafkaAggregatePublisher>();
        builder.Services.AddScoped<AggregateUpserter>();
        builder.Services.AddScoped<AggregateRebuildService>();
        // Embeddings chain: options + HttpClient + client, the text builder, and
        // the scoped embedder. The hosted consumer is registered unconditionally
        // and no-ops when Processor:Embeddings:Enabled is not true (default),
        // mirroring how the replay trigger is gated.
        builder.Services.AddEmbeddingServices(builder.Configuration);
        builder.Services.AddSingleton<RunEmbeddingTextBuilder>();
        builder.Services.AddScoped<RunEmbedder>();
        // Self-telemetry: options + the token typed client + the named ingest client, then the
        // hosted emitter. The emitter is registered LAST (after the aggregate trigger) and is a
        // silent no-op when SelfTelemetry:Enabled is not true (default). The ingest client is a
        // bare named client mirroring the Collector, which builds full URIs from its config.
        builder.Services.AddSingleton(SelfTelemetryOptions.FromConfiguration(builder.Configuration));
        builder.Services.AddHttpClient<SelfTelemetryTokenClient>();
        builder.Services.AddHttpClient("self-telemetry");
        // Replay first (it re-publishes agent_runs as PricedRun; the index
        // consumer is Earliest and catches up), then the live consumers.
        builder.Services.AddHostedService<RunReplayTrigger>();
        builder.Services.AddHostedService<AggregateRebuildTrigger>();
        builder.Services.AddHostedService<TelemetryRawConsumer>();
        builder.Services.AddHostedService<PricedRunIndexConsumer>();
        builder.Services.AddHostedService<EmbeddedRunConsumer>();
        builder.Services.AddHostedService<WasteDetectionConsumer>();
        builder.Services.AddHostedService<ImportCommandWorker>();
        builder.Services.AddHostedService<SelfTelemetryEmitter>();
        return builder;
    }

    public static async Task<WebApplication> InitializeProcessorAsync(this WebApplication app, CancellationToken cancellationToken = default)
    {
        await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TelemetryDbContext>().Database.MigrateAsync(cancellationToken);
        await scope.ServiceProvider.GetRequiredService<PricingSeeder>().SeedAsync(cancellationToken);
        // Topics must exist before the hosted consumers subscribe; Program.cs
        // awaits this, and endpoint-authorization tests never call it.
        await scope.ServiceProvider.GetRequiredService<KafkaTopicInitializer>().EnsureTopicsAsync(cancellationToken);
        return app;
    }

    public static WebApplication MapProcessorEndpoints(this WebApplication app)
    {
        app.UseTokenBurnAuth();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/health/ready");
        app.MapImportsEndpoints();
        return app;
    }
}

internal sealed class TelemetryRawConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<TelemetryRawConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");
        ConsumerConfig consumerConfig = new()
        {
            BootstrapServers = bootstrapServers,
            GroupId = "processor-telemetry-raw",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        using IProducer<string, string> producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            EnableIdempotence = true
        }).Build();
        consumer.Subscribe(KafkaTopics.Raw);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result = null!;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    using IServiceScope scope = scopeFactory.CreateScope();
                    SourceDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<SourceDispatcher>();
                    PricingEngine engine = scope.ServiceProvider.GetRequiredService<PricingEngine>();
                    AgentRunUpserter upserter = scope.ServiceProvider.GetRequiredService<AgentRunUpserter>();
                    AgentMessageUpserter messageUpserter = scope.ServiceProvider.GetRequiredService<AgentMessageUpserter>();
                    foreach (NormalizedRun envelope in dispatcher.Map(result.Message.Value))
                    {
                        AgentRun run = AgentRunEnvelopeMapper.ToAgentRun(envelope);
                        Result pricing = await engine.PriceRunAsync(run, stoppingToken);
                        if (!pricing.IsSuccess)
                            logger.LogWarning("Pricing run {RunId} failed: {Error}", run.Id, pricing.ErrorMessage);
                        (Guid storedId, bool applied) = await upserter.UpsertAsync(run, stoppingToken);
                        // Applied is false when the ended_at guard rejected the replay: the stored
                        // run kept its original pricing, so re-pricing messages would break
                        // SUM(messages.cost) = run.cost. Keep the stored run's messages untouched.
                        if (applied && run.PricingStatus == PricingStatus.Priced && envelope.Messages.Count > 0)
                        {
                            AgentMessage[] messages = envelope.Messages
                                .Select(message => AgentMessageEnvelopeMapper.ToAgentMessage(storedId, message))
                                .ToArray();
                            var messagePricing = await engine.PriceMessagesAsync(run, messages, stoppingToken);
                            if (!messagePricing.IsSuccess)
                                logger.LogWarning("Pricing messages for run {RunId} failed: {Error}", storedId, messagePricing.ErrorMessage);
                            else
                                await messageUpserter.UpsertAsync(storedId, messages, stoppingToken);
                        }
                        // Crash-safe order: upsert -> publish -> commit. A crash between publish
                        // and commit re-publishes the PricedRun; the index layer collapses the
                        // duplicate (_id = runId overwrite), so the run doc count stays distinct.
                        Contracts.PricedRun priced = PricedRunMapper.ToPricedRun(run) with { Id = storedId };
                        await producer.ProduceAsync(KafkaTopics.Priced, new Message<string, string>
                        {
                            Key = priced.SessionId,
                            Value = KafkaJsonSerializer.Serialize(priced)
                        }, stoppingToken);
                    }
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Never swallow: committing past an un-processed offset silently drops the
                    // message and it is recoverable only by full replay. Crash the host so the
                    // consumer restarts at the last committed offset; the idempotent upsert makes
                    // reprocessing safe. 'result' is null when Consume itself failed, so the
                    // offset is best-effort and must never be dereferenced unconditionally.
                    logger.LogError(exception, "Failed to process telemetry.raw message at {TopicPartitionOffset}.", result?.TopicPartitionOffset);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            consumer.Close();
        }
    }
}

/// <summary>
///     Consumes <c>telemetry.priced</c>, runs waste detection for each priced run and persists
///     the findings — publish-and-forget, no downstream topic. A crash between persist and
///     commit re-delivers the PricedRun; the ON CONFLICT (run_id, kind, evidence_hash) dedupe
///     makes the re-detect idempotent, so a replay cannot double-count a finding.
/// </summary>
internal sealed class WasteDetectionConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<WasteDetectionConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");
        ConsumerConfig consumerConfig = new()
        {
            BootstrapServers = bootstrapServers,
            GroupId = "processor-telemetry-waste",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(KafkaTopics.Priced);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result = null!;
                try
                {
                    result = consumer.Consume(stoppingToken);
                    using IServiceScope scope = scopeFactory.CreateScope();
                    WasteDetectionService service = scope.ServiceProvider.GetRequiredService<WasteDetectionService>();
                    PricedRun priced = KafkaJsonSerializer.Deserialize<PricedRun>(result.Message.Value)
                        ?? throw new InvalidOperationException("PricedRun payload deserialized to null.");
                    await service.DetectRunAsync(priced.Id, stoppingToken);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // Same crash-on-error posture as the other consumers: never commit past an
                    // un-processed offset, because the only recovery would be a full replay. The
                    // idempotent findings upsert makes reprocessing safe.
                    logger.LogError(exception, "Failed to detect waste for telemetry.priced message at {TopicPartitionOffset}.", result?.TopicPartitionOffset);
                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            consumer.Close();
        }
    }
}
