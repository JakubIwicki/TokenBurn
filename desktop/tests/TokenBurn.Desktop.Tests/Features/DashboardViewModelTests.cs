using LiveChartsCore.SkiaSharpView;
using TokenBurn.Desktop.Core.Features.Dashboard;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features;

public sealed class DashboardViewModelTests
{
    private sealed class Fixture
    {
        public FakeDispatcher Dispatcher { get; } = new();
        public FakeRefreshLoop Loop { get; } = new();
        public Mock<IInsightsApiClient> Api { get; } = new();
        public DashboardViewModel Sut { get; }

        public Fixture()
        {
            Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CostSummaryResponse { Totals = new CostTotals(), Buckets = [], PricingCoverage = 0 });
            Sut = new DashboardViewModel(Dispatcher, Api.Object, Loop);
        }
    }

    private static CostSummaryResponse CostSummary() => new()
    {
        Totals = new CostTotals { CostUsd = 4.62, InputTokens = 1_200_000 },
        Buckets = [new CostBucket { Key = "2026-08-01", CostUsd = 4.62 }],
        PricingCoverage = 0.62,
    };

    [Fact]
    public async Task Refresh_LoadsHeroCostChartAndCoverage()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CostSummary());

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.HeroCost.Should().Be(4.62);
        fx.Sut.HeroCostText.Should().Be("$4.62");
        fx.Sut.PricingCoverage.Should().Be(0.62);
        fx.Sut.PricingCoverageText.Should().Be("0.62");
        fx.Sut.ChartSeries.Should().Contain(s => s is LineSeries<double>); // coverage line on the chart
        fx.Sut.ChartLabels.Should().ContainSingle("2026-08-01");
        fx.Sut.IsLoading.Should().BeFalse();
        fx.Sut.ErrorMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task Refresh_PassesGroupByAndFilters()
    {
        var fx = new Fixture();
        string? seenGroupBy = null;
        fx.Sut.GroupBy = "model";
        fx.Sut.From = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        fx.Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? groupBy, int? limit, CancellationToken ct) =>
            {
                seenGroupBy = groupBy;
                return Task.FromResult(CostSummary());
            });

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        seenGroupBy.Should().Be("model");
    }

    [Fact]
    public async Task Refresh_WhenApiThrows_SetsErrorMessage()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.ErrorMessage.Should().Be("boom");
        fx.Sut.IsLoading.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_WhenCancelled_LeavesStateUntouched()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? groupBy, int? limit, CancellationToken ct) =>
            {
                var tcs = new TaskCompletionSource<CostSummaryResponse>();
                ct.Register(() => tcs.TrySetCanceled());
                return tcs.Task;
            });

        var executing = fx.Sut.RefreshCommand.ExecuteAsync(null);
        fx.Sut.RefreshCommand.Cancel();
        await executing;

        fx.Sut.HeroCost.Should().BeNull();
        fx.Sut.ErrorMessage.Should().BeEmpty();
        fx.Sut.ChartSeries.Should().BeEmpty();
    }

    [Fact]
    public void Activate_StartsLoop()
    {
        var fx = new Fixture();

        fx.Sut.Activate();

        fx.Loop.StartCount.Should().Be(1);
    }

    [Fact]
    public void Deactivate_StopsLoop()
    {
        var fx = new Fixture();
        fx.Sut.Activate();

        fx.Sut.Deactivate();

        fx.Loop.StopCount.Should().Be(1);
    }
}
