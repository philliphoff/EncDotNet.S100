using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Converts a <see cref="PaletteType"/> value to its localized display name.
/// </summary>
internal sealed class PaletteTypeNameConverter : IValueConverter
{
    public static PaletteTypeNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is PaletteType palette
            ? palette switch
            {
                PaletteType.Day => Strings.Palette_Day,
                PaletteType.Dusk => Strings.Palette_Dusk,
                PaletteType.Night => Strings.Palette_Night,
                _ => palette.ToString(),
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
