using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     Shared severity ladder for every detector: a finding whose wasted cost reaches the
///     critical (or major) threshold is upgraded; an unpriced finding (null wasted cost) is Minor.
/// </summary>
public static class WasteSeverity
{
    public static WasteFindingSeverity For(decimal? wastedCostUsd, WasteDetectionOptions options)
        => wastedCostUsd >= options.SeverityCriticalUsd
            ? WasteFindingSeverity.Critical
            : wastedCostUsd >= options.SeverityMajorUsd
                ? WasteFindingSeverity.Major
                : WasteFindingSeverity.Minor;
}
