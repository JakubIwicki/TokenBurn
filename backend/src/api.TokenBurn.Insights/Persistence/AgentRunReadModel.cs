namespace Api.TokenBurn.Insights.Persistence;

/// <summary>
///     Read-only projection of <c>telemetry.agent_runs</c>. Column mapping
///     mirrors <c>AgentRunConfiguration</c> exactly — any drift breaks the
///     query at runtime, which is covered by a seeded-read test.
/// </summary>
public sealed class AgentRunReadModel
{
    public Guid Id { get; set; }
    public string SessionId { get; set; } = null!;
    public string AgentId { get; set; } = "";
    public string Source { get; set; } = null!;
    public string? ExternalId { get; set; }
    public Guid? ParentRunId { get; set; }
    public string? Workspace { get; set; }
    public string? Persona { get; set; }
    public string? ModelSlug { get; set; }
    public string? Service { get; set; }
    public string Status { get; set; } = null!;
    public string PricingStatus { get; set; } = null!;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public long? InputTokens { get; set; }
    public long? CacheReadTokens { get; set; }
    public long? CacheWriteTokens { get; set; }
    public long? OutputTokens { get; set; }
    public decimal? CostUsd { get; set; }
    public decimal? ReportedCostUsd { get; set; }
    public decimal? PriceMultiplier { get; set; }
    public int Version { get; set; }
}
