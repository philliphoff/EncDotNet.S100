using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.LuaCSharp;
using EncDotNet.S100.Scripting.MoonSharp;

namespace EncDotNet.S100;

/// <summary>
/// Creates the <see cref="ILuaEngine"/> for a <see cref="LuaEngineKind"/>.
/// </summary>
/// <remarks>
/// This package references both engines so a host can pick one by setting
/// (the <c>s100 --lua-engine</c> option uses this). A host that wants only
/// one engine can reference that engine's package and construct it directly.
/// </remarks>
public static class LuaEngineFactory
{
    /// <summary>Creates an engine.</summary>
    /// <param name="kind">The engine to create.</param>
    /// <returns>A new engine.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="kind"/> isn't a defined engine.</exception>
    public static ILuaEngine Create(LuaEngineKind kind = LuaEngineKinds.Default) => kind switch
    {
        LuaEngineKind.LuaCSharp => new LuaCSharpLuaEngine(),
        LuaEngineKind.MoonSharp => new MoonSharpLuaEngine(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Lua engine."),
    };
}
