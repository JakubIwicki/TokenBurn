using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TokenBurn.Desktop.Features.Common;

/// <summary>Non-empty string → Visible (error lines, empty-state text); empty/null → Collapsed.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
