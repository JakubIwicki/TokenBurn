using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     A detected waste finding before persistence. <see cref="WasteDetectionService" /> computes
///     the <see cref="EvidenceHash" /> over the serialized evidence; Slice C maps this onto the
///     <c>WasteFinding</c> aggregate and keys the finding row on (run_id, kind, evidence_hash).
///     <see cref="Evidence" /> never carries message content (privacy boundary) and never carries a
///     timestamp, so an identical replay hashes equal.
/// </summary>
public sealed record WasteFindingDraft(
    Guid RunId,
    WasteFindingKind Kind,
    WasteFindingSeverity Severity,
    object Evidence,
    decimal? WastedCostUsd,
    string EvidenceHash);
