using TokenBurn.Common.Primitives;

namespace TokenBurn.Processor.Domain;

public enum PricingStatus
{
    Quarantined = 0,
    Priced = 1,
    Unpriceable = 2
}

public enum RunStatus
{
    Running,
    Completed,
    Failed,
    Cancelled,
    Unknown
}

public sealed class AgentRun : BaseEntity<Guid>
{
    public string SessionId { get; private init; } = null!;
    public string AgentId { get; private init; } = "";
    public string Source { get; private init; } = null!;
    public string? ExternalId { get; private init; }
    public Guid? ParentRunId { get; private init; }
    public string? Workspace { get; private init; }
    public string? Persona { get; private init; }
    public string? ModelSlug { get; private init; }
    public string? Service { get; private init; }
    public RunStatus Status { get; private set; }
    public PricingStatus PricingStatus { get; private set; }
    public DateTimeOffset? StartedAt { get; private init; }
    public DateTimeOffset? EndedAt { get; private set; }
    public long? InputTokens { get; private init; }
    public long? CacheReadTokens { get; private init; }
    public long? CacheWriteTokens { get; private init; }
    public long? OutputTokens { get; private init; }
    public decimal? CostUsd { get; private set; }
    public decimal? ReportedCostUsd { get; private init; }
    public decimal? PriceMultiplier { get; private set; }
    public int Version { get; private init; }

    private AgentRun() { }

    public static AgentRun Create(
        string sessionId, string agentId, string source, string? externalId, string? persona,
        string? modelSlug, RunStatus status, DateTimeOffset? startedAt,
        DateTimeOffset? endedAt, long? inputTokens, long? cacheReadTokens,
        long? cacheWriteTokens, long? outputTokens, decimal? reportedCostUsd,
        string? service = null, string? workspace = null, Guid? parentRunId = null)
        => new()
        {
            Id = Guid.NewGuid(), SessionId = sessionId, AgentId = agentId ?? "",
            Source = source, ExternalId = externalId, Persona = persona, ModelSlug = modelSlug,
            Service = service, Status = status, StartedAt = startedAt,
            EndedAt = endedAt, InputTokens = inputTokens,
            CacheReadTokens = cacheReadTokens, CacheWriteTokens = cacheWriteTokens, OutputTokens = outputTokens,
            ParentRunId = parentRunId, Workspace = workspace,
            ReportedCostUsd = reportedCostUsd, Version = 1
        };

    /// <summary>
    ///     Spec-of-record for run status transitions, exercised by unit tests. The
    ///     <c>WHERE</c> clause in <c>AgentRunUpserter.UpsertAsync</c> enforces only an
    ///     ordering-based guard under replay/redelivery — "newer terminal state wins" via the
    ///     <c>ended_at</c> predicate. It does NOT freeze a terminal status: a replay carrying a
    ///     newer <c>ended_at</c> can flip Completed to Failed, which this method rejects. That
    ///     invariant is guaranteed today only by the domain layer; pricing-once is enforced
    ///     separately by the upsert <c>CASE</c> guards, and the pricing engine prices only
    ///     final runs (<c>EndedAt</c> non-null).
    /// </summary>
    public Result TryTransitionTo(RunStatus target, DateTimeOffset now)
    {
        if (target == Status)
            return Result.Success();

        if (Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
            return Result.Conflict($"Cannot transition from terminal status '{Status}' to '{target}'.");

        Status = target;
        if (target is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
            EndedAt ??= now;
        return Result.Success();
    }

    /// <summary>
    ///     Spec-of-record for pricing a run, exercised by unit tests. Pricing-once is now
    ///     enforced at the database by the <c>CASE</c> guards in
    ///     <c>AgentRunUpserter.UpsertAsync</c>: once <c>pricing_status</c> is <c>Priced</c>, a
    ///     replayed adapter run carrying <c>PricingStatus = Quarantined</c> cannot reset an
    ///     already-priced run's pricing fields, which this method forbids. The pricing engine
    ///     prices only final runs (<c>EndedAt</c> non-null).
    /// </summary>
    public Result TryMarkPriced(decimal costUsd, decimal priceMultiplier)
    {
        if (PricingStatus != PricingStatus.Quarantined)
            return Result.Conflict($"Cannot price a run in '{PricingStatus}' state.");
        PricingStatus = PricingStatus.Priced;
        CostUsd = costUsd;
        PriceMultiplier = priceMultiplier;
        return Result.Success();
    }
}
