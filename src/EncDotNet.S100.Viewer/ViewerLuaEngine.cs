using System.Reflection;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.LuaCSharp;
using EncDotNet.S100.Scripting.MoonSharp;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Creates the viewer's Lua engines for the selected
/// <see cref="LuaEngineKind"/> (<see cref="ViewerSettings.ResolveLuaEngine"/>).
/// </summary>
internal static class ViewerLuaEngine
{
    /// <summary>
    /// Whether <c>S100_LUA_ENGINE</c> is set, which pins the engine and
    /// disables the setting (the perf harness and A/B runs use it).
    /// </summary>
    public static bool IsPinnedByEnvironment =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LuaEngineKinds.EnvironmentVariable));

    /// <summary>Creates an engine.</summary>
    /// <param name="kind">The engine.</param>
    /// <returns>A new engine.</returns>
    public static ILuaEngine Create(LuaEngineKind kind) => kind switch
    {
        LuaEngineKind.MoonSharp => new MoonSharpLuaEngine(),
        _ => new LuaCSharpLuaEngine(),
    };

    /// <summary>
    /// The assembly of an engine's adapter, which the portrayal caches hash
    /// into their keys so each engine keeps its own entries.
    /// </summary>
    /// <param name="kind">The engine.</param>
    /// <returns>Its adapter assembly.</returns>
    public static Assembly AssemblyOf(LuaEngineKind kind) => kind switch
    {
        LuaEngineKind.MoonSharp => typeof(MoonSharpLuaEngine).Assembly,
        _ => typeof(LuaCSharpLuaEngine).Assembly,
    };
}
