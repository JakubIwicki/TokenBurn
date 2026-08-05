using TokenBurn.Contracts;

namespace TokenBurn.Processor.Domain;

/// <summary>
///     Translates a normalized envelope into the domain aggregate. The envelope
///     carries the transport vocabulary (<see cref="Contracts.RunStatus" />);
///     the aggregate owns the persistence vocabulary (<see cref="RunStatus" />).
///     All other fields pass through unchanged.
/// </summary>
public static class AgentRunEnvelopeMapper
{
    public static AgentRun ToAgentRun(NormalizedRun envelope)
        => AgentRun.Create(
            envelope.SessionId,
            envelope.AgentId,
            envelope.Source,
            envelope.ExternalId,
            envelope.Persona,
            envelope.ModelSlug,
            ToRunStatus(envelope.Status),
            envelope.StartedAt,
            envelope.EndedAt,
            envelope.InputTokens,
            envelope.CacheReadTokens,
            envelope.CacheWriteTokens,
            envelope.OutputTokens,
            envelope.ReportedCostUsd,
            envelope.Service,
            envelope.Workspace,
            envelope.ParentRunId);

    public static RunStatus ToRunStatus(Contracts.RunStatus status) => status switch
    {
        Contracts.RunStatus.Running => RunStatus.Running,
        Contracts.RunStatus.Completed => RunStatus.Completed,
        Contracts.RunStatus.Failed => RunStatus.Failed,
        Contracts.RunStatus.Cancelled => RunStatus.Cancelled,
        _ => RunStatus.Unknown
    };
}
