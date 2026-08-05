using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Adapters;

/// <summary>
///     Maps delegate ledger rows (ledger.jsonl) to normalized run envelopes.
///     A session's multiple delegate-handle rows collapse into ONE run keyed
///     (session_id, agent_id="") per the session = run spec: tokens and cost are
///     summed, and the max-cost row contributes external id, persona, model and
///     status. Session identity comes from session_id, derived from the handle
///     when blank; a row with neither is skipped with a logged reason (rule 2a).
/// </summary>
public sealed class DelegateLedgerAdapter(ILogger<DelegateLedgerAdapter> logger)
{
    public IReadOnlyList<NormalizedRun> Map(string ledgerJsonl)
    {
        Dictionary<string, List<LedgerRow>> bySession = new(StringComparer.Ordinal);
        foreach (string line in ledgerJsonl.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement row = document.RootElement;
            string handle = ReadString(row, "handle") ?? "";
            if (OtlpGenAiAdapter.IsTestHandle(handle))
                continue;
            string sessionId = ReadString(row, "session_id") ?? "";
            string resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? handle : sessionId;
            // Rule 2a: never key a run under a blank identity — ('','') would collapse
            // distinct rows into one. Skip rows with no session id and no handle.
            if (string.IsNullOrWhiteSpace(resolvedSessionId))
            {
                logger.LogWarning("Skipping ledger row with blank session identity (no session_id and no handle).");
                continue;
            }

            if (!bySession.TryGetValue(resolvedSessionId, out List<LedgerRow>? group))
            {
                group = [];
                bySession.Add(resolvedSessionId, group);
            }
            group.Add(ReadRow(row, handle));
        }
        return bySession.Select(Merge).ToList();
    }

    private static LedgerRow ReadRow(JsonElement row, string handle)
    {
        DateTimeOffset startedAt = DateTimeOffset.Parse(
            row.GetProperty("ts").GetString()!,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal);
        double durationSeconds = row.TryGetProperty("duration_s", out JsonElement duration) &&
            duration.ValueKind == JsonValueKind.Number &&
            duration.TryGetDouble(out double parsedDuration)
                ? parsedDuration
                : 0;
        return new LedgerRow(
            handle,
            ReadString(row, "persona"),
            ReadString(row, "model"),
            LedgerStatus.FromLedger(ReadString(row, "status")),
            startedAt,
            startedAt.AddSeconds(durationSeconds),
            ReadLong(row, "miss_tokens"),
            ReadLong(row, "hit_tokens"),
            ReadLong(row, "output_tokens"),
            ReadDecimal(row, "cost_usd"));
    }

    private static NormalizedRun Merge(KeyValuePair<string, List<LedgerRow>> group)
    {
        // Max-cost row wins for external id, persona, model and status; a strict
        // greater-than keeps the earlier row on cost ties.
        LedgerRow maxCost = group.Value[0];
        foreach (LedgerRow row in group.Value.Skip(1))
        {
            if ((row.CostUsd ?? 0) > (maxCost.CostUsd ?? 0))
                maxCost = row;
        }

        return new NormalizedRun
        {
            SessionId = group.Key,
            AgentId = "",
            Source = "delegate-ledger",
            ExternalId = string.IsNullOrWhiteSpace(maxCost.Handle) ? null : maxCost.Handle,
            Persona = maxCost.Persona,
            ModelSlug = maxCost.ModelSlug,
            Status = maxCost.Status,
            StartedAt = group.Value.Min(row => row.StartedAt),
            EndedAt = group.Value.Max(row => row.EndedAt),
            InputTokens = group.Value.Sum(row => row.InputTokens ?? 0),
            CacheReadTokens = group.Value.Sum(row => row.CacheReadTokens ?? 0),
            CacheWriteTokens = 0,
            OutputTokens = group.Value.Sum(row => row.OutputTokens ?? 0),
            ReportedCostUsd = group.Value.Sum(row => row.CostUsd)
        };
    }

    private static string? ReadString(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;

    private static long? ReadLong(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long parsed) ? parsed : null;

    private static decimal? ReadDecimal(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) && value.TryGetDecimal(out decimal parsed) ? parsed : null;

    private sealed record LedgerRow(
        string Handle,
        string? Persona,
        string? ModelSlug,
        RunStatus Status,
        DateTimeOffset StartedAt,
        DateTimeOffset EndedAt,
        long? InputTokens,
        long? CacheReadTokens,
        long? OutputTokens,
        decimal? CostUsd);
}
