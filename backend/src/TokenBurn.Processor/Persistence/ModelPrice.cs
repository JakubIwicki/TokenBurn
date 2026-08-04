namespace TokenBurn.Processor.Persistence;

public sealed class ModelPrice
{
    public string Slug { get; private set; } = null!;
    public string Service { get; private set; } = null!;
    public decimal InputPerMtok { get; private set; }
    public decimal CacheReadPerMtok { get; private set; }
    public decimal CacheWritePerMtok { get; private set; }
    public decimal OutputPerMtok { get; private set; }
    public int? ContextWindow { get; private set; }
    public DateTimeOffset EffectiveFrom { get; private set; }
    public DateTimeOffset? EffectiveTo { get; private set; }

    private ModelPrice() { }

    public ModelPrice(
        string slug,
        string service,
        decimal inputPerMtok,
        decimal cacheReadPerMtok,
        decimal cacheWritePerMtok,
        decimal outputPerMtok,
        int? contextWindow,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveTo)
    {
        Slug = slug;
        Service = service;
        InputPerMtok = inputPerMtok;
        CacheReadPerMtok = cacheReadPerMtok;
        CacheWritePerMtok = cacheWritePerMtok;
        OutputPerMtok = outputPerMtok;
        ContextWindow = contextWindow;
        EffectiveFrom = effectiveFrom;
        EffectiveTo = effectiveTo;
    }
}
