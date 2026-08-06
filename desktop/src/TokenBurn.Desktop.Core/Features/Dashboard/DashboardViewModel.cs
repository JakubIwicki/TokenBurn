using LiveChartsCore;
using TokenBurn.Desktop.Core.Features.Common;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Core.Features.Dashboard;

public sealed partial class DashboardViewModel : ObservableObject, IActivatable
{
    private readonly IDispatcher _dispatcher;
    private readonly IInsightsApiClient _api;
    private readonly IRefreshLoop _refreshLoop;

    public DashboardViewModel(IDispatcher dispatcher, IInsightsApiClient api, IRefreshLoop refreshLoop)
    {
        _dispatcher = dispatcher;
        _api = api;
        _refreshLoop = refreshLoop;
    }

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _groupBy = "day";

    [ObservableProperty]
    private DateTimeOffset? _from;

    [ObservableProperty]
    private DateTimeOffset? _to;

    [ObservableProperty]
    private double? _heroCost;

    [ObservableProperty]
    private string _heroCostText = "";

    [ObservableProperty]
    private double _pricingCoverage;

    [ObservableProperty]
    private string _pricingCoverageText = "";

    [ObservableProperty]
    private IReadOnlyList<ISeries> _chartSeries = [];

    [ObservableProperty]
    private IReadOnlyList<string> _chartLabels = [];

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
            var response = await _api.CostsSummaryAsync(From, To, GroupBy, null, ct);
            _dispatcher.Invoke(() =>
            {
                HeroCost = response.Totals.CostUsd;
                HeroCostText = ChartSeriesFactory.FormatCost(response.Totals.CostUsd);
                PricingCoverage = response.PricingCoverage;
                PricingCoverageText = ChartSeriesFactory.FormatCoverage(response.PricingCoverage);
                // A cost series without its coverage line violates the "coverage never hidden" rule.
                ChartSeries = [.. ChartSeriesFactory.BuildCostSeries(response), ChartSeriesFactory.BuildCoverageLine(response.PricingCoverage)];
                ChartLabels = ChartSeriesFactory.BuildBucketLabels(response);
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
}
