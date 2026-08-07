using TokenBurn.Contracts;

namespace Api.TokenBurn.Insights.Features.ModelDirectory;

/// <summary>
///     Singleton, thread-safe in-memory store of the public aggregate surface
///     (<c>TokenBurn.Contracts.PublicAggregate</c>), keyed by
///     <c>(bucket_day, model_slug, service)</c>. Seeded from the durable
///     <c>metrics.aggregate</c> table at consumer startup (telemetry-pipeline
///     rule 9 — the DB is the source of truth, Kafka retention is bounded),
///     then kept warm by <see cref="MetricsAggregateConsumer" />. Holds only
///     public-safe values (privacy-boundary rule 2).
/// </summary>
public sealed class PublicAggregateCache
{
    private readonly object _lock = new();
    private readonly Dictionary<AggregateKey, PublicAggregate> _rows = new();

    public void ReplaceAll(IEnumerable<PublicAggregate> rows)
    {
        lock (_lock)
        {
            _rows.Clear();
            foreach (PublicAggregate row in rows)
                _rows[Key(row)] = row;
        }
    }

    public void Upsert(PublicAggregate row)
    {
        lock (_lock)
        {
            _rows[Key(row)] = row;
        }
    }

    public IReadOnlyList<ModelStatsEntry> GetStats()
    {
        lock (_lock)
        {
            return _rows.Values
                .GroupBy(row => (row.ModelSlug, row.Service))
                .Select(group => new ModelStatsEntry
                {
                    ModelSlug = group.Key.ModelSlug,
                    Service = group.Key.Service,
                    RunCount = group.Sum(row => row.RunCount),
                    PricedRunCount = group.Sum(row => row.PricedRunCount),
                    MessageCount = group.Sum(row => row.MessageCount),
                    InputTokens = group.Sum(row => row.InputTokens),
                    CacheReadTokens = group.Sum(row => row.CacheReadTokens),
                    CacheWriteTokens = group.Sum(row => row.CacheWriteTokens),
                    OutputTokens = group.Sum(row => row.OutputTokens),
                    CostUsd = group.Sum(row => row.CostUsd)
                })
                .OrderBy(entry => entry.ModelSlug)
                .ThenBy(entry => entry.Service)
                .ToList();
        }
    }

    private static AggregateKey Key(PublicAggregate row) => new(row.BucketDay, row.ModelSlug, row.Service);

    private readonly record struct AggregateKey(DateOnly BucketDay, string ModelSlug, string Service);
}
