namespace TokenBurn.Desktop.Core.Services;

/// <summary>
/// Thin UI-thread marshaler. ViewModels route every observable-state mutation through it so they
/// stay testable without a real WPF Dispatcher. The WPF app provides <c>WpfDispatcher</c>; tests
/// use a synchronous fake.
/// </summary>
public interface IDispatcher
{
    void Invoke(Action action);
    Task InvokeAsync(Action action);
}
