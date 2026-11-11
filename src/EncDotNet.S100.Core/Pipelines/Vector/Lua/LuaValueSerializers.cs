using System.Globalization;

namespace EncDotNet.S100.Pipelines.Vector.Lua;

/// <summary>
/// Core serializers that convert a <see cref="MarinerSettings"/>-derived value
/// into the Lua string literal expected by
/// <c>PortrayalSetContextParameter</c>. These are pure value coercions with no
/// product knowledge — the mapping <i>policy</i> (which mariner setting feeds
/// which declared parameter, and any inversion) lives in each product's
/// <see cref="LuaContextParameterBinding"/> list.
/// </summary>
public static class LuaValueSerializers
{
    /// <summary>Serializes a <see cref="bool"/> as the Lua literal <c>true</c>/<c>false</c>.</summary>
    public static string Bool(object? value) => (bool)value! ? "true" : "false";

    /// <summary>
    /// Serializes a numeric value using the invariant culture, matching the
    /// Lua side's numeric parsing of context-parameter strings.
    /// </summary>
    public static string Number(object? value) =>
        ((IFormattable)value!).ToString(null, CultureInfo.InvariantCulture);

    /// <summary>Serializes a string value as-is (empty string when null).</summary>
    public static string Str(object? value) => (string?)value ?? string.Empty;
}
