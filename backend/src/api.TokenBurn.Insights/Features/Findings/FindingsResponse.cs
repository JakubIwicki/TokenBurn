namespace Api.TokenBurn.Insights.Features.Findings;

public sealed class FindingsResponse
{
    public IReadOnlyList<FindingSummary> Findings { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed class FindingSummary
{
    public Guid Id { get; init; }
    public Guid RunId { get; init; }
    public string Kind { get; init; } = null!;
    public string Severity { get; init; } = null!;
    public decimal? WastedCostUsd { get; init; }
    public DateTimeOffset DetectedAt { get; init; }
    public DateTimeOffset? AcknowledgedAt { get; init; }
}
