using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace EncDotNet.S100.Viewer;

/// <summary>Converts a <see cref="DistanceUnit"/> value to its human-readable display name.</summary>
internal sealed class DistanceUnitNameConverter : IValueConverter
{
    public static DistanceUnitNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DistanceUnit unit ? unit.DisplayName() : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
