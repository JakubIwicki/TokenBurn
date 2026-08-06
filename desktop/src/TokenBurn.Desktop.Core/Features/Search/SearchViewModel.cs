using System.Collections.ObjectModel;
using System.Globalization;
using TokenBurn.Desktop.Core.Features.Common;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Core.Features.Search;

public sealed partial class SearchViewModel : ObservableObject
{
    private const int PageSize = 50;

    private readonly IDispatcher _dispatcher;
    private readonly IInsightsApiClient _api;

    public SearchViewModel(IDispatcher dispatcher, IInsightsApiClient api)
    {
        _dispatcher = dispatcher;
        _api = api;
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _query = "";

    [ObservableProperty]
    private string _mode = "hybrid";

    [ObservableProperty]
    private string? _modelFilter;

    [ObservableProperty]
    private string? _personaFilter;

    [ObservableProperty]
    private string? _sourceFilter;

    [ObservableProperty]
    private string? _statusFilter;

    [ObservableProperty]
    private DateTimeOffset? _from;

    [ObservableProperty]
    private DateTimeOffset? _to;

    [ObservableProperty]
    private long _total;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private string? _nextCursor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMore;

    public ObservableCollection<SearchHitRow> Hits { get; } = [];

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            var response = await _api.SearchAsync(From, To, Query, Mode, ModelFilter, PersonaFilter, SourceFilter, StatusFilter, null, PageSize, ct);
            _dispatcher.Invoke(() =>
            {
                Total = response.Total;
                Hits.Clear();
                var index = 0;
                foreach (var hit in response.Hits)
                {
                    Hits.Add(SearchHitRow.From(index + 1, hit, response.Highlights.ElementAtOrDefault(index)));
                    index++;
                }
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
            var response = await _api.SearchAsync(From, To, Query, Mode, ModelFilter, PersonaFilter, SourceFilter, StatusFilter, cursor, PageSize, ct);
            _dispatcher.Invoke(() =>
            {
                var index = Hits.Count;
                foreach (var hit in response.Hits)
                {
                    Hits.Add(SearchHitRow.From(index + 1, hit, response.Highlights.ElementAtOrDefault(index)));
                    index++;
                }
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

public sealed record SearchHitRow(
    int Rank,
    Guid Id,
    string Session,
    string Model,
    string Status,
    string StartedAt,
    string Tokens,
    string Cost,
    IReadOnlyList<string> DiffChrome,
    string PricingStatus)
{
    public static SearchHitRow From(int rank, SearchRunHit hit, ICollection<string>? highlights) => new(
        rank,
        hit.Id,
        hit.Session_id,
        hit.Model_slug ?? "—",
        hit.Status,
        hit.Started_at?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "—",
        ChartSeriesFactory.FormatTokens(hit.Input_tokens ?? 0),
        ChartSeriesFactory.FormatCost(hit.Cost_usd),
        BuildDiffChrome(highlights),
        hit.Pricing_status ?? "n/a");

    // The search API returns only matched snippets, so the +/− chrome is a presentation mapping: each
    // matched fragment renders as an addition line.
    private static IReadOnlyList<string> BuildDiffChrome(ICollection<string>? highlights) =>
        highlights is null ? [] : [.. highlights.Select(h => $"+ {h}")];
}
