namespace TokenBurn.Contracts;

/// <summary>
///     A fully normalized, priced run published to <c>telemetry.priced</c> after
///     the Postgres upsert. This is the transport shape the indexing consumer
///     (and, in Phase 5, the embedder) reads — a stable projection of the
///     domain aggregate that downstream stages can consume without touching
///     Postgres.
/// </summary>
public sealed record PricedRun
{
    public required Guid Id { get; init; }
    public required string SessionId { get; init; }
    public string AgentId { get; init; } = "";
    public required string Source { get; init; }
    public string? ExternalId { get; init; }
    public Guid? ParentRunId { get; init; }
    public string? Workspace { get; init; }
    public string? Persona { get; init; }
    public string? ModelSlug { get; init; }
    public string? Service { get; init; }
    public required RunStatus Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? EndedAt { get; init; }
    public long? InputTokens { get; init; }
    public long? CacheReadTokens { get; init; }
    public long? CacheWriteTokens { get; init; }
    public long? OutputTokens { get; init; }
    public required PricingStatus PricingStatus { get; init; }
    public decimal? CostUsd { get; init; }
    public decimal? ReportedCostUsd { get; init; }
    public decimal? PriceMultiplier { get; init; }
    public int Version { get; init; }
}
