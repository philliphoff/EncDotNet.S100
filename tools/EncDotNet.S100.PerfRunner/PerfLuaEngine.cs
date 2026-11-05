using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.LuaCSharp;
using EncDotNet.S100.Scripting.MoonSharp;

namespace EncDotNet.S100.PerfRunner;

/// <summary>
/// The Lua engine the scenarios portray with: <c>luacsharp</c> (the
/// default) or <c>moonsharp</c>, from <c>--lua-engine</c> or the
/// <c>S100_LUA_ENGINE</c> environment variable.
/// </summary>
/// <remarks>
/// The perf gate builds this runner against the base commit's libraries, so
/// this parses names itself rather than using <c>LuaEngineKinds</c>, which an
/// older base doesn't have.
/// </remarks>
internal static class PerfLuaEngine
{
    /// <summary>The environment variable that selects the engine when <c>--lua-engine</c> isn't given.</summary>
    public const string EnvironmentVariable = "S100_LUA_ENGINE";

    private static string? _selected;

    /// <summary>The selected engine's name.</summary>
    public static string Name =>
        _selected ?? Normalize(Environment.GetEnvironmentVariable(EnvironmentVariable)) ?? "luacsharp";

    /// <summary>Selects the engine. Call before a scenario first portrays.</summary>
    /// <param name="value">The name from the command line, or <see langword="null"/> to keep the default.</param>
    /// <returns><see langword="false"/> when the name is unknown.</returns>
    public static bool TrySelect(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(fromEnvironment) || Normalize(fromEnvironment) is not null;
        }

        _selected = Normalize(value);
        return _selected is not null;
    }

    /// <summary>Creates the selected engine.</summary>
    public static ILuaEngine Create() => Name == "moonsharp"
        ? new MoonSharpLuaEngine()
        : new LuaCSharpLuaEngine();

    private static string? Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "luacsharp" or "lua-csharp" => "luacsharp",
        "moonsharp" => "moonsharp",
        _ => null,
    };
}
