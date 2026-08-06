using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Tests.Services;

public sealed class PeriodicTimerRefreshLoopTests
{
    private static async Task EventuallyAsync(Func<bool> condition)
    {
        var deadline = Environment.TickCount64 + 2000;
        while (!condition() && Environment.TickCount64 < deadline)
            await Task.Delay(10);
    }

    [Fact]
    public async Task Start_AfterIntervalPumped_TickFires()
    {
        var clock = new FakeTimeProvider();
        var interval = TimeSpan.FromSeconds(1);
        using var sut = new PeriodicTimerRefreshLoop(interval, clock);
        var ticks = 0;
        sut.Tick += () => Interlocked.Increment(ref ticks);

        sut.Start();
        clock.Advance(interval);

        await EventuallyAsync(() => Volatile.Read(ref ticks) == 1);
        ticks.Should().Be(1);
    }

    [Fact]
    public async Task Start_WithoutInterval_TickDoesNotFire()
    {
        var clock = new FakeTimeProvider();
        using var sut = new PeriodicTimerRefreshLoop(TimeSpan.FromSeconds(1), clock);
        var ticks = 0;
        sut.Tick += () => Interlocked.Increment(ref ticks);

        sut.Start();
        clock.Advance(TimeSpan.FromMilliseconds(500));

        await Task.Delay(100);
        ticks.Should().Be(0);
    }

    [Fact]
    public async Task Stop_NoMoreTicks()
    {
        var clock = new FakeTimeProvider();
        using var sut = new PeriodicTimerRefreshLoop(TimeSpan.FromSeconds(1), clock);
        var ticks = 0;
        sut.Tick += () => Interlocked.Increment(ref ticks);

        sut.Start();
        clock.Advance(TimeSpan.FromSeconds(1));
        await EventuallyAsync(() => Volatile.Read(ref ticks) == 1);

        sut.Stop();
        clock.Advance(TimeSpan.FromSeconds(5));

        await Task.Delay(100);
        ticks.Should().Be(1);
    }

    [Fact]
    public async Task StartTwice_StopOnce_LoopStillTicks()
    {
        var clock = new FakeTimeProvider();
        using var sut = new PeriodicTimerRefreshLoop(TimeSpan.FromSeconds(1), clock);
        var ticks = 0;
        sut.Tick += () => Interlocked.Increment(ref ticks);

        sut.Start();
        sut.Start();
        clock.Advance(TimeSpan.FromSeconds(1));
        await EventuallyAsync(() => Volatile.Read(ref ticks) == 1);

        // One subscriber released its reference; the other keeps the loop alive.
        sut.Stop();
        clock.Advance(TimeSpan.FromSeconds(2));
        await EventuallyAsync(() => Volatile.Read(ref ticks) == 3);
        await Task.Delay(100);

        ticks.Should().Be(3);
    }

    [Fact]
    public async Task StartTwice_StopTwice_LoopDies()
    {
        var clock = new FakeTimeProvider();
        using var sut = new PeriodicTimerRefreshLoop(TimeSpan.FromSeconds(1), clock);
        var ticks = 0;
        sut.Tick += () => Interlocked.Increment(ref ticks);

        sut.Start();
        sut.Start();
        clock.Advance(TimeSpan.FromSeconds(1));
        await EventuallyAsync(() => Volatile.Read(ref ticks) == 1);

        sut.Stop();
        sut.Stop();
        clock.Advance(TimeSpan.FromSeconds(5));

        await Task.Delay(100);
        ticks.Should().Be(1);
    }

    [Fact]
    public async Task ThrowingTickHandler_DoesNotKillTheLoop()
    {
        var clock = new FakeTimeProvider();
        using var sut = new PeriodicTimerRefreshLoop(TimeSpan.FromSeconds(1), clock);
        var ticks = 0;
        sut.Tick += () => throw new InvalidOperationException("boom");
        sut.Tick += () => Interlocked.Increment(ref ticks);

        sut.Start();
        clock.Advance(TimeSpan.FromSeconds(1));
        await EventuallyAsync(() => Volatile.Read(ref ticks) == 1);
        clock.Advance(TimeSpan.FromSeconds(1));
        await EventuallyAsync(() => Volatile.Read(ref ticks) == 2);

        ticks.Should().Be(2);
    }

    [Fact]
    public void Dispose_IsSafeWhenNeverStarted() =>
        new PeriodicTimerRefreshLoop(TimeSpan.FromSeconds(1), TimeProvider.System).Dispose();
}
