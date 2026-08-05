using MediatR;

namespace Api.TokenBurn.Insights.Features.Search;

/// <summary>
///     Keyword search over the <c>traces</c> Elasticsearch index. <c>Mode</c> is
///     <c>keyword</c> by default; <c>hybrid</c> is rejected until Phase 5 ships
///     the embedding chain.
/// </summary>
public sealed record SearchQuery(
    string? Q,
    string? Mode,
    string? Model,
    string? Persona,
    string? Source,
    string? Status,
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Cursor,
    int Limit) : IRequest<SearchResponse>;
