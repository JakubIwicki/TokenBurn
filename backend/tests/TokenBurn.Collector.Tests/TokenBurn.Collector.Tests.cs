using System.Reflection;

namespace TokenBurn.Collector.Tests;

public sealed class TokenBurnCollectorTests
{
    [Fact]
    public void CollectorAssembly_Loads()
    {
        Assert.NotNull(Assembly.Load("TokenBurn.Collector"));
    }
}
