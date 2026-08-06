namespace Api.TokenBurn.Insights.Features.Search;

/// <summary>
///     One fused result: the document id and its reciprocal-rank-fusion score.
/// </summary>
public sealed record RrfFusedHit(string Id, double Score);

/// <summary>
///     Reciprocal Rank Fusion (RRF): each document at 1-based rank <c>r</c> in a
///     leg contributes <c>1 / (K + r)</c> to its fused score, summed across legs.
///     Fused client-side — Elasticsearch has no rank query, and fusing here keeps
///     the two retrieval legs (keyword and vector) independently tunable. The
///     returned ordering is deterministic: score descending, then id descending
///     (ordinal) as the tie-break.
/// </summary>
public static class RrfFusion
{
    public static IReadOnlyList<RrfFusedHit> Fuse(IEnumerable<IReadOnlyList<string>> legs, int k = 60)
    {
        Dictionary<string, double> scores = new();
        foreach (IReadOnlyList<string> leg in legs)
        {
            for (int index = 0; index < leg.Count; index++)
            {
                string id = leg[index];
                scores[id] = scores.GetValueOrDefault(id) + 1.0 / (k + index + 1);
            }
        }
        return scores
            .Select(pair => new RrfFusedHit(pair.Key, pair.Value))
            .OrderByDescending(hit => hit.Score)
            .ThenByDescending(hit => hit.Id, StringComparer.Ordinal)
            .ToList();
    }
}
