using System.Diagnostics.CodeAnalysis;

namespace EncDotNet.S100.Scripting;

/// <summary>
/// Parses and names <see cref="LuaEngineKind"/> values, for settings, command
/// lines and the test override.
/// </summary>
public static class LuaEngineKinds
{
    /// <summary>The engine a host uses unless told otherwise.</summary>
    public const LuaEngineKind Default = LuaEngineKind.LuaCSharp;

    /// <summary>
    /// The environment variable that overrides the engine for tests and
    /// tools: <c>luacsharp</c> or <c>moonsharp</c>.
    /// </summary>
    public const string EnvironmentVariable = "S100_LUA_ENGINE";

    /// <summary>The names <see cref="TryParse"/> accepts, as a host shows them.</summary>
    public static IReadOnlyList<string> Names { get; } = ["luacsharp", "moonsharp"];

    /// <summary>Gets the name of <paramref name="kind"/>, as <see cref="TryParse"/> accepts it.</summary>
    /// <param name="kind">The engine.</param>
    /// <returns><c>luacsharp</c> or <c>moonsharp</c>.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> isn't a defined engine.</exception>
    public static string GetName(LuaEngineKind kind) => kind switch
    {
        LuaEngineKind.LuaCSharp => "luacsharp",
        LuaEngineKind.MoonSharp => "moonsharp",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Lua engine."),
    };

    /// <summary>
    /// Parses an engine name: <c>luacsharp</c> (also <c>lua-csharp</c>) or
    /// <c>moonsharp</c>, ignoring case.
    /// </summary>
    /// <param name="value">The name.</param>
    /// <param name="kind">The engine, when the name is known.</param>
    /// <returns><see langword="true"/> when the name is known.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out LuaEngineKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "luacsharp" or "lua-csharp":
                kind = LuaEngineKind.LuaCSharp;
                return true;
            case "moonsharp":
                kind = LuaEngineKind.MoonSharp;
                return true;
            default:
                kind = Default;
                return false;
        }
    }

    /// <summary>
    /// Reads the engine from <see cref="EnvironmentVariable"/>, or returns
    /// <see cref="Default"/> when it's unset.
    /// </summary>
    /// <returns>The engine.</returns>
    /// <exception cref="InvalidOperationException">The variable names no engine.</exception>
    public static LuaEngineKind FromEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(value))
        {
            return Default;
        }

        return TryParse(value, out var kind)
            ? kind
            : throw new InvalidOperationException(
                $"{EnvironmentVariable} is '{value}'; expected one of: {string.Join(", ", Names)}.");
    }
}
