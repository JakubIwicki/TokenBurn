using System.Windows;
using System.Windows.Threading;
using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Services;

/// <summary>
/// WPF implementation of <see cref="IDispatcher"/>. The only place
/// <see cref="Application.Current.Dispatcher"/> is touched in the app.
/// Windows-only; no unit tests (the headless graph fakes this interface).
/// </summary>
public sealed class WpfDispatcher : IDispatcher
{
    private readonly Dispatcher _dispatcher =
        Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

    public void Invoke(Action action) => _dispatcher.Invoke(action);

    public Task InvokeAsync(Action action) => _dispatcher.InvokeAsync(action).Task;
}
