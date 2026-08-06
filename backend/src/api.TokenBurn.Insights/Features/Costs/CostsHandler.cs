using Api.TokenBurn.Insights.Persistence;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Api.TokenBurn.Insights.Features.Costs;

public sealed class CostsHandler(InsightsDbContext db) : IRequestHandler<CostsQuery, CostSummaryResponse>
{
    public Task<CostSummaryResponse> Handle(CostsQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<CostSummaryResponse> HandleAsync(CostsQuery request, CancellationToken cancellationToken)
    {
        // Day-bucketing needs (started_at AT TIME ZONE 'UTC')::date, which EF LINQ
        // cannot translate, so the aggregation is one parameterized raw query with
        // SQL GROUP BY. The fragment strings below come ONLY from the fixed switch
        // sets (never from request.GroupBy), so string interpolation is safe; user
        // input rides the @from/@to parameters alone.
        string keyExpression = request.GroupBy switch
        {
            "day" => "(started_at AT TIME ZONE 'UTC')::date::text",
            "model" => "model_slug",
            "persona" => "persona",
            _ => "NULL"
        };
        string groupClause = request.GroupBy switch
        {
            "day" => "(started_at AT TIME ZONE 'UTC')::date",
            "model" => "model_slug",
            "persona" => "persona",
            _ => "()"
        };
        string orderClause = request.GroupBy switch
        {
            "day" => "\"Key\" ASC",
            "model" => "\"CostUsd\" DESC NULLS LAST",
            "persona" => "\"CostUsd\" DESC NULLS LAST",
            _ => "1"
        };

        string sql = $"""
            SELECT COALESCE({keyExpression}, '(unknown)') AS "Key",
                   COUNT(*) AS "RunCount",
                   COALESCE(SUM(COALESCE(input_tokens, 0)), 0)::bigint AS "InputTokens",
                   COALESCE(SUM(COALESCE(cache_read_tokens, 0)), 0)::bigint AS "CacheReadTokens",
                   COALESCE(SUM(COALESCE(cache_write_tokens, 0)), 0)::bigint AS "CacheWriteTokens",
                   COALESCE(SUM(COALESCE(output_tokens, 0)), 0)::bigint AS "OutputTokens",
                   SUM(cost_usd) AS "CostUsd",
                   SUM(reported_cost_usd) AS "ReportedCostUsd",
                   (SUM(CASE WHEN pricing_status = 'Priced'
                             THEN COALESCE(input_tokens,0)+COALESCE(cache_read_tokens,0)+COALESCE(cache_write_tokens,0)+COALESCE(output_tokens,0)
                             ELSE 0 END)::numeric
                     / NULLIF(SUM(COALESCE(input_tokens,0)+COALESCE(cache_read_tokens,0)+COALESCE(cache_write_tokens,0)+COALESCE(output_tokens,0)), 0)) AS "PricingCoverage"
            FROM telemetry.agent_runs
            WHERE (@from IS NULL OR started_at >= @from)
              AND (@to IS NULL OR started_at <= @to)
            GROUP BY {groupClause}
            ORDER BY {orderClause}
            """;

        // TimestampTz must be explicit: an untyped DBNull parameter makes Postgres
        // raise 42P08 ("could not determine data type of parameter") even though the
        // OR-guards make the value irrelevant — it still has to resolve the type.
        var parameters = new object[]
        {
            new NpgsqlParameter("from", NpgsqlDbType.TimestampTz) { Value = (object?)request.From ?? DBNull.Value },
            new NpgsqlParameter("to", NpgsqlDbType.TimestampTz) { Value = (object?)request.To ?? DBNull.Value }
        };

        // SqlQueryRaw has no CancellationToken overload; the token stays in the
        // signature for call-site consistency and has nothing to forward to here.
        List<CostBucketRow> rows = await db.Database
            .SqlQueryRaw<CostBucketRow>(sql, parameters)
            .ToListAsync(cancellationToken);

        if (request.GroupBy is null)
        {
            CostTotals totals = ToTotals(rows.Single());
            return new CostSummaryResponse { Totals = totals, Buckets = [], PricingCoverage = totals.PricingCoverage };
        }

        List<CostBucket> buckets = rows.Select(ToBucket).ToList();
        CostTotals aggregate = Aggregate(buckets);
        return new CostSummaryResponse { Totals = aggregate, Buckets = buckets, PricingCoverage = aggregate.PricingCoverage };
    }

    private static CostTotals ToTotals(CostBucketRow row) => new()
    {
        RunCount = row.RunCount,
        InputTokens = row.InputTokens,
        CacheReadTokens = row.CacheReadTokens,
        CacheWriteTokens = row.CacheWriteTokens,
        OutputTokens = row.OutputTokens,
        CostUsd = row.CostUsd,
        ReportedCostUsd = row.ReportedCostUsd,
        PricingCoverage = (double)(row.PricingCoverage ?? 0m)
    };

    private static CostBucket ToBucket(CostBucketRow row) => new()
    {
        Key = row.Key,
        RunCount = row.RunCount,
        InputTokens = row.InputTokens,
        CacheReadTokens = row.CacheReadTokens,
        CacheWriteTokens = row.CacheWriteTokens,
        OutputTokens = row.OutputTokens,
        CostUsd = row.CostUsd,
        ReportedCostUsd = row.ReportedCostUsd,
        PricingCoverage = (double)(row.PricingCoverage ?? 0m)
    };

    private static CostTotals Aggregate(List<CostBucket> buckets)
    {
        long tokenTotal = buckets.Sum(bucket => bucket.InputTokens + bucket.CacheReadTokens + bucket.CacheWriteTokens + bucket.OutputTokens);

        return new CostTotals
        {
            RunCount = buckets.Sum(bucket => bucket.RunCount),
            InputTokens = buckets.Sum(bucket => bucket.InputTokens),
            CacheReadTokens = buckets.Sum(bucket => bucket.CacheReadTokens),
            CacheWriteTokens = buckets.Sum(bucket => bucket.CacheWriteTokens),
            OutputTokens = buckets.Sum(bucket => bucket.OutputTokens),
            CostUsd = SumNullable(buckets, bucket => bucket.CostUsd),
            ReportedCostUsd = SumNullable(buckets, bucket => bucket.ReportedCostUsd),
            PricingCoverage = tokenTotal == 0
                ? 0
                : buckets.Sum(bucket =>
                    (bucket.InputTokens + bucket.CacheReadTokens + bucket.CacheWriteTokens + bucket.OutputTokens)
                    * bucket.PricingCoverage) / tokenTotal
        };
    }

    private static decimal? SumNullable(List<CostBucket> buckets, Func<CostBucket, decimal?> selector)
    {
        decimal? sum = null;
        foreach (CostBucket bucket in buckets)
            if (selector(bucket) is { } value)
                sum = (sum ?? 0m) + value;
        return sum;
    }

    private sealed record CostBucketRow(
        string Key,
        long RunCount,
        long InputTokens,
        long CacheReadTokens,
        long CacheWriteTokens,
        long OutputTokens,
        decimal? CostUsd,
        decimal? ReportedCostUsd,
        decimal? PricingCoverage);
}
