namespace Api.TokenBurn.Insights.Features.Runs;

public sealed record RunsQueryParameters(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Model,
    string? Persona,
    decimal? MinCost,
    string? Cursor,
    int? Limit,
    string? Source = null);
