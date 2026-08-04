using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor.Tests.Pricing;

public sealed class PriceMultiplierTests
{
    // Asia/Shanghai is UTC+8 with no DST, so each UTC instant below lands on the named Shanghai
    // wall-clock time. Production falls back to a fixed +08:00 offset when the host lacks the IANA
    // tz database, which is exact for Shanghai — so these instants are valid on every host.
    [Theory]
    [InlineData(0, 59, 1.000)]   // Shanghai 08:59 — one minute before the first window
    [InlineData(1, 0, 2.000)]    // Shanghai 09:00 — first window opens
    [InlineData(2, 30, 2.000)]   // Shanghai 10:30 — mid first window
    [InlineData(4, 0, 1.000)]    // Shanghai 12:00 — first window closes
    [InlineData(5, 59, 1.000)]   // Shanghai 13:59 — one minute before the second window
    [InlineData(6, 0, 2.000)]    // Shanghai 14:00 — second window opens
    [InlineData(10, 0, 1.000)]   // Shanghai 18:00 — second window closes
    [InlineData(14, 0, 1.000)]   // Shanghai 22:00 — deep off-peak
    public void Classifies_ShanghaiWallClockBoundaries(int utcHour, int utcMinute, double expected)
    {
        var asOf = new DateTimeOffset(2026, 1, 1, utcHour, utcMinute, 0, TimeSpan.Zero);

        decimal multiplier = PriceMultiplier.For(asOf);

        multiplier.Should().Be((decimal)expected);
    }
}
