using System.Collections.ObjectModel;
using TokenBurn.Desktop.Core.Features.Common;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Core.Features.Runs;

public sealed partial class RunsViewModel : ObservableObject, IActivatable
{
    private const int PageSize = 50;

    private readonly IDispatcher _dispatcher;
    private readonly IInsightsApiClient _api;
    private readonly IRefreshLoop _refreshLoop;

    public RunsViewModel(IDispatcher dispatcher, IInsightsApiClient api, IRefreshLoop refreshLoop)
    {
        _dispatcher = dispatcher;
        _api = api;
        _refreshLoop = refreshLoop;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private DateTimeOffset? _from;

    [ObservableProperty]
    private DateTimeOffset? _to;

    [ObservableProperty]
    private string? _modelFilter;

    [ObservableProperty]
    private string? _personaFilter;

    [ObservableProperty]
    private double? _minCost;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private string? _nextCursor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMore;

    [ObservableProperty]
    private RunSummary? _selectedRun;

    public ObservableCollection<RunRow> Runs { get; } = [];

    public void Activate()
    {
        _refreshLoop.Tick += OnTick;
        _refreshLoop.Start();
        OnTick();
    }

    public void Deactivate()
    {
        _refreshLoop.Tick -= OnTick;
        _refreshLoop.Stop();
    }

    private void OnTick() =>
        _dispatcher.Invoke(() => _ = RefreshCommand.ExecuteAsync(null));

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var response = await _api.RunsAsync(From, To, ModelFilter, PersonaFilter, MinCost, null, PageSize, ct);
            _dispatcher.Invoke(() =>
            {
                Runs.Clear();
                foreach (var run in response.Runs)
                    Runs.Add(RunRow.From(run));
                NextCursor = response.NextCursor;
                HasMore = !string.IsNullOrEmpty(response.NextCursor);
            });
        }
        catch (OperationCanceledException)
        {
            // cancelled — leave the previous state untouched
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() => ErrorMessage = ex.Message);
        }
        finally
        {
            _dispatcher.Invoke(() => IsLoading = false);
        }
    }

    private bool CanLoadMore() => HasMore && !IsLoading;

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private async Task LoadMoreAsync(CancellationToken ct)
    {
        var cursor = NextCursor;
        if (cursor is null)
            return;
        try
        {
            var response = await _api.RunsAsync(From, To, ModelFilter, PersonaFilter, MinCost, cursor, PageSize, ct);
            _dispatcher.Invoke(() =>
            {
                foreach (var run in response.Runs)
                    Runs.Add(RunRow.From(run));
                NextCursor = response.NextCursor;
                HasMore = !string.IsNullOrEmpty(response.NextCursor);
            });
        }
        catch (OperationCanceledException)
        {
            // cancelled — nothing appended
        }
        catch (Exception ex)
        {
            _dispatcher.Invoke(() => ErrorMessage = ex.Message);
        }
    }
}

public sealed record RunRow(
    Guid Id,
    string Session,
    string Persona,
    string Model,
    string Status,
    string Tokens,
    string Cost,
    string PricingStatus)
{
    public static RunRow From(RunSummary run) => new(
        run.Id,
        run.SessionId,
        run.Persona ?? "—",
        run.ModelSlug ?? "—",
        run.Status,
        ChartSeriesFactory.FormatTokens(run.InputTokens ?? 0),
        ChartSeriesFactory.FormatCost(run.CostUsd),
        run.PricingStatus);
}
