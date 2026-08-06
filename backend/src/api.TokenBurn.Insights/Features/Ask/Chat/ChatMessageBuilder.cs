using System.Text;
using Microsoft.Extensions.AI;

namespace Api.TokenBurn.Insights.Features.Ask.Chat;

/// <summary>
///     Builds the system + user prompt from the ALREADY-redacted context. The user message
///     carries only allow-listed fields (runId/sessionId/persona/modelSlug/status/startedAt/
///     tokens/cost/excerpt for traces; title/ordinal/excerpt ONLY for documents — the document
///     <c>uri</c> is an absolute filesystem path in real corpora and must never leave to a
///     third-party model; it is exposed only on the authed API surface). Excerpts are redacted
///     before they reach this builder, so nothing here can reintroduce a denied field.
/// </summary>
public sealed class ChatMessageBuilder
{
    private const string SystemInstruction =
        "You are a RAG assistant over the TokenBurn telemetry corpus. " +
        "Answer ONLY from the citations provided below. " +
        "If the citations do not contain the answer, say so plainly. " +
        "Do not fabricate information and do not include content absent from the citations.";

    public IList<ChatMessage> Build(
        string question,
        IReadOnlyList<RedactedTraceContext> traces,
        IReadOnlyList<RedactedDocumentContext> documents)
    {
        var user = new StringBuilder();
        user.Append("Question: ").Append(question).Append("\n\n");

        if (traces.Count > 0)
        {
            user.Append("Trace citations:\n");
            foreach (RedactedTraceContext trace in traces)
                user.Append("- run_id: ").Append(trace.RunId.ToString("D"))
                    .Append(" | session_id: ").Append(trace.SessionId)
                    .Append(" | persona: ").Append(trace.Persona ?? "unknown")
                    .Append(" | model: ").Append(trace.ModelSlug ?? "unknown")
                    .Append(" | status: ").Append(trace.Status)
                    .Append(" | started_at: ").Append(trace.StartedAt?.ToString("O") ?? "unknown")
                    .Append(" | tokens: ").Append(trace.Tokens)
                    .Append(" | cost: ").Append(trace.CostUsd ?? 0m)
                    .Append(" | excerpt: ").Append(trace.Excerpt).Append('\n');
        }

        if (documents.Count > 0)
        {
            user.Append("Document citations:\n");
            foreach (RedactedDocumentContext document in documents)
                user.Append("- title: ").Append(document.Title)
                    .Append(" | ordinal: ").Append(document.Ordinal)
                    .Append(" | excerpt: ").Append(document.Excerpt).Append('\n');
        }

        return
        [
            new ChatMessage(ChatRole.System, SystemInstruction),
            new ChatMessage(ChatRole.User, user.ToString())
        ];
    }
}
