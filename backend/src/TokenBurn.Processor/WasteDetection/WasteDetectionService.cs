using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TokenBurn.Common.Primitives;
using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Persistence;
using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     Runs the pure detectors over a persisted run, persists the resulting findings via
///     <see cref="FindingsUpserter" />, and returns the drafts the detectors computed. The run's
///     price row is resolved best-effort so an unpriced run still yields findings with null
///     wasted cost instead of an error. Re-detecting the same run is idempotent: identical
///     evidence hashes identically, so the ON CONFLICT (run_id, kind, evidence_hash) dedupe
///     keeps one row per finding.
/// </summary>
public sealed class WasteDetectionService(
    TelemetryDbContext db,
    WasteDetectionOptions options,
    PricingEngine pricingEngine,
    FindingsUpserter findingsUpserter,
    TimeProvider timeProvider,
    ILogger<WasteDetectionService> logger)
{
    public async Task<IReadOnlyList<WasteFindingDraft>> DetectRunAsync(Guid runId, CancellationToken ct)
    {
        AgentRun? run = await db.AgentRuns.AsNoTracking().SingleOrDefaultAsync(r => r.Id == runId, ct);
        if (run is null)
            return [];

        // A run without an end time is mid-flight: its token totals are incomplete, and the
        // evidence hash includes them, so detection now would persist a different hash than the
        // same (write, read) pair gets once the run completes (breaking Slice C's dedupe key).
        if (run.EndedAt is null)
            return [];

        List<AgentMessage> messages = await db.AgentMessages.AsNoTracking()
            .Where(message => message.RunId == runId)
            .OrderBy(message => message.Sequence)
            .ToListAsync(ct);

        (PriceRow? price, decimal multiplier) = await ResolvePriceAsync(run, ct);

        List<WasteFindingDraft> findings =
        [
            .. ContextReplayDetector.Detect(options, run, messages, price, multiplier),
            .. LoopDetector.Detect(options, run, messages, price, multiplier),
            .. CostThresholdDetector.Detect(options, run, messages, price, multiplier)
        ];
        for (int i = 0; i < findings.Count; i++)
        {
            WasteFindingDraft draft = findings[i];
            findings[i] = draft with { EvidenceHash = EvidenceHasher.Compute(draft.Evidence) };
        }

        // Persist the drafts. A re-detect of the same run recomputes the same evidence and hash,
        // so the ON CONFLICT (run_id, kind, evidence_hash) dedupe turns it into a no-op — the
        // finding row count is stable across replays.
        WasteFinding[] persisted = findings
            .Select(draft => WasteFinding.Create(
                draft.RunId, draft.Kind, draft.Severity, draft.Evidence, draft.WastedCostUsd, timeProvider.GetUtcNow()))
            .ToArray();
        await findingsUpserter.UpsertAsync(persisted, ct);
        return findings;
    }

    private async Task<(PriceRow? Price, decimal Multiplier)> ResolvePriceAsync(AgentRun run, CancellationToken ct)
    {
        DateTimeOffset? asOf = run.StartedAt ?? run.EndedAt;
        if (asOf is null)
            return (null, 1m);

        Result<PriceRow> resolved = await pricingEngine.ResolveAsync(run.ModelSlug, run.Service, asOf.Value, ct);
        if (!resolved.IsSuccess)
        {
            logger.LogDebug("No price row for run {RunId}; wasted-cost values will be null.", run.Id);
            return (null, 1m);
        }
        return (resolved.Value, PriceMultiplier.For(asOf.Value));
    }
}
