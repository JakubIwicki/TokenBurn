using Api.TokenBurn.Ingest.Extensions;

namespace Api.TokenBurn.Ingest.Tests;

public sealed class ApiTokenBurnIngestTests
{
    [Fact]
    public void ServiceHostExtensions_ExposeDefaultEndpoints()
    {
        Assert.NotNull(typeof(ServiceHostExtensions).GetMethod(nameof(ServiceHostExtensions.MapDefaultEndpoints)));
    }
}
