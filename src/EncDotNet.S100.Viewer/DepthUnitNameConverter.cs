using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>Converts a <see cref="DepthUnit"/> value to its localised display name.</summary>
internal sealed class DepthUnitNameConverter : IValueConverter
{
    public static DepthUnitNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DepthUnit unit
            ? unit switch
            {
                DepthUnit.Metres => Strings.DepthUnit_Metres,
                DepthUnit.Feet => Strings.DepthUnit_Feet,
                DepthUnit.FathomsFeet => Strings.DepthUnit_FathomsFeet,
                DepthUnit.Fathoms => Strings.DepthUnit_Fathoms,
                _ => unit.ToString(),
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
