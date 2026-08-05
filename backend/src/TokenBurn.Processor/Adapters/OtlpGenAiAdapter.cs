using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TokenBurn.Contracts;

namespace TokenBurn.Processor.Adapters;

/// <summary>
///     Maps an OTLP/JSON payload (GenAI semantic conventions + TokenBurn custom
///     attributes) to normalized run envelopes. A delegate child is itself a
///     Claude Code session, so the same session appears as several spans; this
///     adapter collapses them into ONE run keyed (session_id, agent_id="") per
///     the session = run spec. Span identity derives from session.id, falling
///     back to the span handle when the session id is blank; a span with neither
///     is skipped with a logged reason (rule 2a — never emit a blank identity).
/// </summary>
public sealed class OtlpGenAiAdapter(ILogger<OtlpGenAiAdapter> logger)
{
    public IReadOnlyList<NormalizedRun> Map(string otlpJson)
    {
        using JsonDocument document = JsonDocument.Parse(otlpJson);
        if (!document.RootElement.TryGetProperty("resourceSpans", out JsonElement resources))
            return [];

        Dictionary<string, List<SpanData>> bySession = new(StringComparer.Ordinal);
        foreach (JsonElement resourceSpan in resources.EnumerateArray())
        {
            JsonElement resource = resourceSpan.GetProperty("resource");
            string? sessionId = ReadAttribute(resource, "session.id", AttributeKind.String);
            string source = ReadAttribute(resource, "tokenburn.source", AttributeKind.String) ?? "delegate-ledger";
            if (!string.Equals(source, "delegate-ledger", StringComparison.Ordinal))
                continue;

            if (!resourceSpan.TryGetProperty("scopeSpans", out JsonElement scopes))
                continue;
            foreach (JsonElement scope in scopes.EnumerateArray())
            {
                if (!scope.TryGetProperty("spans", out JsonElement spans))
                    continue;
                foreach (JsonElement span in spans.EnumerateArray())
                {
                    string? handle = ReadAttribute(span, "tokenburn.handle", AttributeKind.String);
                    if (IsTestHandle(handle))
                        continue;
                    string resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? handle ?? "" : sessionId;
                    // Rule 2a: never key a run under a blank identity — ('','') would collapse
                    // distinct spans into one row. Skip spans with no session id and no handle.
                    if (string.IsNullOrWhiteSpace(resolvedSessionId))
                    {
                        logger.LogWarning("Skipping OTLP span with blank session identity (no session.id and no handle).");
                        continue;
                    }

                    if (!bySession.TryGetValue(resolvedSessionId, out List<SpanData>? group))
                    {
                        group = [];
                        bySession.Add(resolvedSessionId, group);
                    }
                    group.Add(new SpanData(
                        handle ?? "",
                        ReadAttribute(span, "tokenburn.persona", AttributeKind.String),
                        ReadAttribute(span, "gen_ai.request.model", AttributeKind.String),
                        LedgerStatus.FromLedger(ReadAttribute(span, "tokenburn.status", AttributeKind.String)),
                        ReadTime(span, "startTimeUnixNano"),
                        ReadTime(span, "endTimeUnixNano"),
                        ReadLong(span, "gen_ai.usage.input_tokens") ?? 0,
                        ReadLong(span, "gen_ai.usage.cache_read_tokens") ?? 0,
                        ReadLong(span, "gen_ai.usage.cache_write_tokens") ?? 0,
                        ReadLong(span, "gen_ai.usage.output_tokens") ?? 0,
                        ReadDecimal(span, "tokenburn.cost_usd")));
                }
            }
        }
        return bySession.Select(Merge).ToList();
    }

    public static bool IsTestHandle(string? handle)
        => !string.IsNullOrWhiteSpace(handle) &&
           (string.Equals(handle, "test", StringComparison.OrdinalIgnoreCase) ||
            handle.StartsWith("test-", StringComparison.OrdinalIgnoreCase));

    private static NormalizedRun Merge(KeyValuePair<string, List<SpanData>> group)
    {
        // Max-cost span wins for external id, persona, model and status; a strict
        // greater-than keeps the earlier span on cost ties.
        SpanData maxCost = group.Value[0];
        foreach (SpanData span in group.Value.Skip(1))
        {
            if ((span.CostUsd ?? 0) > (maxCost.CostUsd ?? 0))
                maxCost = span;
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
            StartedAt = group.Value.Min(span => span.StartedAt),
            EndedAt = group.Value.Max(span => span.EndedAt),
            InputTokens = group.Value.Sum(span => span.InputTokens),
            CacheReadTokens = group.Value.Sum(span => span.CacheReadTokens),
            CacheWriteTokens = group.Value.Sum(span => span.CacheWriteTokens),
            OutputTokens = group.Value.Sum(span => span.OutputTokens),
            ReportedCostUsd = group.Value.Sum(span => span.CostUsd)
        };
    }

    private static DateTimeOffset? ReadTime(JsonElement span, string property)
    {
        if (!span.TryGetProperty(property, out JsonElement value) ||
            !long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out long nanos))
            return null;
        return DateTimeOffset.FromUnixTimeMilliseconds(nanos / 1_000_000);
    }

    private static long? ReadLong(JsonElement span, string key)
        => long.TryParse(ReadAttribute(span, key, AttributeKind.Int), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : null;

    private static decimal? ReadDecimal(JsonElement span, string key)
        => decimal.TryParse(ReadAttribute(span, key, AttributeKind.Double), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal value) ? value : null;

    private static string? ReadAttribute(JsonElement owner, string key, AttributeKind kind)
    {
        if (!owner.TryGetProperty("attributes", out JsonElement attributes))
            return null;
        foreach (JsonElement attribute in attributes.EnumerateArray())
        {
            if (!string.Equals(attribute.GetProperty("key").GetString(), key, StringComparison.Ordinal))
                continue;
            if (!attribute.TryGetProperty("value", out JsonElement value))
                return null;
            string property = kind switch
            {
                AttributeKind.String => "stringValue",
                AttributeKind.Int => "intValue",
                AttributeKind.Double => "doubleValue",
                _ => ""
            };
            return value.TryGetProperty(property, out JsonElement result) ? result.ToString() : null;
        }
        return null;
    }

    private sealed record SpanData(
        string Handle,
        string? Persona,
        string? ModelSlug,
        RunStatus Status,
        DateTimeOffset? StartedAt,
        DateTimeOffset? EndedAt,
        long InputTokens,
        long CacheReadTokens,
        long CacheWriteTokens,
        long OutputTokens,
        decimal? CostUsd);

    private enum AttributeKind { String, Int, Double }
}
