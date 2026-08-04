using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Adapters;

public static class LedgerStatus
{
    public static IReadOnlyDictionary<string, RunStatus> Map { get; } =
        new Dictionary<string, RunStatus>(StringComparer.OrdinalIgnoreCase)
        {
            ["ok"] = RunStatus.Completed,
            ["error"] = RunStatus.Failed,
            ["timeout"] = RunStatus.Failed,
            ["stopped"] = RunStatus.Cancelled,
            ["orphaned"] = RunStatus.Unknown,
            ["needs_input"] = RunStatus.Unknown
        };

    public static RunStatus FromLedger(string? raw)
        => raw is not null && Map.TryGetValue(raw.Trim(), out RunStatus status)
            ? status
            : RunStatus.Unknown;
}
