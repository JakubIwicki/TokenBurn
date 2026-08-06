using TokenBurn.Desktop.Core.Features.BurnTicker;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features;

public sealed class BurnTickerViewModelTests
{
    private sealed class Fixture
    {
        public FakeDispatcher Dispatcher { get; } = new();
        public FakeRefreshLoop Loop { get; } = new();
        public Mock<IInsightsApiClient> Api { get; } = new();
        public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        public BurnTickerViewModel Sut { get; }

        public Fixture()
        {
            Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CostSummaryResponse { Totals = new CostTotals(), Buckets = [], PricingCoverage = 0 });
            Sut = new BurnTickerViewModel(Dispatcher, Api.Object, Loop, Clock);
        }
    }

    [Fact]
    public async Task Refresh_LoadsUsageLine()
    {
        var fx = new Fixture();
        fx.Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CostSummaryResponse
            {
                Totals = new CostTotals { InputTokens = 1_200_000, OutputTokens = 180_000, CostUsd = 4.62 },
                Buckets = [],
                PricingCoverage = 0.62,
            });

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.Line.Should().Be("usage ▸ 1.2M in · 180k out · $4.62 · coverage 0.62");
        fx.Sut.InputTokensText.Should().Be("1.2M");
        fx.Sut.CostText.Should().Be("$4.62");
        fx.Sut.CoverageText.Should().Be("0.62");
    }

    [Fact]
    public async Task Refresh_QueriesNowMinusWindowToNow()
    {
        var fx = new Fixture();
        DateTimeOffset? seenFrom = null;
        DateTimeOffset? seenTo = null;
        fx.Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .Returns((DateTimeOffset? from, DateTimeOffset? to, string? groupBy, int? limit, CancellationToken ct) =>
            {
                seenFrom = from;
                seenTo = to;
                return Task.FromResult(new CostSummaryResponse { Totals = new CostTotals(), Buckets = [], PricingCoverage = 0 });
            });

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        seenTo.Should().Be(fx.Clock.GetUtcNow());
        seenFrom.Should().Be(fx.Clock.GetUtcNow() - TimeSpan.FromDays(30));
    }

    [Fact]
    public async Task Refresh_WhenApiThrows_KeepsLastLine()
    {
        var fx = new Fixture();
        await fx.Sut.RefreshCommand.ExecuteAsync(null); // loads the empty line
        var lastLine = fx.Sut.Line;
        fx.Api.Setup(a => a.CostsSummaryAsync(It.IsAny<DateTimeOffset?>(), It.IsAny<DateTimeOffset?>(), It.IsAny<string?>(), It.IsAny<int?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        await fx.Sut.RefreshCommand.ExecuteAsync(null);

        fx.Sut.Line.Should().Be(lastLine);
        fx.Sut.ErrorMessage.Should().Be("boom");
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
