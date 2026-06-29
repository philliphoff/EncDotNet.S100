using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Converts a <see cref="BasemapMode"/> value to its localized display
/// name for the settings combo box (issue #295).
/// </summary>
internal sealed class BasemapModeNameConverter : IValueConverter
{
    public static BasemapModeNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BasemapMode mode
            ? mode switch
            {
                BasemapMode.None => Strings.Basemap_None,
                BasemapMode.Offline => Strings.Basemap_Offline,
                BasemapMode.Online => Strings.Basemap_Online,
                _ => mode.ToString(),
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
