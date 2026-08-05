using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.Core.Search;
using Elastic.Clients.Elasticsearch.QueryDsl;
using MediatR;
using TokenBurn.Common.Pagination;

namespace Api.TokenBurn.Insights.Features.Search;

public sealed class SearchHandler(ElasticsearchClient client) : IRequestHandler<SearchQuery, SearchResponse>
{
    private const string IndexName = "traces";
    private const string SearchableTextField = "searchable_text";

    public Task<SearchResponse> Handle(SearchQuery request, CancellationToken cancellationToken)
        => HandleAsync(request, cancellationToken);

    public async Task<SearchResponse> HandleAsync(SearchQuery request, CancellationToken cancellationToken)
    {
        var must = new List<Query>
        {
            new MultiMatchQuery { Query = request.Q!, Fields = SearchableTextField }
        };
        var filter = new List<Query>();
        AddTermFilter(filter, "model_slug", request.Model);
        AddTermFilter(filter, "persona", request.Persona);
        AddTermFilter(filter, "source", request.Source);
        AddTermFilter(filter, "status", request.Status);
        if (request.From is not null || request.To is not null)
        {
            var range = new DateRangeQuery(SearchableRangeField);
            if (request.From is not null) range.Gte = request.From.Value.UtcDateTime;
            if (request.To is not null) range.Lte = request.To.Value.UtcDateTime;
            filter.Add(range);
        }

        var requestModel = new SearchRequest(IndexName)
        {
            Query = new BoolQuery { Must = must, Filter = filter },
            Sort = new[]
            {
                new SortOptions { Field = new FieldSort { Field = "started_at", Order = SortOrder.Desc, Missing = "_last" } },
                new SortOptions { Field = new FieldSort { Field = "id", Order = SortOrder.Desc } }
            },
            Size = request.Limit + 1,
            AllowNoIndices = true,
            IgnoreUnavailable = true,
            Highlight = new Highlight
            {
                PreTags = ["<em>"],
                PostTags = ["</em>"],
                Fields = new[]
                {
                    new KeyValuePair<Field, HighlightField>(
                        new Field(SearchableTextField),
                        new HighlightField { NumberOfFragments = 2, FragmentSize = 150 })
                }
            }
        };

        // The cursor encodes the sort key of the last returned hit; the next
        // page continues strictly after (started_at, id). ES rejects a NULL
        // first search_after value, so once the boundary is a null-started run
        // the query narrows to that tail and pages by id alone.
        DateTimeOffset? startedAt = null;
        Guid id = Guid.Empty;
        bool cursorDecoded = request.Cursor is not null && CursorCodec.TryDecode(request.Cursor, out startedAt, out id);
        bool nullTail = cursorDecoded && startedAt is null;
        if (nullTail)
        {
            requestModel.Query = new BoolQuery
            {
                Must = must,
                Filter = filter.Append(new BoolQuery { MustNot = [new ExistsQuery(new Field(SearchableRangeField))] }).ToList()
            };
            requestModel.Sort = [new SortOptions { Field = new FieldSort { Field = "id", Order = SortOrder.Desc } }];
            requestModel.SearchAfter = [FieldValue.String(id.ToString("D"))];
        }
        else if (cursorDecoded)
        {
            requestModel.SearchAfter =
            [
                FieldValue.String(startedAt!.Value.ToString("O")),
                FieldValue.String(id.ToString("D"))
            ];
        }

        SearchResponse<SearchRunHit> response = await client.SearchAsync<SearchRunHit>(requestModel, cancellationToken);
        if (!response.IsValidResponse)
            throw new InvalidOperationException($"Elasticsearch search failed: {response.DebugInformation}");

        var hits = response.Hits.Where(hit => hit.Source is not null).Take(request.Limit).ToList();
        var highlights = hits
            .Select(hit => hit.Highlight is not null && hit.Highlight.TryGetValue(SearchableTextField, out var fragments)
                ? fragments.ToList()
                : new List<string>())
            .ToList();

        long total = response.Total;
        if (nullTail)
        {
            // The tail query is narrowed to null-started docs, so recount the
            // full query to keep Total stable across pages.
            CountRequest countRequest = new(IndexName)
            {
                Query = new BoolQuery { Must = must, Filter = filter },
                AllowNoIndices = true,
                IgnoreUnavailable = true
            };
            CountResponse count = await client.CountAsync(countRequest, cancellationToken);
            if (!count.IsValidResponse)
                throw new InvalidOperationException($"Elasticsearch count failed: {count.DebugInformation}");
            total = count.Count;
        }

        string? nextCursor = response.Hits.Count > request.Limit
            ? CursorCodec.Encode(hits[^1].Source!.StartedAt, hits[^1].Source!.Id)
            : null;

        return new SearchResponse
        {
            Total = total,
            Hits = hits.Select(hit => hit.Source!).ToList(),
            Highlights = highlights,
            NextCursor = nextCursor
        };
    }

    private const string SearchableRangeField = "started_at";

    private static void AddTermFilter(ICollection<Query> filter, string field, string? value)
    {
        if (value is not null)
            filter.Add(new TermQuery(field, value));
    }
}
