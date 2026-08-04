using Microsoft.Extensions.Logging;

namespace TokenBurn.Testing.Common.Mocking;

public sealed class MockLogger<T> : MockObject<ILogger<T>>
{
    private MockLogger() { }

    public static MockLogger<T> GetSuccessful() => new();

    public MockLogger<T> WithError()
    {
        Mock.Setup(x => x.IsEnabled(LogLevel.Error)).Returns(true);
        return this;
    }
}
