namespace TokenBurn.Desktop.Core.Features.Common;

/// <summary>
/// Lifecycle hook for polling ViewModels. The shell calls <see cref="Activate"/> when a feature
/// becomes the active view and <see cref="Deactivate"/> when it is navigated away from.
/// </summary>
public interface IActivatable
{
    void Activate();
    void Deactivate();
}
