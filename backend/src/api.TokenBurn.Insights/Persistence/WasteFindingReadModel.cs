namespace Api.TokenBurn.Insights.Persistence;

/// <summary>
///     Read-only projection of <c>telemetry.waste_findings</c>. Column mapping
///     mirrors <c>WasteFindingConfiguration</c> exactly — any drift breaks the
///     query at runtime, which is covered by a seeded-read test. Evidence is
///     intentionally NOT projected: the summary surface carries no content
///     (privacy-boundary).
/// </summary>
public sealed class WasteFindingReadModel
{
    public Guid Id { get; set; }
    public Guid RunId { get; set; }
    public string Kind { get; set; } = null!;
    public string Severity { get; set; } = null!;
    public decimal? WastedCostUsd { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? AcknowledgedAt { get; set; }
    public int Version { get; set; }
}
