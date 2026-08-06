using Api.TokenBurn.Insights.Features.Ask.Chat;
using Api.TokenBurn.Insights.Features.Ask.Retrieval;
using MediatR;
using Microsoft.Extensions.AI;

namespace Api.TokenBurn.Insights.Features.Ask;

/// <summary>
///     Orchestrates <c>/api/ask</c>. Order is deliberate (privacy-boundary rule 7 and the
///     budget gate): (1) charge the per-principal budget BEFORE any retrieval or LLM spend,
///     throwing <see cref="AskBudgetExceededException" /> (mapped to 429) when exhausted;
///     (2) retrieve trace + document hits; (3) redact the context through the DEFAULT-DENY
///     projection; (4) build the prompt from redacted blocks only and call the chat client;
///     (5) project the response. No prompt or context text is ever included in an exception
///     this handler lets escape.
/// </summary>
public sealed class AskHandler(
    AskBudget budget,
    TimeProvider timeProvider,
    AskRetrievalService retrieval,
    ContextRedactor redactor,
    ChatMessageBuilder messageBuilder,
    IChatClient chatClient) : IRequestHandler<AskQuery, AskResponse>
{
    private const string Priced = "Priced";

    public Task<AskResponse> Handle(AskQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<AskResponse> HandleAsync(AskQuery request, CancellationToken cancellationToken)
    {
        // 1. Budget BEFORE retrieval: an exhausted principal is rejected without spending
        //    retrieval or provider money.
        if (!budget.TryCharge(request.Sub, timeProvider, cancellationToken))
            throw new AskBudgetExceededException();

        // 2. Retrieval.
        AskRetrievalResult hits = await retrieval.RetrieveAsync(request, cancellationToken);

        // 3. Redact to the allow-list before anything reaches the prompt.
        IReadOnlyList<RedactedTraceContext> traceContext = hits.TraceHits.Select(redactor.RedactTrace).ToList();
        IReadOnlyList<RedactedDocumentContext> documentContext = hits.DocumentHits.Select(redactor.RedactDocument).ToList();

        // 4. Build the prompt from redacted blocks and call the chat client.
        IList<ChatMessage> messages = messageBuilder.Build(request.Question, traceContext, documentContext);
        ChatResponse response = await chatClient.GetResponseAsync(messages, cancellationToken: cancellationToken);

        // 5. Project the allow-listed response.
        double coverage = hits.TraceHits.Count == 0
            ? 0
            : (double)hits.TraceHits.Count(hit => hit.PricingStatus == Priced) / hits.TraceHits.Count;

        var citations = new List<AskCitation>(traceContext.Count + documentContext.Count);
        foreach (RedactedTraceContext trace in traceContext)
            citations.Add(new AskCitation { Kind = AskCitationKind.Trace, RunId = trace.RunId, SessionId = trace.SessionId, Excerpt = trace.Excerpt });
        foreach (RedactedDocumentContext document in documentContext)
            citations.Add(new AskCitation { Kind = AskCitationKind.Document, Uri = document.Uri, Title = document.Title, ChunkIndex = document.Ordinal, Excerpt = document.Excerpt });

        var retrievalHits = new List<AskRetrievalHit>(traceContext.Count + documentContext.Count);
        foreach (RedactedTraceContext trace in traceContext)
            retrievalHits.Add(new AskRetrievalHit
            {
                Kind = AskCitationKind.Trace,
                RunId = trace.RunId,
                SessionId = trace.SessionId,
                Persona = trace.Persona,
                ModelSlug = trace.ModelSlug,
                Status = trace.Status,
                StartedAt = trace.StartedAt,
                Tokens = trace.Tokens,
                Cost = trace.CostUsd,
                Excerpt = trace.Excerpt
            });
        foreach (RedactedDocumentContext document in documentContext)
            retrievalHits.Add(new AskRetrievalHit
            {
                Kind = AskCitationKind.Document,
                Uri = document.Uri,
                Title = document.Title,
                Ordinal = document.Ordinal,
                Excerpt = document.Excerpt
            });

        return new AskResponse
        {
            Answer = response.Text ?? string.Empty,
            Citations = citations,
            Retrieval = retrievalHits,
            PricingCoverage = coverage
        };
    }
}
