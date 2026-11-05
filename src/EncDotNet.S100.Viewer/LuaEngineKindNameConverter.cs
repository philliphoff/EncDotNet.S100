using System.Globalization;
using Avalonia.Data.Converters;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Viewer.Resources;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Converts a <see cref="LuaEngineKind"/> value to its display name for the
/// Settings Lua-engine ComboBox.
/// </summary>
internal sealed class LuaEngineKindNameConverter : IValueConverter
{
    public static LuaEngineKindNameConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LuaEngineKind kind
            ? kind switch
            {
                LuaEngineKind.LuaCSharp => Strings.Settings_LuaEngine_LuaCSharp,
                LuaEngineKind.MoonSharp => Strings.Settings_LuaEngine_MoonSharp,
                _ => kind.ToString(),
            }
            : value?.ToString();

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
