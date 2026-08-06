namespace TokenBurn.Desktop.Core.Services;

/// <summary>
/// <see cref="PeriodicTimer"/>-backed poll loop. <see cref="Tick"/> fires once per interval while
/// started; <see cref="Stop"/> cancels and completes the loop. Subscriber exceptions are swallowed
/// per-subscriber so one broken handler cannot kill a UI poller. Time is injected (FakeTimeProvider
/// in tests).
/// </summary>
public sealed class PeriodicTimerRefreshLoop : IRefreshLoop, IDisposable
{
    private readonly TimeSpan _interval;
    private readonly TimeProvider _timeProvider;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private int _refCount;

    public PeriodicTimerRefreshLoop(TimeSpan interval, TimeProvider timeProvider)
    {
        _interval = interval;
        _timeProvider = timeProvider;
    }

    public event Action? Tick;

    public void Start()
    {
        lock (_gate)
        {
            _refCount++;
            if (_cts is not null)
                return;
            _cts = new CancellationTokenSource();
            var cts = _cts;
            // The timer is created synchronously so a FakeTimeProvider.Advance immediately after
            // Start() can never race the background loop's lazy construction.
            var timer = new PeriodicTimer(_interval, _timeProvider);
            _ = Task.Run(() => RunAsync(cts, timer));
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts = null;
        lock (_gate)
        {
            if (_refCount > 0)
                _refCount--;
            if (_refCount == 0)
            {
                cts = _cts;
                _cts = null;
            }
        }
        cts?.Cancel();
    }

    public void Dispose()
    {
        CancellationTokenSource? cts;
        lock (_gate)
        {
            _refCount = 0;
            cts = _cts;
            _cts = null;
        }
        cts?.Cancel();
    }

    private async Task RunAsync(CancellationTokenSource cts, PeriodicTimer timer)
    {
        using (timer)
        {
            try
            {
                while (await timer.WaitForNextTickAsync(cts.Token).ConfigureAwait(false))
                    RaiseTick();
            }
            catch (OperationCanceledException)
            {
                // loop stopped
            }
        }
    }

    private void RaiseTick()
    {
        if (Tick is null)
            return;
        foreach (var handler in Tick.GetInvocationList())
        {
            try
            {
                ((Action)handler)();
            }
            catch
            {
                // a UI poller must never die
            }
        }
    }
}
