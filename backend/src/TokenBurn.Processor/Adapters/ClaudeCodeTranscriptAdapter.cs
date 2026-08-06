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
///     format, so ReportedCostUsd stays null. Each row carrying a message is
///     retained as a <see cref="NormalizedMessage" /> so waste detectors can
///     inspect individual messages; the run-level token totals are the same
///     sums as before.
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
        List<NormalizedMessage> messages = [];
        int messageSequence = 0;

        foreach (string line in transcriptJsonl.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement row = document.RootElement;

            sessionId ??= ReadString(row, "sessionId");
            if (ReadString(row, "type") is { } type)
                lastType = type;
            DateTimeOffset? rowTimestamp = ReadTimestamp(row);
            if (rowTimestamp is { } timestamp)
            {
                firstTimestamp ??= timestamp;
                lastTimestamp = timestamp;
            }
            if (!row.TryGetProperty("message", out JsonElement message) || message.ValueKind != JsonValueKind.Object)
                continue;
            if (ReadString(message, "model") is { } model)
                latestModel = model;

            messageSequence++;
            messages.Add(BuildMessage(message, rowTimestamp, firstTimestamp, lastTimestamp, messageSequence));

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
                OutputTokens = outputTokens,
                Messages = messages
            }
        ];
    }

    private static NormalizedMessage BuildMessage(
        JsonElement message, DateTimeOffset? rowTimestamp, DateTimeOffset? firstTimestamp,
        DateTimeOffset? lastTimestamp, int sequence)
    {
        // A message without a usage object reads zero on every counter; falling back to the
        // message element itself keeps ReadUsage safe (TryGetProperty on a non-object would
        // throw, and the message is always an object).
        JsonElement usage = message.TryGetProperty("usage", out JsonElement value) && value.ValueKind == JsonValueKind.Object
            ? value
            : message;
        return new NormalizedMessage
        {
            Sequence = sequence,
            Role = ReadString(message, "role") ?? "",
            Content = ReadContent(message),
            ToolName = ReadToolName(message),
            ModelSlug = ReadString(message, "model"),
            InputTokens = ReadUsage(usage, "input_tokens"),
            CacheReadTokens = ReadUsage(usage, "cache_read_input_tokens"),
            CacheWriteTokens = ReadUsage(usage, "cache_creation_input_tokens"),
            OutputTokens = ReadUsage(usage, "output_tokens"),
            OccurredAt = rowTimestamp ?? firstTimestamp ?? lastTimestamp ?? DateTimeOffset.UnixEpoch
        };
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

    /// <summary>
    ///     Best-effort content extraction: the raw string content, or the joined
    ///     text blocks when the content is an array of blocks (the tool_use /
    ///     tool_result payloads are dropped — only prose is kept).
    /// </summary>
    private static string? ReadContent(JsonElement message)
    {
        if (!message.TryGetProperty("content", out JsonElement content) || content.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;
        if (content.ValueKind == JsonValueKind.String)
            return content.GetString();
        if (content.ValueKind != JsonValueKind.Array)
            return null;

        List<string> textBlocks = [];
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object &&
                block.TryGetProperty("type", out JsonElement type) &&
                type.GetString() == "text" &&
                block.TryGetProperty("text", out JsonElement text) &&
                text.ValueKind == JsonValueKind.String)
            {
                textBlocks.Add(text.GetString()!);
            }
        }
        return textBlocks.Count == 0 ? null : string.Join("\n", textBlocks);
    }

    /// <summary>
    ///     Best-effort tool name: the first <c>tool_use</c> block's name in the
    ///     content array, if present.
    /// </summary>
    private static string? ReadToolName(JsonElement message)
    {
        if (!message.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
            return null;
        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object)
                continue;
            string? type = block.TryGetProperty("type", out JsonElement typeValue) ? typeValue.GetString() : null;
            if (type == "tool_use" &&
                block.TryGetProperty("name", out JsonElement name) &&
                name.ValueKind == JsonValueKind.String)
            {
                return name.GetString();
            }
        }
        return null;
    }
}
