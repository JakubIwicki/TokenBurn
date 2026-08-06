namespace Api.TokenBurn.Insights.Features.Costs;

public sealed class CostSummaryResponse
{
    public CostTotals Totals { get; init; } = null!;
    public IReadOnlyList<CostBucket> Buckets { get; init; } = [];
    public double PricingCoverage { get; init; }
}

public sealed class CostTotals
{
    public long RunCount { get; init; }
    public long InputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal? CostUsd { get; init; }
    public decimal? ReportedCostUsd { get; init; }
    public double PricingCoverage { get; init; }
}

public sealed class CostBucket
{
    public string Key { get; init; } = null!;
    public long RunCount { get; init; }
    public long InputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal? CostUsd { get; init; }
    public decimal? ReportedCostUsd { get; init; }
    public double PricingCoverage { get; init; }
}
