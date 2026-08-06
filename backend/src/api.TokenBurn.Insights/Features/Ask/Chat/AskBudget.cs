namespace Api.TokenBurn.Insights.Features.Ask.Chat;

/// <summary>
///     In-memory sliding-window request budget per principal (<c>sub</c>), driven by an
///     injected <see cref="TimeProvider" /> (never <c>DateTime.UtcNow</c>). Max requests per
///     hour comes from <c>Ask:Budget:MaxRequestsPerHour</c>. In-memory means the window resets
///     on restart — the durable per-principal cap is Phase 6. Memory is bounded: a principal's
///     queue never exceeds <c>maxRequestsPerHour</c>, and once a window drains the principal's
///     dictionary entry is removed. Thread-safe: concurrent asks for the same principal
///     serialize on an internal lock.
/// </summary>
public sealed class AskBudget(int maxRequestsPerHour)
{
    private static readonly TimeSpan Window = TimeSpan.FromHours(1);

    private readonly object _lock = new();
    private readonly Dictionary<string, Queue<DateTimeOffset>> _windowByPrincipal = [];

    public bool TryCharge(string sub, TimeProvider timeProvider, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        cancellationToken.ThrowIfCancellationRequested();

        DateTimeOffset now = timeProvider.GetUtcNow();
        lock (_lock)
        {
            if (!_windowByPrincipal.TryGetValue(sub, out Queue<DateTimeOffset>? requests))
            {
                requests = new Queue<DateTimeOffset>();
                _windowByPrincipal[sub] = requests;
            }

            while (requests.Count > 0 && now - requests.Peek() >= Window)
                requests.Dequeue();

            if (requests.Count == 0)
                _windowByPrincipal.Remove(sub);

            if (requests.Count >= maxRequestsPerHour)
                return false;

            requests.Enqueue(now);
            _windowByPrincipal[sub] = requests;
            return true;
        }
    }
}
