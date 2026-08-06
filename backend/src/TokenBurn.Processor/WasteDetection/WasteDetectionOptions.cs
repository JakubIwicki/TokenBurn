using Microsoft.Extensions.Configuration;

namespace TokenBurn.Processor.WasteDetection;

/// <summary>
///     Tunables for the waste-detection pipeline. Read from the <c>WasteDetection:</c> config
///     section with raw <see cref="IConfiguration.GetValue{T}" /> calls (no IOptions), mirroring
///     <c>ImportCommandWorker.WorkerSettings</c>.
/// </summary>
public sealed record WasteDetectionOptions(
    long ContextReplayMinReadTokens,
    long CacheCollapseMinWriteTokens,
    int CacheCollapseWindowMessages,
    long LoopMinInputTokens,
    int LoopWindowMessages,
    decimal LoopTokenTolerance,
    decimal MaxRunCostUsd,
    decimal SeverityMajorUsd,
    decimal SeverityCriticalUsd)
{
    public static WasteDetectionOptions FromConfiguration(IConfiguration configuration) => new(
        configuration.GetValue("WasteDetection:ContextReplayMinReadTokens", 500_000L),
        configuration.GetValue("WasteDetection:CacheCollapseMinWriteTokens", 100_000L),
        configuration.GetValue("WasteDetection:CacheCollapseWindowMessages", 5),
        configuration.GetValue("WasteDetection:LoopMinInputTokens", 10_000L),
        configuration.GetValue("WasteDetection:LoopWindowMessages", 5),
        configuration.GetValue("WasteDetection:LoopTokenTolerance", 0.05m),
        configuration.GetValue("WasteDetection:MaxRunCostUsd", 1.00m),
        configuration.GetValue("WasteDetection:SeverityMajorUsd", 0.01m),
        configuration.GetValue("WasteDetection:SeverityCriticalUsd", 0.10m));
}
