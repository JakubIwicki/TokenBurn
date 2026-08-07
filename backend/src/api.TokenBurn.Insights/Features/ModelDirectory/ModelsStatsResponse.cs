namespace Api.TokenBurn.Insights.Features.ModelDirectory;

public sealed class ModelsStatsResponse
{
    public IReadOnlyList<ModelStatsEntry> Stats { get; init; } = [];
}

public sealed class ModelStatsEntry
{
    public string ModelSlug { get; init; } = null!;
    public string Service { get; init; } = null!;
    public long RunCount { get; init; }
    public long PricedRunCount { get; init; }
    public long MessageCount { get; init; }
    public long InputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal CostUsd { get; init; }
}
