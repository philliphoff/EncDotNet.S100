using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.LuaCSharp;
using EncDotNet.S100.Scripting.MoonSharp;

namespace EncDotNet.S100.TestSupport;

/// <summary>
/// Creates the Lua engine tests run portrayal on: Lua-CSharp by default, or
/// the engine <c>S100_LUA_ENGINE</c> names (<c>luacsharp</c> or
/// <c>moonsharp</c>), so a whole suite can be rerun on the other engine.
/// </summary>
internal static class TestLuaEngine
{
    /// <summary>The engine the environment selects.</summary>
    public static LuaEngineKind Kind { get; } = LuaEngineKinds.FromEnvironment();

    /// <summary>Creates the engine the environment selects.</summary>
    public static ILuaEngine Create() => Create(Kind);

    /// <summary>Creates a given engine.</summary>
    public static ILuaEngine Create(LuaEngineKind kind) => kind switch
    {
        LuaEngineKind.LuaCSharp => new LuaCSharpLuaEngine(),
        LuaEngineKind.MoonSharp => new MoonSharpLuaEngine(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown Lua engine."),
    };
}
