using Microsoft.Extensions.Configuration;

namespace Api.TokenBurn.Insights.Features.Search;

/// <summary>
///     Tunables for hybrid search. Read from the <c>Search:Hybrid</c> config
///     section with raw <see cref="IConfiguration.GetValue{T}" /> calls, mirroring
///     the embedding options binding. <c>RrfK</c> is the reciprocal-rank-fusion
///     constant; <c>KeywordLegSize</c> and <c>VectorLegSize</c> cap how many
///     documents each retrieval leg feeds into the fusion.
/// </summary>
public sealed record HybridRetrievalOptions(int RrfK, int KeywordLegSize, int VectorLegSize)
{
    public static HybridRetrievalOptions FromConfiguration(IConfiguration configuration) => new(
        configuration.GetValue("Search:Hybrid:RrfK", 60),
        configuration.GetValue("Search:Hybrid:KeywordLegSize", 200),
        configuration.GetValue("Search:Hybrid:VectorLegSize", 50));
}
