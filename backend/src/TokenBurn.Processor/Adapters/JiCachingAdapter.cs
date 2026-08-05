using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Adapters;

/// <summary>
///     Provisional jicaching adapter. The real ~/.jicaching/state.json is
///     unusable as a cost source — lifetime accumulators with no epoch and a
///     500-entry ring buffer — so this maps the synthetic ring-buffer snapshot
///     shape documented in jicaching-sample.jsonl (a state record plus entry
///     records). It groups entries by session and applies the shared session =
///     run merge; finalized in the import-commands slice once a usable export
///     format exists.
/// </summary>
public sealed class JiCachingAdapter(ILogger<JiCachingAdapter> logger)
{
    public IReadOnlyList<NormalizedRun> Map(string jicachingJsonl)
    {
        Dictionary<string, List<Entry>> bySession = new(StringComparer.Ordinal);
        foreach (string line in jicachingJsonl.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement row = document.RootElement;
            if (!string.Equals(ReadString(row, "record_type"), "entry", StringComparison.Ordinal))
                continue;
            string handle = ReadString(row, "handle") ?? "";
            string sessionId = ReadString(row, "session_id") ?? "";
            string resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? handle : sessionId;
            // Rule 2a: never key a run under a blank identity — ('','') would
            // collapse distinct entries into one. Skip entries with no session.
            if (string.IsNullOrWhiteSpace(resolvedSessionId))
            {
                logger.LogWarning("Skipping jicaching entry with blank session identity.");
                continue;
            }

            if (!bySession.TryGetValue(resolvedSessionId, out List<Entry>? group))
            {
                group = [];
                bySession.Add(resolvedSessionId, group);
            }
            group.Add(ReadEntry(row, handle));
        }
        return bySession.Select(Merge).ToList();
    }

    private static Entry ReadEntry(JsonElement row, string handle)
    {
        DateTimeOffset timestamp = DateTimeOffset.Parse(
            row.GetProperty("ts").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
        return new Entry(
            handle,
            ReadString(row, "model"),
            timestamp,
            ReadUsage(row, "input_tokens"),
            ReadUsage(row, "cache_read_input_tokens"),
            ReadUsage(row, "cache_creation_input_tokens"),
            ReadUsage(row, "output_tokens"),
            ReadDecimal(row, "cost_usd"));
    }

    private static NormalizedRun Merge(KeyValuePair<string, List<Entry>> group)
    {
        // Max-cost entry wins for external id and model; a strict greater-than
        // keeps the earlier entry on cost ties. Entries carry no status, so the
        // merged run is Unknown by the shared ledger vocabulary.
        Entry maxCost = group.Value[0];
        foreach (Entry entry in group.Value.Skip(1))
        {
            if ((entry.CostUsd ?? 0) > (maxCost.CostUsd ?? 0))
                maxCost = entry;
        }

        return new NormalizedRun
        {
            SessionId = group.Key,
            AgentId = "",
            Source = "jicaching",
            ExternalId = string.IsNullOrWhiteSpace(maxCost.Handle) ? null : maxCost.Handle,
            ModelSlug = maxCost.ModelSlug,
            Status = RunStatus.Unknown,
            StartedAt = group.Value.Min(entry => entry.Timestamp),
            EndedAt = group.Value.Max(entry => entry.Timestamp),
            InputTokens = group.Value.Sum(entry => entry.InputTokens),
            CacheReadTokens = group.Value.Sum(entry => entry.CacheReadTokens),
            CacheWriteTokens = group.Value.Sum(entry => entry.CacheWriteTokens),
            OutputTokens = group.Value.Sum(entry => entry.OutputTokens),
            ReportedCostUsd = group.Value.Sum(entry => entry.CostUsd)
        };
    }

    private static long ReadUsage(JsonElement row, string key)
    {
        if (!row.TryGetProperty("usage", out JsonElement usage) ||
            !usage.TryGetProperty(key, out JsonElement value) || !value.TryGetInt64(out long parsed))
            return 0;
        return parsed;
    }

    private static string? ReadString(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;

    private static decimal? ReadDecimal(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) && value.TryGetDecimal(out decimal parsed) ? parsed : null;

    private sealed record Entry(
        string Handle,
        string? ModelSlug,
        DateTimeOffset Timestamp,
        long InputTokens,
        long CacheReadTokens,
        long CacheWriteTokens,
        long OutputTokens,
        decimal? CostUsd);
}
