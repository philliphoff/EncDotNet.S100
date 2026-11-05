using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Scripting.Tests;

/// <summary>
/// <c>table.sort</c> on elements that compare equal. Lua's sort isn't
/// stable, so their order is whatever the algorithm leaves, and the S-101
/// rules depend on it (a sectored light's colours, <c>LightSectored.lua</c>).
/// </summary>
public sealed class TableSortTests
{
    private const string SortTies = """
        function sortTies(n, seed)
          local t = {}
          local x = seed
          for i = 1, n do
            x = (x * 1103515245 + 12345) % 2147483648
            t[i] = { x % 4, i }
          end
          table.sort(t, function(a, b) return a[1] > b[1] end)
          local out = {}
          for i, e in ipairs(t) do out[i] = e[2] end
          return table.concat(out, ',')
        end
        """;

    [Fact]
    public void LuaCSharp_SortsTiesAsReferenceLuaDoes()
    {
        using var engine = TestLuaEngine.Create(LuaEngineKind.LuaCSharp).CreateContext();
        using var reference = TestLuaEngine.Create(LuaEngineKind.LuaCSharp).CreateContext();
        reference.Execute(ReferenceLuaSort.Install);
        engine.Execute(SortTies);
        reference.Execute(SortTies);

        for (var seed = 1; seed <= 300; seed++)
        {
            var n = seed % 40;
            Assert.Equal(reference.Call("sortTies", n, seed), engine.Call("sortTies", n, seed));
        }
    }

    [Fact]
    public void MoonSharp_SortsTiesStably()
    {
        using var lua = TestLuaEngine.Create(LuaEngineKind.MoonSharp).CreateContext();
        lua.Execute("""
            local t = { { 1, 'a' }, { 1, 'b' }, { 1, 'c' }, { 1, 'd' } }
            table.sort(t, function(x, y) return x[1] > y[1] end)
            r = t[1][2] .. t[2][2] .. t[3][2] .. t[4][2]
            """);

        // Reference Lua 5.1/5.2 (and Lua-CSharp) give "acbd".
        Assert.Equal("abcd", lua.GetGlobal("r"));
    }

    [Theory]
    [InlineData(LuaEngineKind.LuaCSharp)]
    [InlineData(LuaEngineKind.MoonSharp)]
    public void ReferenceSort_SortsCorrectly(LuaEngineKind kind)
    {
        using var lua = TestLuaEngine.Create(kind).CreateContext();
        lua.Execute(ReferenceLuaSort.Install);
        lua.Execute("""
            local t = { 5, 3, 9, 1, 1, 7, 2, 8, 6, 4, 0 }
            table.sort(t)
            r = table.concat(t, ',')
            """);

        Assert.Equal("0,1,1,2,3,4,5,6,7,8,9", lua.GetGlobal("r"));
    }
}
