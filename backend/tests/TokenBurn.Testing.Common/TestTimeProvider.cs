namespace TokenBurn.Testing.Common;

public static class TestTimeProvider
{
    public static TimeProvider Instance { get; } = new FixedTimeProvider();

    private sealed class FixedTimeProvider : TimeProvider
    {
        private static readonly DateTimeOffset CurrentTime =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => CurrentTime;
    }
}
