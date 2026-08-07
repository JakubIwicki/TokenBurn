using Api.TokenBurn.Insights.Persistence;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace Api.TokenBurn.Insights.Features.ModelDirectory;

/// <summary>
///     Consumes <c>metrics.aggregate</c> and keeps the in-memory
///     <see cref="PublicAggregateCache" /> warm. The cache is seeded from the
///     durable <c>metrics.aggregate</c> table FIRST (telemetry-pipeline rule 9 —
///     the DB is the source of truth, Kafka retention is bounded), then the topic
///     feeds live updates, and a periodic reconcile re-seeds from the DB so
///     buckets withdrawn by a rebuild converge out of the cache. Manual commit
///     after each successful message; a crash between upsert and commit
///     re-delivers the aggregate and the keyed upsert converges, so replay is
///     idempotent.
/// </summary>
internal sealed class MetricsAggregateConsumer(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    TimeProvider timeProvider,
    ILogger<MetricsAggregateConsumer> logger) : BackgroundService
{
    // Reconciles the cache against the durable metrics.aggregate table so buckets withdrawn by a
    // rebuild (a re-import shrinking a day below MinSize) and replayed Earliest history from a
    // fresh consumer group converge out of the cache. The DB is the source of truth
    // (telemetry-pipeline rule 9).
    private static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(60);

    // Bounded poll timeout so the reconcile runs even while the topic is idle.
    private static readonly TimeSpan ConsumeTimeout = TimeSpan.FromSeconds(5);

    // Back-off between ConsumeException retries so a transient broker outage is not a tight loop.
    private static readonly TimeSpan ConsumeErrorRetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SeedCacheFromDatabaseAsync(stoppingToken);

        string bootstrapServers = configuration["Kafka:BootstrapServers"]
            ?? throw new InvalidOperationException("Kafka:BootstrapServers must be configured.");
        ConsumerConfig consumerConfig = new()
        {
            BootstrapServers = bootstrapServers,
            GroupId = "insights-metrics-aggregate",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false
        };
        using IConsumer<string, string> consumer = new ConsumerBuilder<string, string>(consumerConfig).Build();
        consumer.Subscribe(KafkaTopics.Metrics);
        DateTimeOffset lastReconcile = timeProvider.GetUtcNow();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string> result = null!;
                try
                {
                    if (timeProvider.GetUtcNow() - lastReconcile >= ReconcileInterval)
                    {
                        await SeedCacheFromDatabaseAsync(stoppingToken);
                        lastReconcile = timeProvider.GetUtcNow();
                    }

                    result = consumer.Consume(ConsumeTimeout);
                    if (result is null)
                        continue;

                    using IServiceScope scope = scopeFactory.CreateScope();
                    PublicAggregateCache cache = scope.ServiceProvider.GetRequiredService<PublicAggregateCache>();
                    PublicAggregate aggregate = KafkaJsonSerializer.Deserialize<PublicAggregate>(result.Message.Value)
                        ?? throw new InvalidOperationException("PublicAggregate payload deserialized to null.");
                    cache.Upsert(aggregate);
                    consumer.Commit(result);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ConsumeException exception)
                {
                    // A transient broker/rebalance failure must not take down the whole Insights
                    // host (BackgroundServiceExceptionBehavior.StopHost) — the cache is lossy and
                    // re-seedable, unlike the Processor's crash-on-error consumers. Do not commit
                    // and do not throw; the periodic DB reconcile keeps the cache convergent.
                    logger.LogWarning("Metrics.aggregate consume failed transiently: {Reason}.", exception.Error.Reason);
                    await Task.Delay(ConsumeErrorRetryDelay, stoppingToken);
                }
                catch (Exception exception)
                {
                    // Same crash-on-error posture as the Processor consumers: never commit past an
                    // un-processed offset, because the only recovery would be a full replay. The
                    // keyed upsert makes reprocessing safe.
                    logger.LogError(exception, "Failed to process metrics.aggregate message at {TopicPartitionOffset}.", result?.TopicPartitionOffset);
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

    private async Task SeedCacheFromDatabaseAsync(CancellationToken stoppingToken)
    {
        using IServiceScope scope = scopeFactory.CreateScope();
        InsightsDbContext db = scope.ServiceProvider.GetRequiredService<InsightsDbContext>();
        PublicAggregateCache cache = scope.ServiceProvider.GetRequiredService<PublicAggregateCache>();
        List<PublicAggregate> rows = await db.MetricAggregates.AsNoTracking()
            .Select(row => new PublicAggregate(
                row.BucketDay,
                row.ModelSlug,
                row.Service,
                row.RunCount,
                row.PricedRunCount,
                row.MessageCount,
                row.InputTokens,
                row.CacheReadTokens,
                row.CacheWriteTokens,
                row.OutputTokens,
                row.CostUsd))
            .ToListAsync(stoppingToken);
        cache.ReplaceAll(rows);
        logger.LogInformation("Seeded metrics aggregate cache from database with {Count} buckets.", rows.Count);
    }
}
