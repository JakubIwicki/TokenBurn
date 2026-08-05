using System.Text.Json;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Adapters;

/// <summary>
///     Routes an incoming payload to the adapter for its source format. With no
///     source given, the payload is auto-detected as OTLP when it carries
///     resourceSpans; with a source, a row-adapter registry maps the source
///     string to its adapter. An unknown source yields no runs (logged), never
///     an exception — source is provenance, not a hard dependency.
/// </summary>
public sealed class SourceDispatcher(
    OtlpGenAiAdapter otlpAdapter,
    DelegateLedgerAdapter ledgerAdapter,
    DelegateRunLogAdapter runLogAdapter,
    ClaudeCodeTranscriptAdapter transcriptAdapter,
    JiCachingAdapter jiCachingAdapter,
    ILogger<SourceDispatcher> logger)
{
    private readonly IReadOnlyDictionary<string, Func<string, IReadOnlyList<NormalizedRun>>> _rowAdapters =
        new Dictionary<string, Func<string, IReadOnlyList<NormalizedRun>>>(StringComparer.Ordinal)
        {
            ["delegate-ledger"] = ledgerAdapter.Map,
            ["delegate-run-log"] = runLogAdapter.Map,
            ["claude-code-transcript"] = transcriptAdapter.Map,
            ["jicaching"] = jiCachingAdapter.Map
        };

    public IReadOnlyList<NormalizedRun> Map(string payload, string? source = null)
    {
        if (source is null)
        {
            if (IsOtlp(payload))
                return otlpAdapter.Map(payload);
            logger.LogWarning("No source given and payload is not OTLP (no top-level 'resourceSpans'); returning no runs.");
            return [];
        }
        if (_rowAdapters.TryGetValue(source, out Func<string, IReadOnlyList<NormalizedRun>>? adapter))
            return adapter(payload);
        logger.LogWarning("Unknown telemetry source '{Source}'; returning no runs.", source);
        return [];
    }

    // Structural check, not a substring scan: a payload whose string content merely
    // mentions "resourceSpans" must not be misrouted to the OTLP adapter.
    private static bool IsOtlp(string payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty("resourceSpans", out _);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
