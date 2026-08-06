using System.Globalization;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using TokenBurn.Desktop.Core.Services.Generated;

namespace TokenBurn.Desktop.Core.Features.Common;

/// <summary>
/// Headless, deterministic conversions from the generated cost DTOs to LiveCharts2 model series,
/// plus the monospace-friendly formatting helpers the ViewModels share. No native Skia is touched.
/// </summary>
public static class ChartSeriesFactory
{
    public static IReadOnlyList<ISeries> BuildCostSeries(CostSummaryResponse response)
    {
        var buckets = response.Buckets ?? [];
        return buckets.Count == 0
            ? []
            :
            [
                new ColumnSeries<double>
                {
                    Name = "cost",
                    Values = buckets.Select(b => b.CostUsd ?? 0d).ToArray(),
                },
            ];
    }

    public static IReadOnlyList<string> BuildBucketLabels(CostSummaryResponse response) =>
        (response.Buckets ?? []).Select(b => b.Key).ToArray();

    public static LineSeries<double> BuildCoverageLine(double coverage) =>
        new() { Name = "coverage", Values = [coverage] };

    public static string FormatCost(double? cost) =>
        cost is null ? "$0.00" : $"${cost.Value.ToString("0.00", CultureInfo.InvariantCulture)}";

    public static string FormatTokens(long tokens)
    {
        if (tokens >= 1_000_000_000)
            return (tokens / 1_000_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "B";
        if (tokens >= 1_000_000)
            return (tokens / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        if (tokens >= 1_000)
            return (tokens / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        return tokens.ToString("N0", CultureInfo.InvariantCulture);
    }

    public static string FormatCoverage(double coverage) =>
        coverage.ToString("0.00", CultureInfo.InvariantCulture);
}
