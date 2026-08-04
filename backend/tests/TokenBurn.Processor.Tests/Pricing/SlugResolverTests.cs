using TokenBurn.Processor.Pricing;

namespace TokenBurn.Processor.Tests.Pricing;

public sealed class SlugResolverTests
{
    [Theory]
    [InlineData("deepseek-v4-pro[1m]", "deepseek-v4-pro")]
    [InlineData("deepseek-v4-flash", "deepseek-v4-flash")]
    [InlineData("luna", "luna")]
    [InlineData("openai/gpt-5.6-luna", "openai/gpt-5.6-luna")]
    [InlineData("openai/gpt-5.6-luna[2m]", "openai/gpt-5.6-luna[2m]")]
    public void StripsOnlyTheTrailingOneMinuteSuffix(string slug, string expected)
    {
        string resolved = SlugResolver.Resolve(slug);

        resolved.Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ReturnsEmpty_WhenNoSlug(string? slug)
    {
        string resolved = SlugResolver.Resolve(slug);

        resolved.Should().Be("");
    }
}
