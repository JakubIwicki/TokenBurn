using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using TokenBurn.Processor.Aggregation;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Tests.Bases;

namespace TokenBurn.Processor.Tests.Aggregation;

public sealed class AggregateUpsertTests : TelemetryHandlerTestBase
{
    private static readonly DateOnly TestBucketDay = new(2026, 8, 1);
    private static readonly DateTimeOffset DayStart = new(2026, 8, 1, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Upsert_ConvergesOnCompositeKey()
    {
        (await Context.MetricBuckets.AsNoTracking().CountAsync()).Should().Be(0);
        MetricBucket first = MetricBucket.Create(TestBucketDay, "deepseek-v4-flash", "gateway", runCount: 1, pricedRunCount: 1, messageCount: 10, inputTokens: 100, cacheReadTokens: 20, cacheWriteTokens: 30, outputTokens: 50, costUsd: 0.01m);
        MetricBucket replay = MetricBucket.Create(TestBucketDay, "deepseek-v4-flash", "gateway", runCount: 7, pricedRunCount: 2, messageCount: 25, inputTokens: 250, cacheReadTokens: 50, cacheWriteTokens: 60, outputTokens: 120, costUsd: 0.02m);

        await UpsertAsync(first, CancellationToken.None);
        await UpsertAsync(replay, CancellationToken.None);

        (await Context.MetricBuckets.AsNoTracking().CountAsync()).Should().Be(1);
        MetricBucket row = await LoadBucketAsync(TestBucketDay, "deepseek-v4-flash", "gateway");
        row.RunCount.Should().Be(7);
        row.PricedRunCount.Should().Be(2);
        row.MessageCount.Should().Be(25);
        row.InputTokens.Should().Be(250);
        row.CacheReadTokens.Should().Be(50);
        row.CacheWriteTokens.Should().Be(60);
        row.OutputTokens.Should().Be(120);
        row.CostUsd.Should().Be(0.02m);
    }

    [Fact]
    public async Task DeleteStale_RemovesVanishedKeyWithinCoveredDay_AndPreservesRecomputedAndUncoveredRows()
    {
        // Two runs make the day covered for the recompute's day extraction.
        SeedRun("covered-1");
        SeedRun("covered-2");
        // Table rows: a recomputed key, a vanished key on the same covered day, and a row on an
        // uncovered day that must never be touched.
        await InsertAggregateAsync(TestBucketDay, "model-a", "gateway");
        await InsertAggregateAsync(TestBucketDay, "model-b", "gateway");
        await InsertAggregateAsync(new DateOnly(2026, 8, 2), "model-a", "gateway");
        MetricBucket recomputed = MetricBucket.Create(TestBucketDay, "model-a", "gateway", runCount: 2, pricedRunCount: 2, messageCount: 0, inputTokens: 0, cacheReadTokens: 0, cacheWriteTokens: 0, outputTokens: 0, costUsd: 0m);

        await DeleteStaleAsync([recomputed], CancellationToken.None);

        MetricBucket preserved = await LoadBucketAsync(TestBucketDay, "model-a", "gateway");
        preserved.RunCount.Should().Be(1);
        (await Context.MetricBuckets.AsNoTracking().CountAsync(x => x.BucketDay == TestBucketDay && x.ModelSlug == "model-b")).Should().Be(0);
        MetricBucket uncovered = await LoadBucketAsync(new DateOnly(2026, 8, 2), "model-a", "gateway");
        uncovered.ModelSlug.Should().Be("model-a");
    }

    private async Task UpsertAsync(MetricBucket bucket, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await Context.Database.BeginTransactionAsync(ct);
        NpgsqlConnection connection = (NpgsqlConnection)Context.Database.GetDbConnection();
        NpgsqlTransaction npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await new AggregateUpserter().UpsertAsync(connection, npgsqlTransaction, [bucket], ct);
        await transaction.CommitAsync(ct);
    }

    private async Task DeleteStaleAsync(IReadOnlyList<MetricBucket> recomputed, CancellationToken ct)
    {
        await using IDbContextTransaction transaction = await Context.Database.BeginTransactionAsync(ct);
        NpgsqlConnection connection = (NpgsqlConnection)Context.Database.GetDbConnection();
        NpgsqlTransaction npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();
        await new AggregateUpserter().DeleteStaleAsync(connection, npgsqlTransaction, recomputed, ct);
        await transaction.CommitAsync(ct);
    }

    private void SeedRun(string sessionId)
    {
        AgentRun run = AgentRun.Create(sessionId, "", "delegate-ledger", null, null,
            "model-a", RunStatus.Completed, DayStart, DayStart.AddMinutes(1),
            inputTokens: 10, cacheReadTokens: 0, cacheWriteTokens: 0, outputTokens: 0,
            reportedCostUsd: null, service: "gateway");
        run.TryMarkPriced(0.01m, 1.0m);
        Db.Store(run);
    }

    private async Task InsertAggregateAsync(DateOnly bucketDay, string modelSlug, string service)
    {
        await Context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO metrics.aggregate (bucket_day, model_slug, service, run_count, priced_run_count, message_count, input_tokens, cache_read_tokens, cache_write_tokens, output_tokens, cost_usd)
            VALUES ({0}, {1}, {2}, 1, 1, 0, 0, 0, 0, 0, 0.0)
            """,
            bucketDay, modelSlug, service);
    }

    private async Task<MetricBucket> LoadBucketAsync(DateOnly bucketDay, string modelSlug, string service)
    {
        Context.ChangeTracker.Clear();
        return await Context.MetricBuckets.AsNoTracking()
            .SingleAsync(x => x.BucketDay == bucketDay && x.ModelSlug == modelSlug && x.Service == service);
    }
}
