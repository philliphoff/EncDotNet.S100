using Lua;
using Lua.Standard;

namespace EncDotNet.S100.Scripting.Tests;

/// <summary>
/// Canaries for the two Lua-CSharp deviations the adapter shims (#1278).
/// They run a bare <see cref="LuaState"/>, without the shims, and pass
/// while Lua-CSharp still deviates. When one fails after a package upgrade,
/// that release fixed the deviation: delete the matching shim in
/// <c>src/EncDotNet.S100.Scripting.LuaCSharp/Compatibility</c> and this test.
/// </summary>
public sealed class UpstreamDeviationTests
{
    [Fact]
    public async Task EqDispatch_UpstreamStillCallsEqForNil()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        var results = await state.DoStringAsync("""
            local called = false
            local t = setmetatable({}, { __eq = function() called = true return false end })
            local _ = (t == nil)
            return called
            """, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            results[0].ToBoolean(),
            "Lua-CSharp no longer calls __eq for nil; EqDispatchShim may be unnecessary.");
    }

    [Fact]
    public async Task Require_UpstreamStillForgetsModulesThatReturnNothing()
    {
        var state = LuaState.Create();
        state.OpenStandardLibraries();
        state.ModuleLoader = new SourceLoader("runs = (runs or 0) + 1");
        var results = await state.DoStringAsync(
            "require('m') require('m') return runs",
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(
            results[0].Read<double>() > 1,
            "Lua-CSharp now records modules that return nothing; RequireShim may be unnecessary.");
    }

    private sealed class SourceLoader(string source) : ILuaModuleLoader
    {
        public bool Exists(string moduleName) => moduleName == "m";

        public ValueTask<LuaModule> LoadAsync(string moduleName, CancellationToken cancellationToken = default) =>
            new(new LuaModule(moduleName, source));
    }
}
