using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Scripting.Tests;

/// <summary>
/// <see cref="ILuaContext.TrySetLineHook"/>: LuaCSharp reports each line with
/// the chunk it's in, including lines in required modules and coroutines;
/// MoonSharp has no hook.
/// </summary>
public sealed class LineHookTests
{
    private const string Module = """
        local M = {}
        function M.pick(x)
          if x == 1 then
            return 'one'
          else
            return 'other'
          end
        end
        return M
        """;

    private const string Main = """
        local m = require 'Pick'
        function run(x)
          return m.pick(x)
        end
        function runInCoroutine(x)
          local co = coroutine.wrap(function() return m.pick(x) end)
          return co()
        end
        """;

    private static ILuaContext LuaCSharp()
    {
        var lua = TestLuaEngine.Create(LuaEngineKind.LuaCSharp).CreateContext();
        lua.SetModuleLoader(name => name is "Pick" ? Module : null);
        lua.Execute(Main, "main.lua");
        return lua;
    }

    [Fact]
    public void LuaCSharp_ReportsChunkAndLine_IntoRequiredModules()
    {
        using var lua = LuaCSharp();
        var lines = new List<(string, int)>();
        Assert.True(lua.TrySetLineHook((chunk, line) => lines.Add((chunk, line))));

        Assert.Equal("other", lua.Call("run", 2));

        Assert.Equal([("main.lua", 3), ("Pick.lua", 3), ("Pick.lua", 6)], lines);
    }

    [Fact]
    public void LuaCSharp_ReportsLinesInCoroutines()
    {
        using var lua = LuaCSharp();
        var lines = new List<(string, int)>();
        lua.TrySetLineHook((chunk, line) => lines.Add((chunk, line)));

        Assert.Equal("one", lua.Call("runInCoroutine", 1));

        Assert.Contains(("Pick.lua", 3), lines);
        Assert.Contains(("Pick.lua", 4), lines);
        Assert.DoesNotContain(("Pick.lua", 6), lines);
    }

    [Fact]
    public void LuaCSharp_RemovedHook_IsNotCalled()
    {
        using var lua = LuaCSharp();
        var count = 0;
        lua.TrySetLineHook((_, _) => count++);
        lua.Call("run", 1);
        Assert.True(count > 0);

        Assert.True(lua.TrySetLineHook(null));
        var before = count;
        lua.Call("run", 1);

        Assert.Equal(before, count);
    }

    [Fact]
    public void LuaCSharp_NamedChunk_NamesErrors()
    {
        using var lua = TestLuaEngine.Create(LuaEngineKind.LuaCSharp).CreateContext();

        var ex = Assert.ThrowsAny<Exception>(() => lua.Execute("local x = 1\nerror('boom')", "rule.lua"));

        Assert.Contains("rule.lua:2", lua.DescribeError(ex), StringComparison.Ordinal);
    }

    [Fact]
    public void MoonSharp_HasNoLineHook()
    {
        using var lua = TestLuaEngine.Create(LuaEngineKind.MoonSharp).CreateContext();

        Assert.False(lua.TrySetLineHook((_, _) => { }));
        Assert.False(lua.TrySetLineHook(null));
    }

    [Fact]
    public void MoonSharp_NamedChunk_Runs()
    {
        using var lua = TestLuaEngine.Create(LuaEngineKind.MoonSharp).CreateContext();

        lua.Execute("x = 41 + 1", "main.lua");

        Assert.Equal(42.0, lua.GetGlobal("x"));
    }
}
