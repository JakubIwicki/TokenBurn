using Api.TokenBurn.Insights.Extensions;

namespace Api.TokenBurn.Insights.Tests;

public sealed class ApiTokenBurnInsightsTests
{
    [Fact]
    public void ServiceHostExtensions_ExposeDefaultEndpoints()
    {
        Assert.NotNull(typeof(ServiceHostExtensions).GetMethod(nameof(ServiceHostExtensions.MapDefaultEndpoints)));
    }
}
