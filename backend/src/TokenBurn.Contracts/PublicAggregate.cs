namespace TokenBurn.Contracts;

/// <summary>
///     Public-safe aggregate projection (privacy-boundary rule 2): counts, sums and model metadata
///     ONLY. One row per (bucket_day, model_slug, service). Never add message text, file paths,
///     session ids, workspace names, user identifiers or personas — a body or identifier here would
///     leak private source data into a public-readable projection. Cost is priced-only; quarantined
///     runs count in RunCount and the token sums but contribute 0 to CostUsd.
/// </summary>
public sealed record PublicAggregate(
    DateOnly BucketDay,
    string ModelSlug,
    string Service,
    long RunCount,
    long PricedRunCount,
    long MessageCount,
    long InputTokens,
    long CacheReadTokens,
    long CacheWriteTokens,
    long OutputTokens,
    decimal CostUsd);
