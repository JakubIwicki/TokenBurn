using TokenBurn.Common.Primitives;
using TokenBurn.Processor.WasteDetection;

namespace TokenBurn.Processor.Domain;

/// <summary>
///     A persisted waste finding, keyed on (run_id, kind, evidence_hash). The evidence is the
///     serialized camelCase JSON of the detector's evidence object; the hash is SHA256 over exactly
///     that serialization, so a replay that recomputes the same evidence lands on the same row.
///     The evidence never carries message content or timestamps (privacy boundary), and the
///     upserter's ON CONFLICT leaves <c>detected_at</c> / <c>acknowledged_at</c> untouched so a
///     redelivery cannot reset the first-detection time or an acknowledgement.
/// </summary>
public sealed class WasteFinding : BaseEntity<Guid>
{
    public Guid RunId { get; private init; }
    public WasteFindingKind Kind { get; private init; }
    public WasteFindingSeverity Severity { get; private set; }
    public string Evidence { get; private set; } = null!;

    /// <summary>
    ///     SHA256 hex of the pre-jsonb camelCase serialization of the evidence object, NOT of the
    ///     stored <see cref="Evidence" /> text: Postgres re-sorts jsonb keys on write, so
    ///     <c>row.Evidence</c> is not byte-equal to what was hashed. NEVER recompute this hash
    ///     from stored evidence text — always re-derive from a fresh evidence object, or the
    ///     (run_id, kind, evidence_hash) dedupe key silently breaks.
    /// </summary>
    public string EvidenceHash { get; private init; } = null!;
    public decimal? WastedCostUsd { get; private set; }
    public DateTimeOffset DetectedAt { get; private init; }
    public DateTimeOffset? AcknowledgedAt { get; private set; }
    public int Version { get; private init; }

    private WasteFinding() { }

    public static WasteFinding Create(
        Guid runId, WasteFindingKind kind, WasteFindingSeverity severity, object evidence,
        decimal? wastedCostUsd, DateTimeOffset detectedAt)
    {
        string evidenceJson = EvidenceHasher.Serialize(evidence);
        return new WasteFinding
        {
            Id = Guid.NewGuid(),
            RunId = runId,
            Kind = kind,
            Severity = severity,
            Evidence = evidenceJson,
            EvidenceHash = EvidenceHasher.Compute(evidence),
            WastedCostUsd = wastedCostUsd,
            DetectedAt = detectedAt,
            Version = 1
        };
    }

    /// <summary>
    ///     Spec-of-record for acknowledging a finding (Phase 7). First acknowledgement wins; a
    ///     second call conflicts so a replay cannot un-acknowledge the finding.
    /// </summary>
    public Result TryAcknowledge(DateTimeOffset at)
    {
        if (AcknowledgedAt is not null)
            return Result.Conflict($"Finding {Id} is already acknowledged.");
        AcknowledgedAt = at;
        return Result.Success();
    }
}
