namespace TokenBurn.Processor.Pricing;

public static class PriceMultiplier
{
    private static readonly TimeZoneInfo AsiaShanghai = ResolveShanghaiTimeZone();

    public static decimal For(DateTimeOffset asOf)
    {
        DateTimeOffset local = TimeZoneInfo.ConvertTime(asOf, AsiaShanghai);
        decimal hourOfDay = local.Hour + local.Minute / 60m + local.Second / 3600m;
        return (hourOfDay is (>= 9.0m and < 12.0m) or (>= 14.0m and < 18.0m)) ? 2.000m : 1.000m;
    }

    // Hosts without the IANA timezone database (minimal containers, some CI images) throw on
    // FindSystemTimeZoneById; Asia/Shanghai has no DST, so a fixed +08:00 offset is exact.
    private static TimeZoneInfo ResolveShanghaiTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Shanghai");
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone(
                "Asia/Shanghai", TimeSpan.FromHours(8), "Asia/Shanghai", "Asia/Shanghai");
        }
    }
}
