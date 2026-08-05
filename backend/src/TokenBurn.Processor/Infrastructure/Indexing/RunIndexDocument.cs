using System.Text.Json.Serialization;

namespace TokenBurn.Processor.Infrastructure.Indexing;

/// <summary>
///     The Elasticsearch run document. Field names are snake_case via
///     <see cref="JsonPropertyNameAttribute" /> to match the <c>traces</c>
///     index template (the ES client's default serializer is camelCase).
///     Status enums are pre-converted to strings by the mapper so the
///     template's keyword mapping is the only mapping these fields need.
/// </summary>
public sealed class RunIndexDocument
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("session_id")] public string SessionId { get; init; } = null!;
    [JsonPropertyName("agent_id")] public string AgentId { get; init; } = "";
    [JsonPropertyName("source")] public string Source { get; init; } = null!;
    [JsonPropertyName("external_id")] public string? ExternalId { get; init; }
    [JsonPropertyName("parent_run_id")] public Guid? ParentRunId { get; init; }
    [JsonPropertyName("workspace")] public string? Workspace { get; init; }
    [JsonPropertyName("persona")] public string? Persona { get; init; }
    [JsonPropertyName("model_slug")] public string? ModelSlug { get; init; }
    [JsonPropertyName("service")] public string? Service { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = null!;
    [JsonPropertyName("pricing_status")] public string PricingStatus { get; init; } = null!;
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; init; }
    [JsonPropertyName("ended_at")] public DateTimeOffset? EndedAt { get; init; }
    [JsonPropertyName("input_tokens")] public long? InputTokens { get; init; }
    [JsonPropertyName("cache_read_tokens")] public long? CacheReadTokens { get; init; }
    [JsonPropertyName("cache_write_tokens")] public long? CacheWriteTokens { get; init; }
    [JsonPropertyName("output_tokens")] public long? OutputTokens { get; init; }
    [JsonPropertyName("cost_usd")] public decimal? CostUsd { get; init; }
    [JsonPropertyName("reported_cost_usd")] public decimal? ReportedCostUsd { get; init; }
    [JsonPropertyName("price_multiplier")] public decimal? PriceMultiplier { get; init; }
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("searchable_text")] public string SearchableText { get; init; } = null!;
}
