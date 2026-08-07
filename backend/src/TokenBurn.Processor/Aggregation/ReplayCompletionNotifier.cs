namespace TokenBurn.Processor.Aggregation;

/// <summary>
///     Bridges replay completion → aggregate rebuild. The aggregate trigger awaits
///     <see cref="Completion" /> when replay is enabled; <see cref="RunReplayTrigger" /> calls
///     <see cref="Complete" /> after a successful replay. A late await of an already-completed task
///     returns immediately, so registration order between the two hosted services is irrelevant;
///     <see cref="Complete" /> is idempotent (a second call is a no-op).
/// </summary>
public sealed class ReplayCompletionNotifier
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Completion => _completion.Task;

    public void Complete() => _completion.TrySetResult();
}
