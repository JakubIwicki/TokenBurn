using System.Text;
using Npgsql;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Persistence;

/// <summary>
///     Chunked upsert + stale-deletion for <c>metrics.aggregate</c> — the public-safe aggregate
///     surface. Runs over a caller-supplied open connection + transaction so the rebuild can write
///     the recompute, the upsert and the stale-deletion atomically on the DbContext's own connection
///     (the only credential path that authenticates in tests).
/// </summary>
public sealed class AggregateUpserter
{
    private const int BatchSize = 100;
    private const string SqlPrefix = """
        INSERT INTO metrics.aggregate
            (bucket_day, model_slug, service, run_count, priced_run_count, message_count,
             input_tokens, cache_read_tokens, cache_write_tokens, output_tokens, cost_usd)
        VALUES
        """;
    private const string SqlSuffix = """
        ON CONFLICT (bucket_day, model_slug, service) DO UPDATE SET
            run_count = EXCLUDED.run_count,
            priced_run_count = EXCLUDED.priced_run_count,
            message_count = EXCLUDED.message_count,
            input_tokens = EXCLUDED.input_tokens,
            cache_read_tokens = EXCLUDED.cache_read_tokens,
            cache_write_tokens = EXCLUDED.cache_write_tokens,
            output_tokens = EXCLUDED.output_tokens,
            cost_usd = EXCLUDED.cost_usd
        """;

    /// <summary>
    ///     Full-recompute upsert: every column is replaced, so re-running a rebuild converges to
    ///     identical state.
    /// </summary>
    public async Task UpsertAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<MetricBucket> buckets,
        CancellationToken cancellationToken)
    {
        if (buckets.Count == 0)
            return;

        foreach (MetricBucket[] chunk in buckets.Chunk(BatchSize))
            await UpsertChunkAsync(connection, transaction, chunk, cancellationToken);
    }

    /// <summary>
    ///     Removes rows whose composite key (bucket_day, model_slug, service) is NOT in the recomputed
    ///     set, restricted to the days the recompute examined. Stale = sub-N or vanished bucket; days
    ///     outside the recompute are never touched.
    /// </summary>
    public async Task DeleteStaleAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<MetricBucket> recomputed,
        CancellationToken cancellationToken)
    {
        // Covered days come from the recompute's input (any run with a started_at), not from the
        // recomputed set — a day whose buckets all fell below MinSize produces no recomputed rows but
        // must still have its stale rows removed, while a day with no runs at all is never touched.
        StringBuilder sql = new("""
            WITH covered_days AS (
                SELECT DISTINCT timezone('UTC', started_at)::date AS bucket_day
                FROM telemetry.agent_runs
                WHERE started_at IS NOT NULL)
            DELETE FROM metrics.aggregate a
            WHERE a.bucket_day IN (SELECT bucket_day FROM covered_days)
            """);
        if (recomputed.Count > 0)
        {
            sql.Append(" AND NOT EXISTS (SELECT 1 FROM (VALUES ");
            for (int i = 0; i < recomputed.Count; i++)
            {
                if (i > 0)
                    sql.Append(',');
                sql.Append("(@bucket_day_").Append(i).Append(", @model_slug_").Append(i)
                    .Append(", @service_").Append(i).Append(')');
            }
            sql.Append("""
                ) AS rc(bucket_day, model_slug, service)
                WHERE rc.bucket_day = a.bucket_day
                  AND rc.model_slug = a.model_slug
                  AND rc.service = a.service)
                """);
        }

        await using NpgsqlCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql.ToString();
        for (int i = 0; i < recomputed.Count; i++)
        {
            MetricBucket bucket = recomputed[i];
            command.Parameters.AddWithValue($"bucket_day_{i}", bucket.BucketDay);
            command.Parameters.AddWithValue($"model_slug_{i}", bucket.ModelSlug);
            command.Parameters.AddWithValue($"service_{i}", bucket.Service);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task UpsertChunkAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        IReadOnlyList<MetricBucket> chunk,
        CancellationToken cancellationToken)
    {
        StringBuilder sql = new(SqlPrefix);
        for (int i = 0; i < chunk.Count; i++)
        {
            if (i > 0)
                sql.Append(',');
            sql.Append("(@bucket_day_").Append(i).Append(", @model_slug_").Append(i)
                .Append(", @service_").Append(i).Append(", @run_count_").Append(i)
                .Append(", @priced_run_count_").Append(i).Append(", @message_count_").Append(i)
                .Append(", @input_tokens_").Append(i).Append(", @cache_read_tokens_").Append(i)
                .Append(", @cache_write_tokens_").Append(i).Append(", @output_tokens_").Append(i)
                .Append(", @cost_usd_").Append(i).Append(')');
        }
        sql.Append(SqlSuffix);

        await using NpgsqlCommand command = new(sql.ToString(), connection);
        command.Transaction = transaction;
        for (int i = 0; i < chunk.Count; i++)
        {
            MetricBucket bucket = chunk[i];
            command.Parameters.AddWithValue($"bucket_day_{i}", bucket.BucketDay);
            command.Parameters.AddWithValue($"model_slug_{i}", bucket.ModelSlug);
            command.Parameters.AddWithValue($"service_{i}", bucket.Service);
            command.Parameters.AddWithValue($"run_count_{i}", bucket.RunCount);
            command.Parameters.AddWithValue($"priced_run_count_{i}", bucket.PricedRunCount);
            command.Parameters.AddWithValue($"message_count_{i}", bucket.MessageCount);
            command.Parameters.AddWithValue($"input_tokens_{i}", bucket.InputTokens);
            command.Parameters.AddWithValue($"cache_read_tokens_{i}", bucket.CacheReadTokens);
            command.Parameters.AddWithValue($"cache_write_tokens_{i}", bucket.CacheWriteTokens);
            command.Parameters.AddWithValue($"output_tokens_{i}", bucket.OutputTokens);
            command.Parameters.AddWithValue($"cost_usd_{i}", bucket.CostUsd);
        }
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
