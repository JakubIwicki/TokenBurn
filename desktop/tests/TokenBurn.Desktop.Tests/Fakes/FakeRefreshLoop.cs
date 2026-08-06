using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Tests.Fakes;

/// <summary>
/// Manual tick pump: <see cref="Start"/>/<see cref="Stop"/> only record state; <see cref="Pump"/>
/// fires <see cref="IRefreshLoop.Tick"/> on demand so tests drive the poll without real timers.
/// </summary>
public sealed class FakeRefreshLoop : IRefreshLoop
{
    public int StartCount { get; private set; }
    public int StopCount { get; private set; }

    public event Action? Tick;

    public void Start() => StartCount++;

    public void Stop() => StopCount++;

    public void Pump() => Tick?.Invoke();
}
