using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Converts a <see cref="RenderSubsystemKind"/> value to its localized display
/// name for the Settings render-subsystem ComboBox (issue #331).
/// </summary>
internal sealed class RenderSubsystemNameConverter : IValueConverter
{
    public static RenderSubsystemNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is RenderSubsystemKind kind
            ? kind switch
            {
                RenderSubsystemKind.Mapsui => Strings.Settings_RenderSubsystem_Mapsui,
                RenderSubsystemKind.TiledScene => Strings.Settings_RenderSubsystem_TiledScene,
                _ => kind.ToString(),
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
