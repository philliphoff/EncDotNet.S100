using System;
using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Converts a <see cref="ChromeTheme"/> value to its localized
/// display name for the settings ComboBox.
/// </summary>
internal sealed class ChromeThemeNameConverter : IValueConverter
{
    public static ChromeThemeNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is ChromeTheme theme
            ? theme switch
            {
                ChromeTheme.Light => Strings.ChromeTheme_Light,
                ChromeTheme.Dark => Strings.ChromeTheme_Dark,
                ChromeTheme.S100Night => Strings.ChromeTheme_S100Night,
                _ => theme.ToString(),
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
