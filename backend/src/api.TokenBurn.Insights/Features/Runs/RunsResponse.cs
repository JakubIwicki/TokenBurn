using Api.TokenBurn.Insights.Features.Findings;

namespace Api.TokenBurn.Insights.Features.Runs;

public sealed class RunsResponse
{
    public IReadOnlyList<RunSummary> Runs { get; init; } = [];
    public string? NextCursor { get; init; }
}

public sealed class RunSummary
{
    public Guid Id { get; init; }
    public string SessionId { get; init; } = null!;
    public string Source { get; init; } = null!;
    public string? ExternalId { get; init; }
    public string? Workspace { get; init; }
    public string? Persona { get; init; }
    public string? ModelSlug { get; init; }
    public string Status { get; init; } = null!;
    public string PricingStatus { get; init; } = null!;
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public long? InputTokens { get; init; }
    public long? OutputTokens { get; init; }
    public decimal? CostUsd { get; init; }
    public decimal? ReportedCostUsd { get; init; }
}

public sealed class RunDetailResponse
{
    public RunSummary Run { get; init; } = null!;
    // Messages table absent in Phase 4 — empty list keeps the contract stable
    // across phases without a breaking change (deferred by design).
    public IReadOnlyList<object> Messages { get; init; } = [];
    public IReadOnlyList<FindingSummary> Findings { get; init; } = [];
}
