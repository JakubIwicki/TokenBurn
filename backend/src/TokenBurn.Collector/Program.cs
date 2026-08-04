using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Google.Protobuf;
using OpenTelemetry.Proto.Collector.Trace.V1;
using OpenTelemetry.Proto.Common.V1;
using OpenTelemetry.Proto.Resource.V1;
using OpenTelemetry.Proto.Trace.V1;

return await CollectorCli.RunAsync(args, CancellationToken.None);

static class CollectorCli
{
    private const string ClientId = "tokenburn-collector";
    private const string Scope = "telemetry.write";
    private const string Source = "delegate-ledger";
    private const string DefaultIngest = "http://localhost:5080";
    private const string DefaultIdentity = "http://localhost:5000";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length < 3 || !string.Equals(args[0], "backfill", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(args[1], "--source", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(args[2], Source, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: TokenBurn.Collector backfill --source delegate-ledger [--path <ledger.jsonl>] [--ingest <baseUrl>] [--identity <authorityUrl>]");
            return 2;
        }

        string? path = GetOption(args, "--path") ?? Environment.GetEnvironmentVariable("Collector__LedgerPath");
        string? ingest = GetOption(args, "--ingest") ?? Environment.GetEnvironmentVariable("Collector__Ingest") ?? DefaultIngest;
        string? identity = GetOption(args, "--identity") ?? Environment.GetEnvironmentVariable("Collector__Identity") ?? DefaultIdentity;
        string? secret = Environment.GetEnvironmentVariable("Collector__ClientSecret") ??
                         Environment.GetEnvironmentVariable("Identity__CollectorClientSecret");
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(secret))
        {
            Console.WriteLine("Missing ledger path or collector client secret configuration.");
            return 2;
        }

        List<LedgerRow> rows = [];
        int malformedRows = 0;
        foreach (string line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            try
            {
                LedgerRow? row = JsonSerializer.Deserialize<LedgerRow>(line);
                if (row is null || string.IsNullOrWhiteSpace(row.Handle) || !DateTimeOffset.TryParse(row.Ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _))
                    malformedRows++;
                else
                    rows.Add(row);
            }
            catch (JsonException)
            {
                malformedRows++;
            }
        }

        using HttpClient client = new();
        string token = await GetTokenAsync(client, identity, secret, cancellationToken);
        int posted = 0;
        int failures = malformedRows;
        IEnumerable<IGrouping<string, LedgerRow>> sessions = rows
            .Where(row => !string.IsNullOrWhiteSpace(row.SessionId))
            .GroupBy(row => row.SessionId!, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal);

        foreach (IGrouping<string, LedgerRow> session in sessions)
        {
            if (await PostSessionAsync(client, ingest, token, session.Key, session.OrderBy(row => row.Ts, StringComparer.Ordinal).ThenBy(row => row.Handle, StringComparer.Ordinal), cancellationToken))
                posted++;
            else
                failures++;
        }

        foreach (LedgerRow row in rows.Where(row => string.IsNullOrWhiteSpace(row.SessionId)).OrderBy(row => row.Ts, StringComparer.Ordinal).ThenBy(row => row.Handle, StringComparer.Ordinal))
        {
            if (await PostSessionAsync(client, ingest, token, string.Empty, [row], cancellationToken))
                posted++;
            else
                failures++;
        }

        Console.WriteLine($"Backfill complete: rows read {rows.Count + malformedRows}, sessions posted {posted}, HTTP failures {failures}.");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<string> GetTokenAsync(HttpClient client, string authority, string secret, CancellationToken cancellationToken)
    {
        using FormUrlEncodedContent form = new([
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("client_secret", secret),
            new KeyValuePair<string, string>("scope", Scope)
        ]);
        using HttpResponseMessage response = await client.PostAsync(BuildUri(authority, "/connect/token"), form, cancellationToken);
        response.EnsureSuccessStatusCode();
        using JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellationToken));
        return document.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("Identity returned an empty access token.");
    }

    private static async Task<bool> PostSessionAsync(HttpClient client, string ingest, string token, string sessionId, IEnumerable<LedgerRow> rows, CancellationToken cancellationToken)
    {
        ExportTraceServiceRequest request = new();
        ResourceSpans resourceSpans = new()
        {
            Resource = new Resource
            {
                Attributes =
                {
                    Attribute("session.id", sessionId),
                    Attribute("tokenburn.source", Source)
                }
            }
        };
        ScopeSpans scopeSpans = new();
        scopeSpans.Spans.Add(rows.Select(CreateSpan));
        resourceSpans.ScopeSpans.Add(scopeSpans);
        request.ResourceSpans.Add(resourceSpans);

        using StringContent content = new(Google.Protobuf.JsonFormatter.Default.Format(request), Encoding.UTF8, "application/json");
        using HttpRequestMessage message = new(HttpMethod.Post, BuildUri(ingest, "/v1/traces"))
        {
            Content = content
        };
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(message, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            Console.WriteLine($"POST failed: session={sessionId} rows={rows.Count()} exception={ex.GetType().Name}: {ex.Message}");
            return false;
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"POST failed: session={sessionId} rows={rows.Count()} status={(int)response.StatusCode} {response.ReasonPhrase}");
                return false;
            }

            return true;
        }
    }

    private static Span CreateSpan(LedgerRow row)
    {
        DateTimeOffset timestamp = DateTimeOffset.Parse(row.Ts, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
        long start = checked((timestamp.UtcTicks - DateTimeOffset.UnixEpoch.UtcTicks) * 100);
        long duration = checked((long)(row.DurationSeconds * 1_000_000_000d));
        Span span = new() { Name = row.Handle, StartTimeUnixNano = (ulong)start, EndTimeUnixNano = (ulong)checked(start + duration) };
        span.Attributes.Add(Attribute("tokenburn.handle", row.Handle));
        span.Attributes.Add(Attribute("tokenburn.persona", row.Persona ?? string.Empty));
        span.Attributes.Add(Attribute("gen_ai.request.model", row.Model ?? string.Empty));
        span.Attributes.Add(Attribute("tokenburn.status", row.Status ?? string.Empty));
        span.Attributes.Add(Attribute("gen_ai.usage.input_tokens", row.MissTokens));
        span.Attributes.Add(Attribute("gen_ai.usage.cache_read_tokens", row.HitTokens));
        span.Attributes.Add(Attribute("gen_ai.usage.output_tokens", row.OutputTokens));
        if (row.CostUsd is double cost)
            span.Attributes.Add(Attribute("tokenburn.cost_usd", cost));
        return span;
    }

    private static KeyValue Attribute(string key, string value) => new() { Key = key, Value = new AnyValue { StringValue = value } };
    private static KeyValue Attribute(string key, long value) => new() { Key = key, Value = new AnyValue { IntValue = value } };
    private static KeyValue Attribute(string key, double value) => new() { Key = key, Value = new AnyValue { DoubleValue = value } };

    private static string? GetOption(string[] args, string name)
    {
        int index = Array.FindIndex(args, arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static Uri BuildUri(string baseUrl, string path) => new($"{baseUrl.TrimEnd('/')}{path}");
}

sealed record LedgerRow(
    [property: System.Text.Json.Serialization.JsonPropertyName("ts")] string Ts,
    [property: System.Text.Json.Serialization.JsonPropertyName("handle")] string Handle,
    [property: System.Text.Json.Serialization.JsonPropertyName("persona")] string? Persona,
    [property: System.Text.Json.Serialization.JsonPropertyName("model")] string? Model,
    [property: System.Text.Json.Serialization.JsonPropertyName("status")] string? Status,
    [property: System.Text.Json.Serialization.JsonPropertyName("session_id")] string? SessionId,
    [property: System.Text.Json.Serialization.JsonPropertyName("cost_usd")] double? CostUsd,
    [property: System.Text.Json.Serialization.JsonPropertyName("duration_s")] double DurationSeconds,
    [property: System.Text.Json.Serialization.JsonPropertyName("hit_tokens")] long HitTokens,
    [property: System.Text.Json.Serialization.JsonPropertyName("miss_tokens")] long MissTokens,
    [property: System.Text.Json.Serialization.JsonPropertyName("output_tokens")] long OutputTokens);
