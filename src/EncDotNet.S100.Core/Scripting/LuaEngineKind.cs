namespace EncDotNet.S100.Scripting;

/// <summary>
/// The Lua engines a host can run the S-100 Part 9a portrayal rules on.
/// </summary>
/// <remarks>
/// Both implement <see cref="ILuaEngine"/> and produce the same portrayal
/// output. Lua-CSharp is the default; MoonSharp stays selectable as a
/// reference while Lua-CSharp settles in, and will be removed later (map
/// render design, step 0). Use <see cref="LuaEngineKinds"/> to parse a
/// setting or read the test override.
/// </remarks>
public enum LuaEngineKind
{
    /// <summary>
    /// Lua-CSharp (package <c>EncDotNet.S100.Scripting.LuaCSharp</c>), a
    /// low-allocation Lua 5.2 interpreter. The default.
    /// </summary>
    LuaCSharp = 0,

    /// <summary>
    /// MoonSharp (package <c>EncDotNet.S100.Scripting.MoonSharp</c>), the
    /// previous engine.
    /// </summary>
    MoonSharp = 1,
}
