using ContractsPricingStatus = TokenBurn.Contracts.PricingStatus;
using ContractsRunStatus = TokenBurn.Contracts.RunStatus;

namespace TokenBurn.Processor.Domain;

/// <summary>
///     Projects the priced domain aggregate onto the <c>telemetry.priced</c>
///     transport contract. The domain carries the persistence vocabulary;
///     <see cref="Contracts.PricedRun" /> owns the topic-chain vocabulary, so
///     the two enums convert here just as <c>AgentRunEnvelopeMapper</c>
///     converts at the entry boundary.
/// </summary>
public static class PricedRunMapper
{
    public static Contracts.PricedRun ToPricedRun(AgentRun run) => new()
    {
        Id = run.Id,
        SessionId = run.SessionId,
        AgentId = run.AgentId,
        Source = run.Source,
        ExternalId = run.ExternalId,
        ParentRunId = run.ParentRunId,
        Workspace = run.Workspace,
        Persona = run.Persona,
        ModelSlug = run.ModelSlug,
        Service = run.Service,
        Status = ToRunStatus(run.Status),
        StartedAt = run.StartedAt,
        EndedAt = run.EndedAt,
        InputTokens = run.InputTokens,
        CacheReadTokens = run.CacheReadTokens,
        CacheWriteTokens = run.CacheWriteTokens,
        OutputTokens = run.OutputTokens,
        PricingStatus = ToPricingStatus(run.PricingStatus),
        CostUsd = run.CostUsd,
        ReportedCostUsd = run.ReportedCostUsd,
        PriceMultiplier = run.PriceMultiplier,
        Version = run.Version
    };

    public static ContractsRunStatus ToRunStatus(RunStatus status) => status switch
    {
        RunStatus.Running => ContractsRunStatus.Running,
        RunStatus.Completed => ContractsRunStatus.Completed,
        RunStatus.Failed => ContractsRunStatus.Failed,
        RunStatus.Cancelled => ContractsRunStatus.Cancelled,
        _ => ContractsRunStatus.Unknown
    };

    public static ContractsPricingStatus ToPricingStatus(PricingStatus status) => status switch
    {
        PricingStatus.Priced => ContractsPricingStatus.Priced,
        PricingStatus.Unpriceable => ContractsPricingStatus.Unpriceable,
        _ => ContractsPricingStatus.Quarantined
    };
}
