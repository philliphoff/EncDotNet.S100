using MoonSharp.Interpreter;

namespace EncDotNet.S100.Scripting.MoonSharp;

/// <summary>
/// <see cref="ILuaEngine"/> implementation backed by the MoonSharp interpreter,
/// a pure .NET Lua 5.2 implementation with no native dependencies.
/// </summary>
public sealed class MoonSharpLuaEngine : ILuaEngine
{
    /// <summary>
    /// The <see cref="CoreModules"/> loaded into each new context.
    /// Defaults to a safe subset (no OS/IO/debug access).
    /// </summary>
    public CoreModules Modules { get; init; } =
        CoreModules.Preset_HardSandbox
        | CoreModules.Coroutine
        | CoreModules.TableIterators
        | CoreModules.Metatables
        | CoreModules.ErrorHandling
        | CoreModules.LoadMethods;

    /// <inheritdoc />
    public ILuaContext CreateContext()
    {
        var script = new Script(Modules);
        return new MoonSharpLuaContext(script);
    }
}
