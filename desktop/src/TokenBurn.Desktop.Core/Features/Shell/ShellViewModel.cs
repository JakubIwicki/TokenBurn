using System.ComponentModel;
using TokenBurn.Desktop.Core.Features.Ask;
using TokenBurn.Desktop.Core.Features.BurnTicker;
using TokenBurn.Desktop.Core.Features.Common;
using TokenBurn.Desktop.Core.Features.Dashboard;
using TokenBurn.Desktop.Core.Features.Findings;
using TokenBurn.Desktop.Core.Features.RunDetail;
using TokenBurn.Desktop.Core.Features.Runs;
using TokenBurn.Desktop.Core.Features.Search;
using TokenBurn.Desktop.Core.Services;

namespace TokenBurn.Desktop.Core.Features.Shell;

/// <summary>
/// Nav rail + session shell. Owns sign-in/sign-out, mirrors <see cref="IAuthSession"/> state, listens
/// for <see cref="IAuthSession.Unauthenticated"/>, and switches the active feature VM — calling
/// <see cref="IActivatable"/> on the incoming/outgoing view. Makes no client call itself. The burn
/// ticker footer is activated once at construction and stays live for the app lifetime.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, IDisposable
{
    private readonly IAuthSession _session;
    private readonly IDispatcher _dispatcher;
    private readonly DashboardViewModel _dashboard;
    private readonly RunsViewModel _runs;
    private readonly RunDetailViewModel _runDetail;
    private readonly SearchViewModel _search;
    private readonly FindingsViewModel _findings;
    private readonly AskViewModel _ask;
    private readonly BurnTickerViewModel _burnTicker;

    public ShellViewModel(
        IAuthSession session,
        IDispatcher dispatcher,
        DashboardViewModel dashboard,
        RunsViewModel runs,
        RunDetailViewModel runDetail,
        SearchViewModel search,
        FindingsViewModel findings,
        AskViewModel ask,
        BurnTickerViewModel burnTicker)
    {
        _session = session;
        _dispatcher = dispatcher;
        _dashboard = dashboard;
        _runs = runs;
        _runDetail = runDetail;
        _search = search;
        _findings = findings;
        _ask = ask;
        _burnTicker = burnTicker;

        IsAuthenticated = session.IsAuthenticated;
        GrantedScopes = session.GrantedScopes;

        _session.Unauthenticated += OnUnauthenticated;
        _runs.PropertyChanged += OnRunsPropertyChanged;

        ShowDashboard();
        _burnTicker.Activate();
    }

    [ObservableProperty]
    private IReadOnlyList<string> _features = [];

    [ObservableProperty]
    private bool _hasAskScope;

    [ObservableProperty]
    private bool _isAuthenticated;

    [ObservableProperty]
    private IReadOnlyList<string> _grantedScopes = [];

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _activeFeature = "";

    [ObservableProperty]
    private object? _activeView;

    [RelayCommand]
    private async Task SignInAsync(CancellationToken ct)
    {
        try
        {
            var ok = await _session.SignInAsync(ct);
            _dispatcher.Invoke(() =>
            {
                IsAuthenticated = _session.IsAuthenticated;
                GrantedScopes = _session.GrantedScopes;
                ErrorMessage = ok ? "" : "sign-in failed";
            });
        }
        catch (OperationCanceledException)
        {
            // cancelled — nothing to do
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() => ErrorMessage = ex.Message);
        }
    }

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken ct)
    {
        try
        {
            await _session.SignOutAsync(ct);
            _dispatcher.Invoke(() =>
            {
                IsAuthenticated = _session.IsAuthenticated;
                GrantedScopes = _session.GrantedScopes;
            });
        }
        catch (OperationCanceledException)
        {
            // cancelled — nothing to do
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() => ErrorMessage = ex.Message);
        }
    }

    [RelayCommand]
    private void ShowDashboard() => ShowFeature(_dashboard, "Dashboard");

    [RelayCommand]
    private void ShowRuns() => ShowFeature(_runs, "Runs");

    [RelayCommand]
    private void ShowSearch() => ShowFeature(_search, "Search");

    [RelayCommand]
    private void ShowFindings() => ShowFeature(_findings, "Findings");

    [RelayCommand]
    private void ShowAsk() => ShowFeature(_ask, "Ask");

    private void ShowFeature(object viewModel, string name)
    {
        if (ActiveView is IActivatable outgoing)
            outgoing.Deactivate();
        ActiveView = viewModel;
        ActiveFeature = name;
        if (viewModel is IActivatable incoming)
            incoming.Activate();
    }

    private void ShowRunDetail(Guid id)
    {
        if (ActiveView is IActivatable outgoing)
            outgoing.Deactivate();
        _runDetail.OpenCommand.Execute(id);
        ActiveView = _runDetail;
        ActiveFeature = "RunDetail";
    }

    private void OnUnauthenticated(object? sender, EventArgs e) =>
        _dispatcher.Invoke(() =>
        {
            IsAuthenticated = false;
            GrantedScopes = [];
        });

    // Fired by the source-generated GrantedScopes setter on every change (ctor, sign-in/out,
    // unauthenticated) — feature navigation is derived from the granted scopes.
    partial void OnGrantedScopesChanged(IReadOnlyList<string> value)
    {
        Features = BuildFeatures(value ?? []);
        HasAskScope = value?.Contains("ask.invoke") ?? false;
        _ask.ApplyScopes(value ?? []);
    }

    private static IReadOnlyList<string> BuildFeatures(IReadOnlyList<string> scopes)
    {
        var list = new List<string>();
        if (scopes.Contains("insights.read")) list.AddRange(["Dashboard", "Runs", "Search", "Findings"]);
        if (scopes.Contains("ask.invoke")) list.Add("Ask");
        return list;
    }

    private void OnRunsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RunsViewModel.SelectedRun) && _runs.SelectedRun is { } run)
            ShowRunDetail(run.Id);
    }

    public void Dispose()
    {
        _session.Unauthenticated -= OnUnauthenticated;
        _runs.PropertyChanged -= OnRunsPropertyChanged;
        _burnTicker.Deactivate();
    }
}
