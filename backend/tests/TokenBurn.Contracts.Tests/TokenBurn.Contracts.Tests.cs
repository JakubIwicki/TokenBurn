using TokenBurn.Contracts;

namespace TokenBurn.Contracts.Tests;

public sealed class TokenBurnContractsTests
{
    [Fact]
    public void ContractsAssemblyMarker_Exists()
    {
        Assert.NotNull(typeof(ContractsAssemblyMarker));
    }
}
