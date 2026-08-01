using TokenBurn.Processor;

namespace TokenBurn.Processor.Tests;

public sealed class TokenBurnProcessorTests
{
    [Fact]
    public void ProcessorExtensions_Exist()
    {
        Assert.NotNull(typeof(ProcessorExtensions));
    }
}
