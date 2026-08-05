using System.Text.Json;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Adapters;

/// <summary>
///     Maps one delegate run log (logs/*.jsonl) to a single normalized run
///     envelope. The meta line carries the handle (the filename stem) and start
///     time; the result line carries the terminal status, session and usage.
///     The run log has no run timestamp, so EndedAt stays null. A log missing
///     its meta or result line, or with a blank session, is skipped with a
///     logged reason (rule 2a).
/// </summary>
public sealed class DelegateRunLogAdapter(ILogger<DelegateRunLogAdapter> logger)
{
    public IReadOnlyList<NormalizedRun> Map(string runLogJsonl)
    {
        JsonElement meta = default;
        JsonElement result = default;
        bool hasMeta = false;
        bool hasResult = false;
        foreach (string line in runLogJsonl.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement row = document.RootElement;
            string type = ReadString(row, "type") ?? "";
            if (type == "meta" && !hasMeta)
            {
                meta = row.Clone();
                hasMeta = true;
            }
            else if (type == "result" && !hasResult)
            {
                result = row.Clone();
                hasResult = true;
            }
        }

        if (!hasMeta || !hasResult)
        {
            logger.LogWarning("Skipping delegate run log with missing meta or result line.");
            return [];
        }

        string sessionId = ReadString(result, "session_id") ?? "";
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            logger.LogWarning("Skipping delegate run log with blank session identity.");
            return [];
        }

        string handle = ReadString(meta, "handle") ?? "";
        bool isError = result.TryGetProperty("is_error", out JsonElement error)
                       && error.ValueKind == JsonValueKind.True;
        string? stopReason = ReadString(result, "stop_reason");

        return
        [
            new NormalizedRun
            {
                SessionId = sessionId,
                AgentId = "",
                Source = "delegate-run-log",
                ExternalId = string.IsNullOrWhiteSpace(handle) ? null : handle,
                Persona = ReadString(meta, "persona"),
                Status = isError
                    ? RunStatus.Failed
                    : string.IsNullOrWhiteSpace(stopReason) ? RunStatus.Running : RunStatus.Completed,
                StartedAt = ReadStartedAt(meta),
                EndedAt = null,
                InputTokens = ReadUsage(result, "input_tokens"),
                CacheReadTokens = ReadUsage(result, "cache_read_input_tokens"),
                CacheWriteTokens = ReadUsage(result, "cache_creation_input_tokens"),
                OutputTokens = ReadUsage(result, "output_tokens")
            }
        ];
    }

    private static DateTimeOffset? ReadStartedAt(JsonElement meta)
        => meta.TryGetProperty("started", out JsonElement started) && started.TryGetDouble(out double seconds)
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds * 1000))
            : null;

    private static long ReadUsage(JsonElement result, string key)
    {
        if (!result.TryGetProperty("usage", out JsonElement usage) ||
            !usage.TryGetProperty(key, out JsonElement value) || !value.TryGetInt64(out long parsed))
            return 0;
        return parsed;
    }

    private static string? ReadString(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
}
