using Api.TokenBurn.Identity.Extensions;

namespace Api.TokenBurn.Identity.Tests;

public sealed class ApiTokenBurnIdentityTests
{
    [Fact]
    public void ServiceHostExtensions_ExposeDefaultEndpoints()
    {
        Assert.NotNull(typeof(ServiceHostExtensions).GetMethod(nameof(ServiceHostExtensions.MapDefaultEndpoints)));
    }
}
