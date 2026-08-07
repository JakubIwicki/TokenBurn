namespace Api.TokenBurn.Insights.Features.ModelDirectory;

public sealed class ModelsDirectoryResponse
{
    public IReadOnlyList<ModelDirectoryEntry> Models { get; init; } = [];
}

public sealed class ModelDirectoryEntry
{
    public string Slug { get; init; } = null!;
    public string Provider { get; init; } = null!;
    public int? ContextWindow { get; init; }
    public decimal InputPerMtok { get; init; }
    public decimal CacheReadPerMtok { get; init; }
    public decimal CacheWritePerMtok { get; init; }
    public decimal OutputPerMtok { get; init; }
}
