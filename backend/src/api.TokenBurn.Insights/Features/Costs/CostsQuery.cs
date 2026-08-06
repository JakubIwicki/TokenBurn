using MediatR;

namespace Api.TokenBurn.Insights.Features.Costs;

public sealed record CostsQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? GroupBy,
    int Limit) : IRequest<CostSummaryResponse>;
