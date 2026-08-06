using Api.TokenBurn.Insights.Extensions.Embeddings;
using Api.TokenBurn.Insights.Features.Ask.Chat;
using Api.TokenBurn.Insights.Features.Search;
using Microsoft.Extensions.Logging;

namespace Api.TokenBurn.Insights.Features.Ask.Retrieval;

/// <summary>
///     Retrieval for ask: embeds the question ONCE, then runs the traces hybrid leg (top
///     <c>TraceTopK</c>) and the documents hybrid leg (top <c>DocTopK</c>) with the SAME
///     vector. If the question cannot be embedded (host without the embedding chain, failure,
///     timeout), both legs degrade to keyword-only. The trace hits are the search slice's
///     projection; the document hits are the ask slice's <see cref="DocumentChunkHit" />.
/// </summary>
public sealed class AskRetrievalService(
    HybridTracesRetrievalService tracesRetrieval,
    HybridDocumentsRetrievalService documentsRetrieval,
    Lazy<IEmbeddingClient> embeddings,
    AskOptions options,
    ILogger<AskRetrievalService> logger)
{
    public async Task<AskRetrievalResult> RetrieveAsync(AskQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<float> queryVector = await TryEmbedAsync(request.Question, cancellationToken);

        SearchQuery searchQuery = new(
            request.Question, "hybrid", request.Model, request.Persona, request.Source, request.Status,
            request.From, request.To, null, options.TraceTopK);
        SearchResponse traces = await tracesRetrieval.SearchAsync(searchQuery, queryVector, cancellationToken);
        IReadOnlyList<DocumentChunkHit> documents = await documentsRetrieval.SearchAsync(
            request.Question, queryVector, options.DocTopK, cancellationToken);

        return new AskRetrievalResult(traces.Hits, documents);
    }

    private async Task<IReadOnlyList<float>> TryEmbedAsync(string question, CancellationToken cancellationToken)
    {
        try
        {
            return await embeddings.Value.EmbedAsync([question], cancellationToken);
        }
        // A genuine caller/request cancellation must propagate; an embedding failure or timeout
        // degrades to keyword-only retrieval.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The ask question could not be embedded; running keyword-only retrieval.");
            return [];
        }
    }
}

/// <summary>
///     The two hit lists ask retrieval returns: fused trace hits and fused document chunks.
/// </summary>
public sealed record AskRetrievalResult(
    IReadOnlyList<SearchRunHit> TraceHits,
    IReadOnlyList<DocumentChunkHit> DocumentHits);
