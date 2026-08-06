using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Tests.Fakes;

/// <summary>Synchronous dispatcher — no UI thread needed in headless tests.</summary>
public sealed class FakeDispatcher : IDispatcher
{
    public void Invoke(Action action) => action();

    public Task InvokeAsync(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}
