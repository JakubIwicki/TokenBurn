namespace TokenBurn.Processor.Pricing;

public sealed record PriceRow(
    string Slug,
    string Service,
    decimal InputPerMtok,
    decimal CacheReadPerMtok,
    decimal CacheWritePerMtok,
    decimal OutputPerMtok,
    int? ContextWindow);

public enum PricingOutcome
{
    Priced,
    Quarantined,
    Unpriceable
}
