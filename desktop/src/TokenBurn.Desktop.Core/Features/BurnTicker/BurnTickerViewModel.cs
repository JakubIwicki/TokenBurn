using TokenBurn.Desktop.Core.Features.Common;
using TokenBurn.Desktop.Core.Services;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Core.Features.BurnTicker;

/// <summary>
/// Permanent footer line: aggregate usage, cost and pricing coverage over a rolling window. Polls on
/// the shared <see cref="IRefreshLoop"/>; on error it keeps the last line (a silent footer).
/// </summary>
public sealed partial class BurnTickerViewModel : ObservableObject, IActivatable
{
    private static readonly TimeSpan Window = TimeSpan.FromDays(30);

    private readonly IDispatcher _dispatcher;
    private readonly IInsightsApiClient _api;
    private readonly IRefreshLoop _refreshLoop;
    private readonly TimeProvider _timeProvider;

    public BurnTickerViewModel(IDispatcher dispatcher, IInsightsApiClient api, IRefreshLoop refreshLoop, TimeProvider timeProvider)
    {
        _dispatcher = dispatcher;
        _api = api;
        _refreshLoop = refreshLoop;
        _timeProvider = timeProvider;
    }

    [ObservableProperty]
    private string _inputTokensText = "0";

    [ObservableProperty]
    private string _outputTokensText = "0";

    [ObservableProperty]
    private string _costText = "$0.00";

    [ObservableProperty]
    private string _coverageText = "0.00";

    [ObservableProperty]
    private string _line = "usage ▸ 0 in · 0 out · $0.00 · coverage 0.00";

    [ObservableProperty]
    private string _errorMessage = "";

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
        var now = _timeProvider.GetUtcNow();
        try
        {
            var response = await _api.CostsSummaryAsync(now - Window, now, "day", 1, ct);
            _dispatcher.Invoke(() =>
            {
                InputTokensText = ChartSeriesFactory.FormatTokens(response.Totals.InputTokens);
                OutputTokensText = ChartSeriesFactory.FormatTokens(response.Totals.OutputTokens);
                CostText = ChartSeriesFactory.FormatCost(response.Totals.CostUsd);
                CoverageText = ChartSeriesFactory.FormatCoverage(response.PricingCoverage);
                Line = $"usage ▸ {InputTokensText} in · {OutputTokensText} out · {CostText} · coverage {CoverageText}";
                ErrorMessage = "";
            });
        }
        catch (OperationCanceledException)
        {
            // cancelled — keep the last line
        }
        catch (Exception ex)
        {
            // keep the last line; the ticker is a silent footer
            _dispatcher.Invoke(() => ErrorMessage = ex.Message);
        }
    }
}
