using TokenBurn.Processor.Domain;
using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     Flags a run whose total cost reached MaxRunCostUsd; the wasted amount is the overage
///     beyond the threshold. The extra parameters mirror the other detectors so the service can
///     drive all three uniformly.
/// </summary>
public static class CostThresholdDetector
{
    public static IReadOnlyList<WasteFindingDraft> Detect(
        WasteDetectionOptions options,
        AgentRun run,
        IReadOnlyList<AgentMessage> messages,
        PriceRow? price,
        decimal multiplier)
    {
        if (run.CostUsd is null || run.CostUsd < options.MaxRunCostUsd)
            return [];

        decimal overage = run.CostUsd.Value - options.MaxRunCostUsd;
        object evidence = new
        {
            kind = nameof(WasteFindingKind.CostThreshold),
            rule = "cost-threshold",
            runCostUsd = run.CostUsd.Value,
            maxRunCostUsd = options.MaxRunCostUsd,
            overageUsd = overage
        };
        return
        [
            new WasteFindingDraft(
                run.Id, WasteFindingKind.CostThreshold, WasteSeverity.For(overage, options),
                evidence, overage, "")
        ];
    }
}
