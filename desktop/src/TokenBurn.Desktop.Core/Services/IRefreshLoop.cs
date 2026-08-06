namespace TokenBurn.Desktop.Core.Services;

/// <summary>
/// Poll pump for the burn ticker and polling ViewModels. <see cref="Tick"/> fires once per period
/// while started; the implementation swallows subscriber exceptions so a UI poller never dies.
/// <see cref="Start"/> takes a reference and <see cref="Stop"/> drops one — the underlying loop dies
/// only when the reference count reaches zero, so a persistent subscriber (the burn ticker) keeps
/// polling while transient features come and go.
/// </summary>
public interface IRefreshLoop
{
    event Action? Tick;
    void Start();
    void Stop();
}
