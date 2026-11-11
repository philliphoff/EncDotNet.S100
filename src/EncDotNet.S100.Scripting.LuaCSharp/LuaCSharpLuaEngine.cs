using EncDotNet.S100.Scripting.LuaCSharp.Compatibility;
using Lua;
using Lua.Standard;

namespace EncDotNet.S100.Scripting.LuaCSharp;

/// <summary>
/// <see cref="ILuaEngine"/> implementation backed by
/// <see href="https://github.com/nuskey8/Lua-CSharp">Lua-CSharp</see>, a
/// Lua 5.2 interpreter written in C# with no native dependencies.
/// </summary>
/// <remarks>
/// <para>
/// Each context is sandboxed like MoonSharp's <c>Preset_HardSandbox</c>: the
/// basic, <c>bit32</c>, coroutine, math, package, string and table libraries
/// are opened, and <c>io</c>, <c>os</c> and <c>debug</c> are not. The
/// platform has no file system, so <c>dofile</c>, <c>loadfile</c> and the
/// file searcher of <c>require</c> cannot reach the disk, and <c>print</c>
/// output is discarded. Modules come only from
/// <see cref="ILuaContext.SetModuleLoader"/>.
/// </para>
/// <para>
/// Two shims in <c>Compatibility/</c> restore Lua 5.2 behaviour that
/// Lua-CSharp 0.5.8 deviates from and that the S-100 Part 9a framework
/// relies on: <see cref="EqDispatchShim"/> and <see cref="RequireShim"/>.
/// </para>
/// </remarks>
public sealed class LuaCSharpLuaEngine : ILuaEngine
{
    /// <inheritdoc />
    public ILuaContext CreateContext()
    {
        var state = LuaState.Create(SandboxPlatform.Create());
        state.OpenBasicLibrary();
        state.OpenBitwiseLibrary();
        state.OpenCoroutineLibrary();
        state.OpenMathLibrary();
        state.OpenModuleLibrary();
        state.OpenStringLibrary();
        state.OpenTableLibrary();

        RequireShim.Install(state);
        EqDispatchShim.Install(state);

        return new LuaCSharpLuaContext(state);
    }
}
