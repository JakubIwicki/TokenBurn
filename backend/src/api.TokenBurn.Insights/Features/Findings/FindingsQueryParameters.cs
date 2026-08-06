namespace Api.TokenBurn.Insights.Features.Findings;

public sealed record FindingsQueryParameters(
    string? Kind,
    string? Severity,
    bool? Acknowledged,
    string? Cursor,
    int? Limit);
