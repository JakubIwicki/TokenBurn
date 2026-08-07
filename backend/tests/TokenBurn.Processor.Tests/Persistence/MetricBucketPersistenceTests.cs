using Microsoft.EntityFrameworkCore;
using Npgsql;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;

namespace TokenBurn.Processor.Tests.Persistence;

public sealed class MetricBucketPersistenceTests : TelemetryHandlerTestBase
{
    private static readonly DateOnly TestBucketDay = new(2026, 8, 1);

    [Fact]
    public async Task Upsert_ConvergesOnCompositeKey()
    {
        (await Context.MetricBuckets.AsNoTracking().CountAsync()).Should().Be(0);
        MetricBucket first = MetricBucket.Create(TestBucketDay, "deepseek-v4-flash", "gateway", runCount: 1, pricedRunCount: 1, messageCount: 10, inputTokens: 100, cacheReadTokens: 20, cacheWriteTokens: 30, outputTokens: 50, costUsd: 0.01m);
        MetricBucket replay = MetricBucket.Create(TestBucketDay, "deepseek-v4-flash", "gateway", runCount: 7, pricedRunCount: 2, messageCount: 25, inputTokens: 250, cacheReadTokens: 50, cacheWriteTokens: 60, outputTokens: 120, costUsd: 0.02m);

        await UpsertAsync(Context, first, CancellationToken.None);
        await UpsertAsync(Context, replay, CancellationToken.None);

        (await Context.MetricBuckets.AsNoTracking().CountAsync()).Should().Be(1);
        MetricBucket row = await ReadBucketAsync(Context, TestBucketDay, "deepseek-v4-flash", "gateway", CancellationToken.None);
        row.RunCount.Should().Be(7);
    }

    [Fact]
    public async Task Sentinel_RoundTrips()
    {
        // Null slug and whitespace service both collapse to the __unknown__ sentinel in the
        // factory, so the NOT NULL composite key still resolves for the unknown bucket.
        MetricBucket bucket = MetricBucket.Create(TestBucketDay, null!, "  ", runCount: 3, pricedRunCount: 2, messageCount: 40, inputTokens: 400, cacheReadTokens: 0, cacheWriteTokens: 0, outputTokens: 200, costUsd: 0.05m);

        await UpsertAsync(Context, bucket, CancellationToken.None);

        MetricBucket row = await ReadBucketAsync(Context, TestBucketDay, MetricBucket.UnknownBucket, MetricBucket.UnknownBucket, CancellationToken.None);
        row.ModelSlug.Should().Be(MetricBucket.UnknownBucket);
        row.Service.Should().Be(MetricBucket.UnknownBucket);
    }

    [Fact]
    public async Task TableLivesInMetricsSchema_NotTelemetry()
    {
        bool metricsAggregateExists = await TableExistsAsync(Context, "metrics", "aggregate", CancellationToken.None);
        bool telemetryAggregateExists = await TableExistsAsync(Context, "telemetry", "aggregate", CancellationToken.None);

        metricsAggregateExists.Should().BeTrue();
        telemetryAggregateExists.Should().BeFalse();
    }

    private static async Task UpsertAsync(TelemetryDbContext db, MetricBucket bucket, CancellationToken ct)
    {
        // Reuses the context's own connection (same credentials EF queries use) via positional
        // parameters; the ON CONFLICT target is the composite PK (bucket_day, model_slug, service).
        object[] values = [bucket.BucketDay, bucket.ModelSlug, bucket.Service, bucket.RunCount, bucket.PricedRunCount, bucket.MessageCount, bucket.InputTokens, bucket.CacheReadTokens, bucket.CacheWriteTokens, bucket.OutputTokens, bucket.CostUsd];
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO metrics.aggregate (bucket_day, model_slug, service, run_count, priced_run_count, message_count, input_tokens, cache_read_tokens, cache_write_tokens, output_tokens, cost_usd)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, {7}, {8}, {9}, {10})
            ON CONFLICT (bucket_day, model_slug, service) DO UPDATE SET run_count = EXCLUDED.run_count
            """,
            values,
            ct);
    }

    private static async Task<MetricBucket> ReadBucketAsync(TelemetryDbContext db, DateOnly bucketDay, string modelSlug, string service, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = """
                SELECT bucket_day, model_slug, service, run_count, priced_run_count, message_count, input_tokens, cache_read_tokens, cache_write_tokens, output_tokens, cost_usd
                FROM metrics.aggregate
                WHERE bucket_day = @bucket_day AND model_slug = @model_slug AND service = @service
                """;
            command.Parameters.AddWithValue("bucket_day", bucketDay);
            command.Parameters.AddWithValue("model_slug", modelSlug);
            command.Parameters.AddWithValue("service", service);
            await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(ct);
            await reader.ReadAsync(ct);
            return MetricBucket.FromReader(reader);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    private static async Task<bool> TableExistsAsync(TelemetryDbContext db, string schema, string table, CancellationToken ct)
    {
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            NpgsqlConnection connection = (NpgsqlConnection)db.Database.GetDbConnection();
            await using NpgsqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass(@qualifiedName) IS NOT NULL";
            command.Parameters.AddWithValue("qualifiedName", $"{schema}.{table}");
            return (bool)(await command.ExecuteScalarAsync(ct))!;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }
}
