using System.Globalization;
using System.Text.Json;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Adapters;

public sealed class DelegateLedgerAdapter
{
    public IReadOnlyList<AgentRun> Map(string otlpJson)
    {
        using JsonDocument document = JsonDocument.Parse(otlpJson);
        List<AgentRun> runs = [];
        if (!document.RootElement.TryGetProperty("resourceSpans", out JsonElement resources))
            return runs;

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
                    string? externalId = ReadAttribute(span, "tokenburn.handle", AttributeKind.String);
                    if (IsTestHandle(externalId))
                        continue;
                    string resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? externalId ?? "" : sessionId;
                    // Rule 2a: never key a run under a blank identity — ('','') would collapse
                    // distinct spans into one row. Skip spans with no session id and no handle.
                    if (string.IsNullOrWhiteSpace(resolvedSessionId))
                        continue;

                    runs.Add(AgentRun.Create(
                        resolvedSessionId,
                        externalId ?? "",
                        source,
                        externalId,
                        ReadAttribute(span, "tokenburn.persona", AttributeKind.String),
                        ReadAttribute(span, "gen_ai.request.model", AttributeKind.String),
                        LedgerStatus.FromLedger(ReadAttribute(span, "tokenburn.status", AttributeKind.String)),
                        ReadTime(span, "startTimeUnixNano"),
                        ReadTime(span, "endTimeUnixNano"),
                        ReadLong(span, "gen_ai.usage.input_tokens"),
                        ReadLong(span, "gen_ai.usage.cache_read_tokens"),
                        ReadLong(span, "gen_ai.usage.cache_write_tokens"),
                        ReadLong(span, "gen_ai.usage.output_tokens"),
                        ReadDecimal(span, "tokenburn.cost_usd")));
                }
            }
        }
        return runs;
    }

    public static bool IsTestHandle(string? handle)
        => !string.IsNullOrWhiteSpace(handle) &&
           (string.Equals(handle, "test", StringComparison.OrdinalIgnoreCase) ||
            handle.StartsWith("test-", StringComparison.OrdinalIgnoreCase));

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

    private enum AttributeKind { String, Int, Double }
}
