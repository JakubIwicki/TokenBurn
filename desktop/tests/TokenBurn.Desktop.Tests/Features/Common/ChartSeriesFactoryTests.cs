using LiveChartsCore.SkiaSharpView;
using TokenBurn.Desktop.Core.Features.Common;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Tests.Features.Common;

public sealed class ChartSeriesFactoryTests
{
    [Fact]
    public void BuildCostSeries_EmptyBuckets_ReturnsEmpty()
    {
        var response = new CostSummaryResponse { Totals = new CostTotals(), Buckets = [], PricingCoverage = 0 };

        ChartSeriesFactory.BuildCostSeries(response).Should().BeEmpty();
    }

    [Fact]
    public void BuildCostSeries_WithBuckets_OneColumnSeriesWithValues()
    {
        var response = new CostSummaryResponse
        {
            Totals = new CostTotals(),
            Buckets =
            [
                new CostBucket { Key = "2026-08-01", CostUsd = 1.5 },
                new CostBucket { Key = "2026-08-02", CostUsd = 2.25 },
            ],
            PricingCoverage = 0.62,
        };

        var series = ChartSeriesFactory.BuildCostSeries(response);

        series.Should().HaveCount(1);
        var column = series.Single().Should().BeOfType<ColumnSeries<double>>().Subject;
        column.Name.Should().Be("cost");
        column.Values.Should().ContainInOrder(1.5, 2.25);
    }

    [Fact]
    public void BuildBucketLabels_ReturnsKeysInOrder()
    {
        var response = new CostSummaryResponse
        {
            Totals = new CostTotals(),
            Buckets =
            [
                new CostBucket { Key = "2026-08-01" },
                new CostBucket { Key = "2026-08-02" },
            ],
            PricingCoverage = 0,
        };

        ChartSeriesFactory.BuildBucketLabels(response).Should().ContainInOrder("2026-08-01", "2026-08-02");
    }

    [Fact]
    public void BuildCoverageLine_HoldsSingleCoverageValue()
    {
        var line = ChartSeriesFactory.BuildCoverageLine(0.62);

        line.Name.Should().Be("coverage");
        line.Values.Should().ContainSingle();
        line.Values.Single().Should().Be(0.62);
    }

    [Fact]
    public void FormatCost_FormatsTwoDecimals() =>
        ChartSeriesFactory.FormatCost(4.62).Should().Be("$4.62");

    [Fact]
    public void FormatCost_Null_IsZero()
    {
        ChartSeriesFactory.FormatCost(null).Should().Be("$0.00");
    }

    [Theory]
    [InlineData(1_200_000_000, "1.2B")]
    [InlineData(1_200_000, "1.2M")]
    [InlineData(180_000, "180k")]
    [InlineData(950, "950")]
    public void FormatTokens_ScalesToSuffix(long tokens, string expected) =>
        ChartSeriesFactory.FormatTokens(tokens).Should().Be(expected);

    [Fact]
    public void FormatCoverage_FormatsTwoDecimals() =>
        ChartSeriesFactory.FormatCoverage(0.618).Should().Be("0.62");
}
