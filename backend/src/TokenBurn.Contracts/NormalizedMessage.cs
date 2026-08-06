namespace TokenBurn.Contracts;

/// <summary>
///     One retained message row of a <see cref="NormalizedRun" />. Sequence is a
///     1-based ordinal over the source rows that carry a message; OccurredAt is
///     the row's own timestamp (never null — see the adapter for the fallback
///     chain). Content and tool name are best-effort extractions and may be
///     null. Token counters default to 0 when the row carries no usage object.
/// </summary>
public sealed record NormalizedMessage
{
    public required int Sequence { get; init; }
    public required string Role { get; init; } = "";
    public string? Content { get; init; }
    public string? ToolName { get; init; }
    public string? ModelSlug { get; init; }
    public long InputTokens { get; init; }
    public long CacheReadTokens { get; init; }
    public long CacheWriteTokens { get; init; }
    public long OutputTokens { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
}
