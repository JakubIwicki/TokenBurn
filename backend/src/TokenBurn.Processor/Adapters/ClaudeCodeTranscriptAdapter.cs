using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Adapters;

/// <summary>
///     Maps one Claude Code transcript (transcript-*.jsonl) to a single
///     normalized run envelope: StartedAt from the first event timestamp,
///     EndedAt from the last, tokens summed across every message usage, and the
///     model taken from the latest event that carries one. Status comes from the
///     terminal event type. Session identity is the sessionId the events
///     themselves carry; a transcript whose events are all session-less is
///     skipped with a logged reason (rule 2a). Reported cost is not part of the
///     format, so ReportedCostUsd stays null.
/// </summary>
public sealed class ClaudeCodeTranscriptAdapter(ILogger<ClaudeCodeTranscriptAdapter> logger)
{
    public IReadOnlyList<NormalizedRun> Map(string transcriptJsonl)
    {
        string? sessionId = null;
        DateTimeOffset? firstTimestamp = null;
        DateTimeOffset? lastTimestamp = null;
        string? latestModel = null;
        string? lastType = null;
        long inputTokens = 0;
        long cacheReadTokens = 0;
        long cacheWriteTokens = 0;
        long outputTokens = 0;

        foreach (string line in transcriptJsonl.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement row = document.RootElement;

            sessionId ??= ReadString(row, "sessionId");
            if (ReadString(row, "type") is { } type)
                lastType = type;
            if (ReadTimestamp(row) is { } timestamp)
            {
                firstTimestamp ??= timestamp;
                lastTimestamp = timestamp;
            }
            if (!row.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object)
                continue;
            if (ReadString(message, "model") is { } model)
                latestModel = model;
            if (!message.TryGetProperty("usage", out JsonElement usage) || usage.ValueKind != JsonValueKind.Object)
                continue;
            inputTokens += ReadUsage(usage, "input_tokens");
            cacheReadTokens += ReadUsage(usage, "cache_read_input_tokens");
            cacheWriteTokens += ReadUsage(usage, "cache_creation_input_tokens");
            outputTokens += ReadUsage(usage, "output_tokens");
        }

        if (string.IsNullOrWhiteSpace(sessionId))
        {
            logger.LogWarning("Skipping transcript with blank session identity (no sessionId on any event).");
            return [];
        }

        return
        [
            new NormalizedRun
            {
                SessionId = sessionId,
                AgentId = "",
                Source = "claude-code-transcript",
                ModelSlug = latestModel,
                Status = MapStatus(lastType),
                StartedAt = firstTimestamp,
                EndedAt = lastTimestamp,
                InputTokens = inputTokens,
                CacheReadTokens = cacheReadTokens,
                CacheWriteTokens = cacheWriteTokens,
                OutputTokens = outputTokens
            }
        ];
    }

    private static RunStatus MapStatus(string? terminalType) => terminalType switch
    {
        "error" or "abort" => RunStatus.Failed,
        "result" or "summary" => RunStatus.Completed,
        _ => RunStatus.Running
    };

    private static DateTimeOffset? ReadTimestamp(JsonElement row)
        => row.TryGetProperty("timestamp", out JsonElement value) &&
           DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed)
            ? parsed
            : null;

    private static long ReadUsage(JsonElement usage, string key)
        => usage.TryGetProperty(key, out JsonElement value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetInt64(out long parsed)
            ? parsed
            : 0;

    private static string? ReadString(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) ? value.GetString() : null;
}
