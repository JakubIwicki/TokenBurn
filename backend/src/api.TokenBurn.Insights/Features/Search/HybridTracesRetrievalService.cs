using Api.TokenBurn.Insights.Extensions.Embeddings;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using Microsoft.Extensions.Logging;
using TokenBurn.Common.Pagination;

namespace Api.TokenBurn.Insights.Features.Search;

/// <summary>
///     Runs the two hybrid retrieval legs against the <c>traces</c> index — a
///     keyword MultiMatch leg and a kNN vector leg — and fuses them with
///     <see cref="RrfFusion" />. Pagination is positional: every page re-runs
///     both legs and re-fuses, then continues strictly after the opaque
///     <c>score|id</c> cursor (hybrid pages are NOT search_after). The vector leg
///     degrades to keyword-only when embeddings are unavailable or fail, so hybrid
///     search still answers on a host without the embedding chain or an index
///     whose documents carry no vectors yet. Highlights are intentionally empty —
///     hybrid ranks fused hits, it does not highlight a single query field.
/// </summary>
public sealed class HybridTracesRetrievalService(
    ElasticsearchClient client,
    Lazy<IEmbeddingClient> embeddings,
    HybridRetrievalOptions options,
    ILogger<HybridTracesRetrievalService> logger)
{
    private const string IndexName = "traces";
    private const string SearchableTextField = "searchable_text";
    private const string EmbeddingField = "embedding";
    private const string IdField = "id";
    private const int NumCandidates = 100;

    public Task<SearchResponse> SearchAsync(SearchQuery request, CancellationToken cancellationToken)
        => SearchAsync(request, null, cancellationToken);

    /// <summary>
    ///     Runs both hybrid legs. When <paramref name="queryVector" /> is null the vector leg
    ///     embeds the query itself (unchanged keyword/hybrid behaviour); a non-empty vector is
    ///     reused as-is so ask embeds ONCE and feeds both the traces and documents legs; an
    ///     empty vector skips the vector leg (embedding unavailable — keyword-only).
    /// </summary>
    public async Task<SearchResponse> SearchAsync(SearchQuery request, IReadOnlyList<float>? queryVector, CancellationToken cancellationToken)
    {
        List<Query> filters = SearchFilterBuilder.BuildFilters(request);
        IReadOnlyList<SearchRunHit> keywordHits = await RunKeywordLegAsync(request, filters, cancellationToken);
        IReadOnlyList<SearchRunHit> vectorHits = await RunVectorLegAsync(request, filters, queryVector, cancellationToken);

        var hitsById = new Dictionary<string, SearchRunHit>();
        foreach (SearchRunHit hit in keywordHits)
            hitsById[hit.Id.ToString("D")] = hit;
        foreach (SearchRunHit hit in vectorHits)
            hitsById.TryAdd(hit.Id.ToString("D"), hit);

        IReadOnlyList<RrfFusedHit> fused = RrfFusion.Fuse(
        [
            keywordHits.Select(hit => hit.Id.ToString("D")).ToList(),
            vectorHits.Select(hit => hit.Id.ToString("D")).ToList()
        ], options.RrfK);

        HybridCursorPosition? position = request.Cursor is not null && HybridCursorCodec.TryParse(request.Cursor, out HybridCursorPosition parsed)
            ? parsed
            : null;

        List<RrfFusedHit> remaining = position is HybridCursorPosition cursor
            ? fused.Where(hit => hit.Score < cursor.Score
                || (hit.Score == cursor.Score && string.CompareOrdinal(hit.Id, cursor.Id) < 0)).ToList()
            : fused.ToList();
        List<RrfFusedHit> page = remaining.Take(request.Limit).ToList();

        string? nextCursor = remaining.Count > page.Count
            ? HybridCursorCodec.Encode(page[^1].Score, page[^1].Id)
            : null;

        return new SearchResponse
        {
            Total = fused.Count,
            Hits = page.Select(hit => hitsById[hit.Id]).ToList(),
            Highlights = page.Select(_ => (IReadOnlyList<string>)[]).ToList(),
            NextCursor = nextCursor
        };
    }

    private async Task<IReadOnlyList<SearchRunHit>> RunKeywordLegAsync(
        SearchQuery request,
        List<Query> filters,
        CancellationToken cancellationToken)
    {
        SearchRequest searchRequest = new(IndexName)
        {
            Query = new BoolQuery
            {
                Must = [new MultiMatchQuery { Query = request.Q!, Fields = SearchableTextField }],
                Filter = filters
            },
            Sort =
            [
                new SortOptions { Field = new FieldSort { Field = "_score", Order = SortOrder.Desc } },
                new SortOptions { Field = new FieldSort { Field = IdField, Order = SortOrder.Desc } }
            ],
            Size = options.KeywordLegSize,
            AllowNoIndices = true,
            IgnoreUnavailable = true
        };
        SearchResponse<SearchRunHit> response = await client.SearchAsync<SearchRunHit>(searchRequest, cancellationToken);
        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Elasticsearch keyword leg failed: {response.DebugInformation}");
        return response.Hits.Where(hit => hit.Source is not null).Select(hit => hit.Source!).ToList();
    }

    private async Task<IReadOnlyList<SearchRunHit>> RunVectorLegAsync(
        SearchQuery request,
        List<Query> filters,
        IReadOnlyList<float>? precomputedVector,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<float> queryVector = precomputedVector ?? await embeddings.Value.EmbedAsync([request.Q!], cancellationToken);
            if (queryVector.Count == 0)
            {
                logger.LogWarning("The query embedding was empty; skipping the vector leg.");
                return [];
            }

            var knn = new KnnQuery
            {
                Field = EmbeddingField,
                QueryVector = queryVector.ToArray(),
                K = options.VectorLegSize,
                NumCandidates = NumCandidates
            };
            if (filters.Count > 0)
                knn.Filter = filters;

            SearchRequest searchRequest = new(IndexName)
            {
                Query = knn,
                Size = options.VectorLegSize,
                AllowNoIndices = true,
                IgnoreUnavailable = true
            };
            SearchResponse<SearchRunHit> response = await client.SearchAsync<SearchRunHit>(searchRequest, cancellationToken);
            if (!response.IsValidResponse)
            {
                logger.LogWarning("Elasticsearch vector leg failed: {Detail}", response.DebugInformation);
                return [];
            }
            return response.Hits.Where(hit => hit.Source is not null).Select(hit => hit.Source!).ToList();
        }
        // A genuine caller/request cancellation must propagate, but HttpClient raises
        // TaskCanceledException (an OperationCanceledException) for its OWN timeout while the
        // request token is NOT cancelled — a hung TEI endpoint must degrade like any other
        // embedding failure, not fail the whole hybrid request.
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The vector leg failed; degrading to keyword-only fusion.");
            return [];
        }
    }
}
