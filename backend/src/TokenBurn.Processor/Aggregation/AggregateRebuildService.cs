using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TokenBurn.Contracts;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;

namespace TokenBurn.Processor.Aggregation;

/// <summary>
///     Recomputes <c>metrics.aggregate</c> from <c>telemetry.agent_runs</c> in one transaction and
///     republishes every surviving bucket to the <c>metrics.aggregate</c> Kafka topic. Full-recompute
///     semantics: the upsert replaces every column and stale buckets are deleted in the same
///     transaction, so a second rebuild converges to identical state (idempotent by construction).
/// </summary>
public sealed class AggregateRebuildService(
    TelemetryDbContext db,
    AggregateUpserter upserter,
    IAggregatePublisher publisher,
    AggregateOptions options,
    ILogger<AggregateRebuildService> logger)
{
    // Column order MUST match MetricBucket.FromReader's 11 positional ordinals: (0) bucket_day,
    // (1) model_slug, (2) service, (3) run_count, (4) priced_run_count, (5) message_count,
    // (6) input_tokens, (7) cache_read_tokens, (8) cache_write_tokens, (9) output_tokens,
    // (10) cost_usd. The SELECT below emits exactly that order — reordering it without updating
    // MetricBucket.FromReader would silently mis-map every bucket.
    private static readonly string RecomputeSql = $"""
        -- Runs with NULL started_at are excluded (can't attribute to a day — same as RunReplayService).
        WITH run_stats AS (
            SELECT r.started_at, r.model_slug, r.service, r.pricing_status, r.input_tokens,
                   r.cache_read_tokens, r.cache_write_tokens, r.output_tokens, r.cost_usd,
                   COALESCE(m.msg_count, 0)::bigint AS msg_count
            FROM telemetry.agent_runs r
            LEFT JOIN (SELECT run_id, COUNT(*)::bigint AS msg_count FROM telemetry.agent_messages GROUP BY run_id) m
                ON m.run_id = r.id
            WHERE r.started_at IS NOT NULL)
        -- Null/empty/whitespace slug or service collapse to the __unknown__ sentinel (MetricBucket.UnknownBucket).
        -- Quarantined runs count in run_count and all token sums but contribute 0 cost (their cost_usd
        -- is NULL -> COALESCE(...,0)); priced_run_count is the priced-only count. MinSize is enforced
        -- here (HAVING) so the table can never hold a sub-N bucket.
        SELECT timezone('UTC', started_at)::date AS bucket_day,
               COALESCE(NULLIF(btrim(model_slug), ''), '{MetricBucket.UnknownBucket}') AS model_slug,
               COALESCE(NULLIF(btrim(service), ''), '{MetricBucket.UnknownBucket}') AS service,
               COUNT(*)::bigint,
               COUNT(*) FILTER (WHERE pricing_status = 'Priced')::bigint,
               SUM(msg_count)::bigint,
               SUM(COALESCE(input_tokens, 0))::bigint,
               SUM(COALESCE(cache_read_tokens, 0))::bigint,
               SUM(COALESCE(cache_write_tokens, 0))::bigint,
               SUM(COALESCE(output_tokens, 0))::bigint,
               SUM(COALESCE(cost_usd, 0))::numeric(20, 10)
        FROM run_stats
        GROUP BY 1, 2, 3
        HAVING COUNT(*) >= @MinSize
        """;

    public async Task<int> RebuildAsync(CancellationToken cancellationToken)
    {
        List<MetricBucket> buckets = [];
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
        NpgsqlTransaction npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        try
        {
            buckets = await ComputeBucketsAsync(connection, npgsqlTransaction, cancellationToken);
            await upserter.UpsertAsync(connection, npgsqlTransaction, buckets, cancellationToken);
            await upserter.DeleteStaleAsync(connection, npgsqlTransaction, buckets, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        // Publish only after the commit: a publish failure leaves the table correct and the next
        // rebuild re-publishes everything (full-recompute), so the topic converges too.
        foreach (MetricBucket bucket in buckets)
            await publisher.PublishAsync(ToPublicAggregate(bucket), bucket.BucketDay, cancellationToken);

        logger.LogInformation("Aggregate rebuild produced {BucketCount} buckets.", buckets.Count);
        return buckets.Count;
    }

    private async Task<List<MetricBucket>> ComputeBucketsAsync(
        NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = RecomputeSql;
        command.Parameters.AddWithValue("MinSize", options.MinSize);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var buckets = new List<MetricBucket>();
        while (await reader.ReadAsync(cancellationToken))
            buckets.Add(MetricBucket.FromReader(reader));
        return buckets;
    }

    private static PublicAggregate ToPublicAggregate(MetricBucket bucket) => new(
        bucket.BucketDay, bucket.ModelSlug, bucket.Service, bucket.RunCount, bucket.PricedRunCount,
        bucket.MessageCount, bucket.InputTokens, bucket.CacheReadTokens, bucket.CacheWriteTokens,
        bucket.OutputTokens, bucket.CostUsd);
}
