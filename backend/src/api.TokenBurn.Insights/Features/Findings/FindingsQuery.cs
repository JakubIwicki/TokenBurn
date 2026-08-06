using MediatR;

namespace Api.TokenBurn.Insights.Features.Findings;

/// <summary>
///     Cursor-paginated finding summaries from <c>telemetry.waste_findings</c>,
///     keyed on <c>(detected_at, id)</c> descending — the same key the read
///     model's index is built on.
/// </summary>
public sealed record FindingsQuery(
    string? Kind,
    string? Severity,
    bool? Acknowledged,
    string? Cursor,
    int Limit) : IRequest<FindingsResponse>;
