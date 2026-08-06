namespace Api.TokenBurn.Insights.Features.Costs;

public sealed record CostsQueryParameters(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? GroupBy,
    int? Limit);
