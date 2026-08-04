using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor.Tests.Pricing;

public sealed class CostCalculatorTests
{
    private static readonly PriceRow DeepseekV4Flash = new("deepseek-v4-flash", "deepseek", 0.14m, 0.0028m, 0m, 0.28m, 1_048_576);

    [Fact]
    public void Computes_AllFourTokenClasses_Exactly()
    {
        decimal cost = CostCalculator.Compute(1_000_000, 500_000, 100_000, 200_000, DeepseekV4Flash, 2.0m);

        cost.Should().Be(0.3948m);
    }

    [Fact]
    public void Treats_NullTokenCounts_AsZero()
    {
        decimal cost = CostCalculator.Compute(null, null, null, null, DeepseekV4Flash, 1.0m);

        cost.Should().Be(0m);
    }

    [Fact]
    public void Treats_MissingClasses_AsZero_WhenOthersContribute()
    {
        decimal cost = CostCalculator.Compute(null, 1_000_000, null, null, DeepseekV4Flash, 1.0m);

        cost.Should().Be(0.0028m);
    }

    [Theory]
    [InlineData(1.0, 0.14)]
    [InlineData(2.0, 0.28)]
    public void Scales_ByMultiplier(double multiplier, double expected)
    {
        decimal cost = CostCalculator.Compute(1_000_000, 0, 0, 0, DeepseekV4Flash, (decimal)multiplier);

        cost.Should().Be((decimal)expected);
    }

    [Fact]
    public void Retains_SubCentPrecision()
    {
        decimal cost = CostCalculator.Compute(0, 1, 0, 0, DeepseekV4Flash, 1.0m);

        cost.Should().Be(0.0000000028m);
    }
}
