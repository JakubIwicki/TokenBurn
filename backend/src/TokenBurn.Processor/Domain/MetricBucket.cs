namespace TokenBurn.Processor.Domain;

/// <summary>
///     One whole-corpus summary bucket keyed on <c>(bucket_day, model_slug, service)</c> — the
///     public-safe read surface the Phase 8 web projection consumes. Aggregate-only per
///     privacy-boundary rule 2: the row carries counts and sums and NOTHING else. Never add a
///     text, path, session, workspace, user or persona column — a message body or identifier here
///     would leak private source data into a public-readable projection.
/// </summary>
public sealed class MetricBucket
{
    public const string UnknownBucket = "__unknown__";

    public DateOnly BucketDay { get; private init; }
    public string ModelSlug { get; private init; } = null!;
    public string Service { get; private init; } = null!;
    public long RunCount { get; private init; }
    public long PricedRunCount { get; private init; }
    public long MessageCount { get; private init; }
    public long InputTokens { get; private init; }
    public long CacheReadTokens { get; private init; }
    public long CacheWriteTokens { get; private init; }
    public long OutputTokens { get; private init; }
    public decimal CostUsd { get; private init; }

    private MetricBucket() { }

    public static MetricBucket Create(
        DateOnly bucketDay,
        string modelSlug,
        string service,
        long runCount,
        long pricedRunCount,
        long messageCount,
        long inputTokens,
        long cacheReadTokens,
        long cacheWriteTokens,
        long outputTokens,
        decimal costUsd)
        => new()
        {
            BucketDay = bucketDay,
            // Table invariant: model_slug / service are never empty — a missing value collapses
            // to the shared sentinel so the composite key still resolves for the unknown bucket.
            ModelSlug = string.IsNullOrWhiteSpace(modelSlug) ? UnknownBucket : modelSlug,
            Service = string.IsNullOrWhiteSpace(service) ? UnknownBucket : service,
            RunCount = runCount,
            PricedRunCount = pricedRunCount,
            MessageCount = messageCount,
            InputTokens = inputTokens,
            CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens,
            OutputTokens = outputTokens,
            CostUsd = costUsd
        };

    /// <summary>
    ///     Materializes a bucket from a forward-only reader whose columns are
    ///     <c>bucket_day, model_slug, service, run_count, priced_run_count, message_count,
    ///     input_tokens, cache_read_tokens, cache_write_tokens, output_tokens, cost_usd</c>
    ///     in that order — the read path the rebuild service uses to republish recomputed buckets.
    /// </summary>
    public static MetricBucket FromReader(System.Data.Common.DbDataReader reader)
        => new()
        {
            BucketDay = reader.GetFieldValue<DateOnly>(0),
            ModelSlug = reader.GetString(1),
            Service = reader.GetString(2),
            RunCount = reader.GetInt64(3),
            PricedRunCount = reader.GetInt64(4),
            MessageCount = reader.GetInt64(5),
            InputTokens = reader.GetInt64(6),
            CacheReadTokens = reader.GetInt64(7),
            CacheWriteTokens = reader.GetInt64(8),
            OutputTokens = reader.GetInt64(9),
            CostUsd = reader.GetDecimal(10)
        };
}
