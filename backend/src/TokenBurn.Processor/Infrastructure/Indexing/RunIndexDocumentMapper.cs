using TokenBurn.Contracts;
using ContractsPricingStatus = TokenBurn.Contracts.PricingStatus;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     Maps a priced transport contract onto the Elasticsearch run document.
///     Enums become their string forms here — the document must never carry an
///     enum that the index template's keyword mapping has to interpret.
/// </summary>
public static class RunIndexDocumentMapper
{
    public static RunIndexDocument FromPricedRun(PricedRun run) => new()
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
        Status = ToString(run.Status),
        PricingStatus = ToString(run.PricingStatus),
        StartedAt = run.StartedAt,
        EndedAt = run.EndedAt,
        InputTokens = run.InputTokens,
        CacheReadTokens = run.CacheReadTokens,
        CacheWriteTokens = run.CacheWriteTokens,
        OutputTokens = run.OutputTokens,
        CostUsd = run.CostUsd,
        ReportedCostUsd = run.ReportedCostUsd,
        PriceMultiplier = run.PriceMultiplier,
        Version = run.Version,
        SearchableText = JoinSearchable(run)
    };

    public static string ToString(RunStatus status) => status switch
    {
        RunStatus.Running => "Running",
        RunStatus.Completed => "Completed",
        RunStatus.Failed => "Failed",
        RunStatus.Cancelled => "Cancelled",
        _ => "Unknown"
    };

    public static string ToString(ContractsPricingStatus status) => status switch
    {
        ContractsPricingStatus.Priced => "Priced",
        ContractsPricingStatus.Unpriceable => "Unpriceable",
        _ => "Quarantined"
    };

    private static string JoinSearchable(PricedRun run)
        => string.Join(' ', [run.Workspace, run.Persona, run.ExternalId, run.SessionId]);
}
