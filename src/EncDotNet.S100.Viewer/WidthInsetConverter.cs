using System.Globalization;
using Avalonia.Data.Converters;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Subtracts a fixed inset (passed as <c>ConverterParameter</c>) from a bound
/// width, clamping the result to a non-negative value. Used by the pick
/// report's scrolling region to cap its content width to the panel's own
/// rendered width minus the scroll padding — a width that is independent of
/// the content (so it never feeds back into layout) and always resolves,
/// unlike a themed <c>ScrollViewer.Viewport.Width</c> which some control
/// themes do not surface to bindings.
/// </summary>
internal sealed class WidthInsetConverter : IValueConverter
{
    public static WidthInsetConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width) || double.IsInfinity(width))
            return double.NaN;

        var inset = parameter switch
        {
            double d => d,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0d,
        };

        return Math.Max(0d, width - inset);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
