using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EncDotNet.S100.Viewer;

/// <summary>Converts a <see cref="TimeFormat"/> value to its human-readable display name.</summary>
internal sealed class TimeFormatNameConverter : IValueConverter
{
    public static TimeFormatNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is TimeFormat tf ? tf.DisplayName() : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
