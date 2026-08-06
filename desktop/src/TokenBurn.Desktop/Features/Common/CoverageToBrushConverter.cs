using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace TokenBurn.Desktop.Features.Common;

/// <summary>
/// Coverage &lt; 1 is a warning (a cost figure with incomplete pricing is the product's founding lie);
/// full coverage reads as ok. Returns the palette's WarnBrush/SuccessBrush (frozen resource brushes,
/// resolved once and cached), so theming stays in TerminalPalette.xaml.
/// </summary>
public sealed class CoverageToBrushConverter : IValueConverter
{
    private static readonly object Gate = new();
    private static Brush? _warn;
    private static Brush? _success;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var coverage = value is double d ? d : 0d;
        return coverage < 1d ? GetWarn() : GetSuccess();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Brush GetWarn() => Get("WarnBrush", ref _warn);

    private static Brush GetSuccess() => Get("SuccessBrush", ref _success);

    private static Brush Get(string key, ref Brush? cached)
    {
        if (cached is not null)
            return cached;
        lock (Gate)
        {
            cached ??= (Brush)Application.Current.FindResource(key);
            return cached;
        }
    }
}
