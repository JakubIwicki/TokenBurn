namespace Api.TokenBurn.Insights.Features.Search;

public sealed record SearchQueryParameters(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Q,
    string? Mode,
    string? Model,
    string? Persona,
    string? Source,
    string? Status,
    string? Cursor,
    int? Limit);
