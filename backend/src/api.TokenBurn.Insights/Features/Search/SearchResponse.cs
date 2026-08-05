namespace Api.TokenBurn.Insights.Features.Search;

/// <summary>
///     <c>/api/search</c> response. <c>Highlights</c> is parallel to
///     <c>Hits</c> — one fragment list per hit — and <c>NextCursor</c> is
///     present when a further page exists.
/// </summary>
public sealed class SearchResponse
{
    public long Total { get; init; }
    public IReadOnlyList<SearchRunHit> Hits { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Highlights { get; init; } = [];
    public string? NextCursor { get; init; }
}
