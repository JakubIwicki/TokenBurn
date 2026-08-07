using Microsoft.Extensions.Configuration;
using TokenBurn.Processor.Aggregation;

namespace TokenBurn.Processor.Tests.Aggregation;

public sealed class AggregateOptionsTests
{
    [Fact]
    public void Throws_WhenEnabledWithMinSizeBelowOne()
    {
        IConfiguration configuration = ConfigurationFor(enabled: "true", minSize: "0");

        Action act = () => AggregateOptions.FromConfiguration(configuration);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Processor:Aggregate:MinSize*")
            .WithMessage("*0*");
    }

    [Fact]
    public void DoesNotThrow_WhenDisabledWithMinSizeBelowOne()
    {
        IConfiguration configuration = ConfigurationFor(enabled: "false", minSize: "0");

        AggregateOptions options = AggregateOptions.FromConfiguration(configuration);

        options.Enabled.Should().BeFalse();
        options.MinSize.Should().Be(0);
    }

    private static IConfiguration ConfigurationFor(string enabled, string minSize)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Processor:Aggregate:Enabled"] = enabled,
                ["Processor:Aggregate:MinSize"] = minSize
            })
            .Build();
}
