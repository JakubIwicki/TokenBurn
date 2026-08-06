using System.Globalization;
using System.Windows.Data;

namespace TokenBurn.Desktop.Features.Common;

/// <summary>
/// Two-way DatePicker glue: <c>DatePicker.SelectedDate</c> is <see cref="DateTime"/>, the ViewModels
/// expose <see cref="DateTimeOffset"/>. Converts to the date component on the way out and back with
/// the local offset (a local operator console has no timezone story).
/// </summary>
public sealed class DateTimeOffsetConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset dto ? (DateTime?)dto.Date : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime dt ? (DateTimeOffset?)new DateTimeOffset(dt) : null;
}
