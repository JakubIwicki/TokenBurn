using System.Collections.ObjectModel;
using System.Globalization;
using TokenBurn.Desktop.Core.Features.Common;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Core.Features.Findings;

/// <summary>
/// Findings list. Read-only in this phase — the API exposes no acknowledgement mutation.
/// </summary>
public sealed partial class FindingsViewModel : ObservableObject, IActivatable
{
    private const int PageSize = 50;

    private readonly IDispatcher _dispatcher;
    private readonly IInsightsApiClient _api;
    private readonly IRefreshLoop _refreshLoop;

    public FindingsViewModel(IDispatcher dispatcher, IInsightsApiClient api, IRefreshLoop refreshLoop)
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
    private string? _kindFilter;

    [ObservableProperty]
    private string? _severityFilter;

    [ObservableProperty]
    private bool? _acknowledgedFilter;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private string? _nextCursor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMore;

    public ObservableCollection<FindingRow> Findings { get; } = [];

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
            var response = await _api.FindingsAsync(KindFilter, SeverityFilter, AcknowledgedFilter, null, PageSize, ct);
            _dispatcher.Invoke(() =>
            {
                Findings.Clear();
                foreach (var finding in response.Findings)
                    Findings.Add(FindingRow.From(finding));
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
            var response = await _api.FindingsAsync(KindFilter, SeverityFilter, AcknowledgedFilter, cursor, PageSize, ct);
            _dispatcher.Invoke(() =>
            {
                foreach (var finding in response.Findings)
                    Findings.Add(FindingRow.From(finding));
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

public sealed record FindingRow(
    Guid Id,
    Guid RunId,
    string Kind,
    string Severity,
    string WastedCost,
    string DetectedAt,
    string AcknowledgedAt)
{
    public static FindingRow From(FindingSummary finding) => new(
        finding.Id,
        finding.RunId,
        finding.Kind,
        finding.Severity,
        ChartSeriesFactory.FormatCost(finding.WastedCostUsd),
        finding.DetectedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        finding.AcknowledgedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—");
}
