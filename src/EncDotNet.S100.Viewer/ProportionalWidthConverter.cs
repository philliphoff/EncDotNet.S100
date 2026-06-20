using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Multiplies a bound width by a fraction (passed as <c>ConverterParameter</c>),
/// clamping the result to a non-negative value. Used by the pick report's
/// attribute table to cap the auto-sized label column to a fraction of the
/// panel width, so a long attribute name grows to fit when there is room but
/// can never crowd out the value column on a narrow panel.
/// </summary>
internal sealed class ProportionalWidthConverter : IValueConverter
{
    public static ProportionalWidthConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double width || double.IsNaN(width) || double.IsInfinity(width))
            return double.NaN;

        var fraction = parameter switch
        {
            double d => d,
            string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 1d,
        };

        return Math.Max(0d, width * fraction);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
