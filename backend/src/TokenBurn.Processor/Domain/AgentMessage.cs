using TokenBurn.Common.Primitives;

namespace TokenBurn.Processor.Domain;

/// <summary>
///     One retained message row of an <see cref="AgentRun" />, keyed on
///     (run_id, sequence). Cost attaches after pricing and is null for
///     unpriced runs; the four token counters are never null so the per-message
///     cost sums exactly to the run cost (CostCalculator.Compute is linear).
/// </summary>
public sealed class AgentMessage : BaseEntity<Guid>
{
    public Guid RunId { get; private init; }
    public int Sequence { get; private init; }
    public string Role { get; private init; } = null!;
    public string? Content { get; private init; }
    public string? ToolName { get; private init; }
    public string? ModelSlug { get; private init; }
    public long InputTokens { get; private init; }
    public long CacheReadTokens { get; private init; }
    public long CacheWriteTokens { get; private init; }
    public long OutputTokens { get; private init; }
    public decimal? CostUsd { get; private set; }
    public DateTimeOffset OccurredAt { get; private init; }
    public int Version { get; private init; }

    private AgentMessage() { }

    public static AgentMessage Create(
        Guid runId, int sequence, string role, string? content, string? toolName, string? modelSlug,
        long inputTokens, long cacheReadTokens, long cacheWriteTokens, long outputTokens,
        DateTimeOffset occurredAt)
        => new()
        {
            Id = Guid.NewGuid(), RunId = runId, Sequence = sequence, Role = role ?? "",
            Content = content, ToolName = toolName, ModelSlug = modelSlug,
            InputTokens = inputTokens, CacheReadTokens = cacheReadTokens,
            CacheWriteTokens = cacheWriteTokens, OutputTokens = outputTokens,
            OccurredAt = occurredAt, Version = 1
        };

    public Result AttachCost(decimal costUsd)
    {
        if (CostUsd is not null)
            return Result.Conflict($"Cannot re-price message {Sequence} of run {RunId}.");
        CostUsd = costUsd;
        return Result.Success();
    }
}
