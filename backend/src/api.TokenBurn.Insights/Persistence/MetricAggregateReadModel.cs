namespace Api.TokenBurn.Insights.Persistence;

/// <summary>
///     Read-only projection of <c>metrics.aggregate</c> — the public-safe
///     aggregation surface (privacy-boundary rule 2). Column mapping mirrors
///     <c>MetricBucketConfiguration</c> exactly; the consumer seeds its cache
///     from this durable store because Kafka retention is bounded
///     (telemetry-pipeline rule 9).
/// </summary>
public sealed class MetricAggregateReadModel
{
    public DateOnly BucketDay { get; set; }
    public string ModelSlug { get; set; } = null!;
    public string Service { get; set; } = null!;
    public long RunCount { get; set; }
    public long PricedRunCount { get; set; }
    public long MessageCount { get; set; }
    public long InputTokens { get; set; }
    public long CacheReadTokens { get; set; }
    public long CacheWriteTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
}
