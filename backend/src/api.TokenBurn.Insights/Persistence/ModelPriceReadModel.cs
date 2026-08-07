namespace Api.TokenBurn.Insights.Persistence;

/// <summary>
///     Read-only projection of <c>telemetry.model_prices</c>. Column mapping
///     mirrors <c>ModelPriceConfiguration</c> exactly — any drift breaks the
///     query at runtime, which is covered by a seeded-read test. Explicit
///     allow-list columns only (privacy-boundary rule 8): the registry also
///     holds credential env-var names, upstream hostnames and internal ports,
///     none of which are projected.
/// </summary>
public sealed class ModelPriceReadModel
{
    public string Slug { get; set; } = null!;
    public string Service { get; set; } = null!;
    public int? ContextWindow { get; set; }
    public decimal InputPerMtok { get; set; }
    public decimal CacheReadPerMtok { get; set; }
    public decimal CacheWritePerMtok { get; set; }
    public decimal OutputPerMtok { get; set; }
    public DateTimeOffset EffectiveFrom { get; set; }
    public DateTimeOffset? EffectiveTo { get; set; }
}
