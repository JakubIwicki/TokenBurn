namespace TokenBurn.Processor.Pricing;

public static class CostCalculator
{
    public static decimal Compute(
        long? input,
        long? cacheRead,
        long? cacheWrite,
        long? output,
        PriceRow price,
        decimal multiplier)
        => ((input ?? 0) * price.InputPerMtok
            + (cacheRead ?? 0) * price.CacheReadPerMtok
            + (cacheWrite ?? 0) * price.CacheWritePerMtok
            + (output ?? 0) * price.OutputPerMtok)
            / 1_000_000m * multiplier;
}
