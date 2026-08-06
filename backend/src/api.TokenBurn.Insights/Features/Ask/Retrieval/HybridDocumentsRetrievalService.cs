using Api.TokenBurn.Insights.Features.Search;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;

namespace Api.TokenBurn.Insights.Features.Ask.Retrieval;

/// <summary>
///     Runs the two hybrid retrieval legs against the <c>documents</c> index — a keyword
///     MultiMatch leg on <c>chunk_text</c> and a kNN vector leg on <c>embedding</c> — and
///     fuses them with <see cref="RrfFusion" />. Top-k only (no positional cursor): ask
///     retrieval returns the <c>topK</c> best chunks. The vector leg degrades to keyword-only
///     on any failure, mirroring <see cref="HybridTracesRetrievalService" />; a missing
///     <c>documents</c> index (no corpus imported yet) returns an empty list, not an error.
/// </summary>
public sealed class HybridDocumentsRetrievalService(
    ElasticsearchClient client,
    HybridRetrievalOptions options,
    ILogger<HybridDocumentsRetrievalService> logger)
{
    private const string IndexName = "documents";
    private const string ChunkTextField = "chunk_text";
    private const string EmbeddingField = "embedding";
    private const int NumCandidates = 100;

    public async Task<IReadOnlyList<DocumentChunkHit>> SearchAsync(
        string query,
        IReadOnlyList<float> queryVector,
        int topK,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DocumentChunkHit> keywordHits = await RunKeywordLegAsync(query, cancellationToken);
        IReadOnlyList<DocumentChunkHit> vectorHits = queryVector.Count == 0
            ? []
            : await RunVectorLegAsync(queryVector, cancellationToken);

        var hitsById = new Dictionary<string, DocumentChunkHit>();
        foreach (DocumentChunkHit hit in keywordHits)
            hitsById[hit.Id] = hit;
        foreach (DocumentChunkHit hit in vectorHits)
            hitsById.TryAdd(hit.Id, hit);

        IReadOnlyList<RrfFusedHit> fused = RrfFusion.Fuse(
        [
            keywordHits.Select(hit => hit.Id).ToList(),
            vectorHits.Select(hit => hit.Id).ToList()
        ], options.RrfK);

        var results = new List<DocumentChunkHit>();
        foreach (RrfFusedHit fusedHit in fused.Take(topK))
        {
            if (hitsById.TryGetValue(fusedHit.Id, out DocumentChunkHit? hit))
            {
                hit.FusedScore = fusedHit.Score;
                results.Add(hit);
            }
        }
        return results;
    }

    private async Task<IReadOnlyList<DocumentChunkHit>> RunKeywordLegAsync(string query, CancellationToken cancellationToken)
    {
        SearchRequest searchRequest = new(IndexName)
        {
            Query = new MultiMatchQuery { Query = query, Fields = ChunkTextField },
            Sort = [new SortOptions { Field = new FieldSort { Field = "_score", Order = SortOrder.Desc } }],
            Size = options.KeywordLegSize,
            AllowNoIndices = true,
            IgnoreUnavailable = true
        };
        SearchResponse<DocumentChunkHit> response = await client.SearchAsync<DocumentChunkHit>(searchRequest, cancellationToken);
        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Elasticsearch documents keyword leg failed: {response.DebugInformation}");
        return response.Hits.Where(hit => hit.Source is not null).Select(hit => hit.Source!).ToList();
    }

    private async Task<IReadOnlyList<DocumentChunkHit>> RunVectorLegAsync(
        IReadOnlyList<float> queryVector,
        CancellationToken cancellationToken)
    {
        try
        {
            var knn = new KnnQuery
            {
                Field = EmbeddingField,
                QueryVector = queryVector.ToArray(),
                K = options.VectorLegSize,
                NumCandidates = NumCandidates
            };
            SearchRequest searchRequest = new(IndexName)
            {
                Query = knn,
                Size = options.VectorLegSize,
                AllowNoIndices = true,
                IgnoreUnavailable = true
            };
            SearchResponse<DocumentChunkHit> response = await client.SearchAsync<DocumentChunkHit>(searchRequest, cancellationToken);
            if (!response.IsValidResponse)
            {
                logger.LogWarning("Elasticsearch documents vector leg failed: {Detail}", response.DebugInformation);
                return [];
            }
            return response.Hits.Where(hit => hit.Source is not null).Select(hit => hit.Source!).ToList();
        }
        // A genuine caller/request cancellation must propagate; anything else (a documents index
        // that predates the embedding mapping, a vector-field error) degrades to keyword-only.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The documents vector leg failed; degrading to keyword-only fusion.");
            return [];
        }
    }
}
