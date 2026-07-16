using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Converts a <see cref="VectorSceneMode"/> value to its localized display name
/// for the Settings scene-mode ComboBox (issue #331).
/// </summary>
internal sealed class VectorSceneModeNameConverter : IValueConverter
{
    public static VectorSceneModeNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is VectorSceneMode mode
            ? mode switch
            {
                VectorSceneMode.Tiled => Strings.Settings_SceneMode_Tiled,
                VectorSceneMode.Single => Strings.Settings_SceneMode_Single,
                _ => mode.ToString(),
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
