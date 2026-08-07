using System.Text.Json;
using System.Text.Json.Serialization;

namespace TokenBurn.Contracts;

/// <summary>
///     Shared JSON (de)serialization for the topic-chain contracts. camelCase
///     field names plus string enums keep the wire format consistent with the
///     normalized-envelope pipeline and with what Elasticsearch receives.
/// </summary>
public static class KafkaJsonSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}
