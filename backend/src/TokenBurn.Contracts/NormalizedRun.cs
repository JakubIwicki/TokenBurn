namespace TokenBurn.Contracts;

/// <summary>
///     Normalized run envelope — the single output shape every source-format
///     adapter emits. Identity is the session, not the source: a delegate
///     child is itself a Claude Code session, so the same run arrives from
///     multiple adapters and must collapse onto one row. <see cref="AgentId"/>
///     is the empty string for the main-thread run and non-empty only for
///     sidechains. No pricing fields cross this boundary — pricing happens
///     downstream on the domain aggregate.
/// </summary>
public sealed record NormalizedRun
{
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
    public long InputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long OutputTokens { get; init; }
    public decimal? ReportedCostUsd { get; init; }
}
