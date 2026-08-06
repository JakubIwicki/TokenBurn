using System.Globalization;
using System.Windows.Data;

namespace TokenBurn.Desktop.Features.Common;

/// <summary>
/// LiveCharts2's <c>Axis.Labels</c> is an <see cref="IList{T}"/>, but ViewModels expose
/// <see cref="IReadOnlyList{T}"/> (ChartSeriesFactory output). Copy into a mutable list for the binding.
/// </summary>
public sealed class ToListConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            IEnumerable<string> strings => strings.ToList(),
            null => null,
            _ => new List<string>(),
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
