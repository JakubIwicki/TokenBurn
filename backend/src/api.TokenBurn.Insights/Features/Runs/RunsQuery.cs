using MediatR;

namespace Api.TokenBurn.Insights.Features.Runs;

/// <summary>
///     Cursor-paginated run summaries from <c>telemetry.agent_runs</c>,
///     keyed on <c>(started_at, id)</c> descending — the same key as the
///     <c>/api/search</c> cursor, and covered by the Processor's existing
///     index.
/// </summary>
public sealed record RunsQuery(
    DateTimeOffset? From,
    DateTimeOffset? To,
    string? Model,
    string? Persona,
    decimal? MinCost,
    string? Cursor,
    int Limit) : IRequest<RunsResponse>;

public sealed record RunDetailQuery(Guid Id) : IRequest<RunDetailResponse?>;
