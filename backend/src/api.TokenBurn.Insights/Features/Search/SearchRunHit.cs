using System.Text.Json.Serialization;

namespace Api.TokenBurn.Insights.Features.Search;

/// <summary>
///     A run document as stored in the <c>traces</c> index. The ES client's
///     default serializer is camelCase, so each property is pinned to its
///     snake_case <c>_source</c> field with <see cref="JsonPropertyNameAttribute" />
///     for both serialize and deserialize. Deliberately a per-feature projection
///     (the Processor owns the canonical document; Insights owns the read side).
/// </summary>
public sealed class SearchRunHit
{
    [JsonPropertyName("id")] public Guid Id { get; init; }
    [JsonPropertyName("session_id")] public string SessionId { get; init; } = null!;
    [JsonPropertyName("source")] public string Source { get; init; } = null!;
    [JsonPropertyName("external_id")] public string? ExternalId { get; init; }
    [JsonPropertyName("workspace")] public string? Workspace { get; init; }
    [JsonPropertyName("persona")] public string? Persona { get; init; }
    [JsonPropertyName("model_slug")] public string? ModelSlug { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = null!;
    [JsonPropertyName("pricing_status")] public string PricingStatus { get; init; } = null!;
    [JsonPropertyName("started_at")] public DateTimeOffset? StartedAt { get; init; }
    [JsonPropertyName("ended_at")] public DateTimeOffset? EndedAt { get; init; }
    [JsonPropertyName("input_tokens")] public long? InputTokens { get; init; }
    [JsonPropertyName("output_tokens")] public long? OutputTokens { get; init; }
    [JsonPropertyName("cost_usd")] public decimal? CostUsd { get; init; }
    [JsonPropertyName("reported_cost_usd")] public decimal? ReportedCostUsd { get; init; }
}
