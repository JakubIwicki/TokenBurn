using Elastic.Clients.Elasticsearch.QueryDsl;

namespace Api.TokenBurn.Insights.Features.Search;

/// <summary>
///     Builds the term/range filter list shared by keyword and hybrid search, so
///     both retrieval paths apply exactly the same constraints. The keyword
///     handler additionally uses <see cref="StartedAtField" /> for its null-tail
///     query.
/// </summary>
internal static class SearchFilterBuilder
{
    internal const string StartedAtField = "started_at";

    public static List<Query> BuildFilters(SearchQuery request)
    {
        var filter = new List<Query>();
        AddTermFilter(filter, "model_slug", request.Model);
        AddTermFilter(filter, "persona", request.Persona);
        AddTermFilter(filter, "source", request.Source);
        AddTermFilter(filter, "status", request.Status);
        if (request.From is not null || request.To is not null)
        {
            var range = new DateRangeQuery(StartedAtField);
            if (request.From is not null) range.Gte = request.From.Value.UtcDateTime;
            if (request.To is not null) range.Lte = request.To.Value.UtcDateTime;
            filter.Add(range);
        }
        return filter;
    }

    private static void AddTermFilter(ICollection<Query> filter, string field, string? value)
    {
        if (value is not null)
            filter.Add(new TermQuery(field, value));
    }
}
