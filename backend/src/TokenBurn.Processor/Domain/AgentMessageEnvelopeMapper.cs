using TokenBurn.Contracts;

namespace TokenBurn.Processor.Domain;

/// <summary>
///     Translates a retained <see cref="NormalizedMessage" /> envelope row into
///     the domain aggregate. The run id is not part of the message envelope —
///     it is keyed under its parent run — so the caller supplies the stored run
///     id. All fields pass through unchanged.
/// </summary>
public static class AgentMessageEnvelopeMapper
{
    public static AgentMessage ToAgentMessage(Guid runId, NormalizedMessage envelope)
        => AgentMessage.Create(
            runId,
            envelope.Sequence,
            envelope.Role,
            envelope.Content,
            envelope.ToolName,
            envelope.ModelSlug,
            envelope.InputTokens,
            envelope.CacheReadTokens,
            envelope.CacheWriteTokens,
            envelope.OutputTokens,
            envelope.OccurredAt);
}
