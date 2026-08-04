using System.Globalization;
using System.Text.Json;
using TokenBurn.Processor.Adapters;
using TokenBurn.Processor.Domain;

namespace TokenBurn.Processor.Tests.Fixtures;

public static class LedgerCorpusReader
{
    public static IReadOnlyList<AgentRun> Read(string jsonlPath)
    {
        List<AgentRun> runs = [];
        foreach (string line in File.ReadLines(jsonlPath))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement row = document.RootElement;
            DateTimeOffset startedAt = DateTimeOffset.Parse(
                row.GetProperty("ts").GetString()!,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal);
            runs.Add(AgentRun.Create(
                row.GetProperty("session_id").GetString() ?? "",
                row.GetProperty("handle").GetString() ?? "",
                "delegate-ledger",
                row.GetProperty("handle").GetString() ?? "",
                row.GetProperty("persona").GetString(),
                row.GetProperty("model").GetString(),
                LedgerStatus.FromLedger(row.GetProperty("status").GetString()),
                startedAt,
                startedAt.AddSeconds(row.GetProperty("duration_s").GetDouble()),
                ReadLong(row, "miss_tokens"),
                ReadLong(row, "hit_tokens"),
                null,
                ReadLong(row, "output_tokens"),
                ReadDecimal(row, "cost_usd")));
        }
        return runs;
    }

    private static long? ReadLong(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) && value.TryGetInt64(out long parsed) ? parsed : null;

    private static decimal? ReadDecimal(JsonElement row, string property)
        => row.TryGetProperty(property, out JsonElement value) && value.TryGetDecimal(out decimal parsed) ? parsed : null;
}
