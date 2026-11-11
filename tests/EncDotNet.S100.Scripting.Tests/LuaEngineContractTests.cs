using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Scripting.Tests;

/// <summary>
/// The <see cref="ILuaContext"/> contract, checked on every engine so both
/// hand the portrayal rules the same values and behave the same way.
/// </summary>
public sealed class LuaEngineContractTests
{
    public static TheoryData<LuaEngineKind> Engines => [LuaEngineKind.LuaCSharp, LuaEngineKind.MoonSharp];

    private static ILuaContext Context(LuaEngineKind kind) => TestLuaEngine.Create(kind).CreateContext();

    [Theory]
    [MemberData(nameof(Engines))]
    public void Call_ReturnsFirstValue(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.Execute("""
            function add(a, b) return a + b end
            function pair(a, b) return a + b, 'extra' end
            """);

        // MoonSharp's Call returns every value as an array when a function
        // returns more than one, so the contract is checked on one value.
        Assert.Equal(5.0, lua.Call("add", 2, 3));
        Assert.Equal([5.0, "extra"], lua.CallMultiReturn("pair", 2, 3));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void CallMultiReturn_NoValues_IsOneNull(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.Execute("function nothing() end");

        Assert.Equal([null], lua.CallMultiReturn("nothing"));
        Assert.Null(lua.Call("nothing"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Call_UndefinedFunction_Throws(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        Assert.Throws<InvalidOperationException>(() => lua.Call("missing"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Globals_RoundTripScalars(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.SetGlobal("b", true);
        lua.SetGlobal("i", 42);
        lua.SetGlobal("l", 7L);
        lua.SetGlobal("f", 1.5f);
        lua.SetGlobal("s", "text");
        lua.SetGlobal("n", null);

        Assert.Equal(true, lua.GetGlobal("b"));
        Assert.Equal(42.0, lua.GetGlobal("i"));
        Assert.Equal(7.0, lua.GetGlobal("l"));
        Assert.Equal(1.5, lua.GetGlobal("f"));
        Assert.Equal("text", lua.GetGlobal("s"));
        Assert.Null(lua.GetGlobal("n"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Tables_MarshalBothWays(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.SetGlobal("dict", new Dictionary<string, object?> { ["a"] = 1.0, ["b"] = "x" });
        lua.SetGlobal("list", new List<object> { "p", 2.0 });
        lua.Execute("""
            out_list = { list[1], list[2], dict.a, dict.b }
            out_dict = { x = 1, [2] = 'two' }
            out_empty = {}
            out_holes = { [1] = 'a', [3] = 'c' }
            """);

        Assert.Equal(new List<object?> { "p", 2.0, 1.0, "x" }, lua.GetGlobal("out_list"));
        var dict = Assert.IsType<Dictionary<string, object?>>(lua.GetGlobal("out_dict"));
        Assert.Equal(1.0, dict["x"]);
        Assert.Equal("two", dict["2"]);
        Assert.Empty(Assert.IsType<Dictionary<string, object?>>(lua.GetGlobal("out_empty")));
        var holes = Assert.IsType<Dictionary<string, object?>>(lua.GetGlobal("out_holes"));
        Assert.Equal(["1", "3"], holes.Keys.Order());
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void HostFunctions_ConvertArgumentsAndResults(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        var seen = new List<string>();
        lua.SetGlobal("code", (Func<double, string>)(id => $"code{id}"));
        lua.SetGlobal("attr", (Func<double, string, string?, List<object>>)((id, path, code) =>
            [id, path, code ?? "<null>"]));
        lua.SetGlobal("count", (Func<double, string, string, double>)((_, _, _) => 3));
        lua.SetGlobal("emit", (Func<string, string, string, bool>)((a, b, c) =>
        {
            seen.Add($"{a}|{b}|{c}");
            return true;
        }));
        lua.SetGlobal("trace", (Action<string>)(seen.Add));
        lua.SetGlobal("pair", (Action<string, string?>)((a, b) => seen.Add($"{a}:{b ?? "<null>"}")));
        lua.SetGlobal("info", (Func<string, IReadOnlyDictionary<string, object?>>)(name =>
            new Dictionary<string, object?> { ["Name"] = name, ["Values"] = new List<object> { 1.0, "two" } }));
        lua.SetGlobal("nothing", (Func<string, object?>)(_ => null));

        lua.Execute("""
            r_code = code(12)
            local a = attr(5, '', nil)
            r_attr = a[1] .. '/' .. a[2] .. '/' .. a[3]
            r_count = count(1, 'x', 'y')
            r_emit = emit(7, 'b', 'c')
            trace('hello')
            pair('k', nil)
            local i = info('Light')
            r_info = i.Name .. #i.Values .. i.Values[2]
            r_nothing = nothing('x') == nil
            """);

        Assert.Equal("code12", lua.GetGlobal("r_code"));
        Assert.Equal("5//<null>", lua.GetGlobal("r_attr"));
        Assert.Equal(3.0, lua.GetGlobal("r_count"));
        Assert.Equal(true, lua.GetGlobal("r_emit"));
        Assert.Equal("Light2two", lua.GetGlobal("r_info"));
        Assert.Equal(true, lua.GetGlobal("r_nothing"));
        Assert.Equal(["7|b|c", "hello", "k:<null>"], seen);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void HostFunctions_UncommonShape_StillWorks(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.SetGlobal("scale", (Func<int, float, double>)((a, b) => a * b));
        lua.Execute("r = scale(3, 0.5)");
        Assert.Equal(1.5, lua.GetGlobal("r"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void HostFunctionException_Propagates(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.SetGlobal("fail", (Action<string>)(message => throw new InvalidDataException(message)));
        lua.Execute("function run() fail('boom') end");

        var ex = Assert.ThrowsAny<Exception>(() => lua.Call("run"));
        Assert.Contains("boom", lua.DescribeError(ex), StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void DescribeError_IncludesTheLuaLocation(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.SetModuleLoader(name => name is "Rules" or "Rules.lua"
            ? "local M = {}\n\nfunction M.run()\n  local t = nil\n  return t.field\nend\n\nreturn M\n"
            : null);
        lua.Execute("function go() return require('Rules').run() end");

        var ex = Assert.ThrowsAny<Exception>(() => lua.Call("go"));
        var description = lua.DescribeError(ex);
        Assert.Contains("Rules", description, StringComparison.Ordinal);
        Assert.Contains("5", description, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Require_ModuleReturningNothing_LoadsOnce(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        var loads = 0;
        lua.SetModuleLoader(name =>
        {
            if (name is not ("Rule" or "Rule.lua"))
            {
                return null;
            }

            loads++;
            return "ruleRuns = (ruleRuns or 0) + 1";
        });
        lua.Execute("""
            r1 = require('Rule')
            r2 = require('Rule')
            r3 = package.loaded['Rule']
            """);

        Assert.Equal(1.0, lua.GetGlobal("ruleRuns"));
        Assert.Equal(true, lua.GetGlobal("r1"));
        Assert.Equal(true, lua.GetGlobal("r2"));
        Assert.Equal(true, lua.GetGlobal("r3"));
        Assert.InRange(loads, 1, 2);
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Require_ModuleReturningTable_IsCached(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.SetModuleLoader(name => name is "Mod" or "Mod.lua" ? "count = (count or 0) + 1 return { v = 3 }" : null);
        lua.Execute("""
            local a = require('Mod')
            local b = require('Mod')
            same = rawequal(a, b)
            v = b.v
            """);

        Assert.Equal(true, lua.GetGlobal("same"));
        Assert.Equal(3.0, lua.GetGlobal("v"));
        Assert.Equal(1.0, lua.GetGlobal("count"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Require_UnknownModule_Throws(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.SetModuleLoader(_ => null);
        Assert.ThrowsAny<Exception>(() => lua.Execute("require('Nope')"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void EqDispatch_FollowsLua52(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.Execute("""
            calls = 0
            local mt = { __eq = function(a, b) calls = calls + 1 return a.v == b.v end }
            local x = setmetatable({ v = 1 }, mt)
            local y = setmetatable({ v = 1 }, mt)

            r_nil = (x ~= nil)
            r_nil_left = (nil == x)
            r_number = (x == 1)
            r_string = (x == 'a')
            r_plain = (x == {})
            r_plain_left = ({} == x)
            r_same = (x == y)
            r_identity = (x == x)
            """);

        Assert.Equal(true, lua.GetGlobal("r_nil"));
        Assert.Equal(false, lua.GetGlobal("r_nil_left"));
        Assert.Equal(false, lua.GetGlobal("r_number"));
        Assert.Equal(false, lua.GetGlobal("r_string"));
        Assert.Equal(false, lua.GetGlobal("r_plain"));
        Assert.Equal(false, lua.GetGlobal("r_plain_left"));
        Assert.Equal(true, lua.GetGlobal("r_same"));
        Assert.Equal(true, lua.GetGlobal("r_identity"));
        if (kind == LuaEngineKind.LuaCSharp)
        {
            // Only x == y reaches the handler. MoonSharp also calls it for x == x.
            Assert.Equal(1.0, lua.GetGlobal("calls"));
        }
    }

    /// <summary>
    /// Lua 5.2 cases MoonSharp gets wrong too: it calls the left operand's
    /// handler when the handlers differ, and not a handler two metatables
    /// share. The S-100 rules depend on neither.
    /// </summary>
    [Fact]
    public void EqDispatch_LuaCSharp_ComparesHandlersAsLua52Does()
    {
        using var lua = Context(LuaEngineKind.LuaCSharp);
        lua.Execute("""
            calls = 0
            local eq = function(a, b) calls = calls + 1 return true end
            local x = setmetatable({}, { __eq = eq })
            local y = setmetatable({}, { __eq = eq })
            local z = setmetatable({}, { __eq = function() calls = calls + 100 return true end })
            r_shared = (x == y)
            r_other = (x == z)
            """);

        Assert.Equal(true, lua.GetGlobal("r_shared"));
        Assert.Equal(false, lua.GetGlobal("r_other"));
        Assert.Equal(1.0, lua.GetGlobal("calls"));
    }

    /// <summary>
    /// The S-100 framework's own probe (<c>main.lua</c>): it assigns
    /// <c>__eq</c> after <c>setmetatable</c> and expects a Lua 5.1/5.2 engine
    /// not to call it for a table without one.
    /// </summary>
    [Theory]
    [MemberData(nameof(Engines))]
    public void EqDispatch_FrameworkProbe_ReportsStandardEngine(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.Execute("""
            local t1 = {}
            local t2 = {}
            local m1 = {}
            local m2 = {}
            setmetatable(t1, m1)
            setmetatable(t2, m2)
            m1.__eq = function(p1, p2) return true end
            EqMetaMethodGuarantee = not (t1 == t2)
            """);

        Assert.Equal(true, lua.GetGlobal("EqMetaMethodGuarantee"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Sandbox_HasNoIoOsOrDebug(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.Execute("""
            r_io = io == nil
            r_os = os == nil
            r_debug = debug == nil
            r_math = math ~= nil and string ~= nil and table ~= nil
            """);

        Assert.Equal(true, lua.GetGlobal("r_io"));
        Assert.Equal(true, lua.GetGlobal("r_os"));
        Assert.Equal(true, lua.GetGlobal("r_debug"));
        Assert.Equal(true, lua.GetGlobal("r_math"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Sandbox_CannotReadFiles(LuaEngineKind kind)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lua-sandbox-{Guid.NewGuid():N}.lua");
        File.WriteAllText(path, "leaked = true");
        try
        {
            using var lua = Context(kind);
            lua.SetModuleLoader(_ => null);
            lua.SetGlobal("path", path);
            lua.Execute("""
                r_dofile = pcall(dofile, path)
                local ok, chunk = pcall(loadfile, path)
                r_loadfile = ok and chunk ~= nil
                r_require = pcall(require, path)
                """);

            Assert.Equal(false, lua.GetGlobal("r_dofile"));
            Assert.Equal(false, lua.GetGlobal("r_loadfile"));
            Assert.Equal(false, lua.GetGlobal("r_require"));
            Assert.Null(lua.GetGlobal("leaked"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Numbers the rules turn into instruction strings. The engines format
    /// them the same way, which the portrayal output depends on.
    /// </summary>
    [Theory]
    [MemberData(nameof(Engines))]
    public void NumberFormatting_MatchesAcrossEngines(LuaEngineKind kind)
    {
        using var lua = Context(kind);
        lua.Execute("""
            r = table.concat({
              tostring(3), tostring(3.0), tostring(-0.5), tostring(0.1), tostring(1/3),
              tostring(123456789012), tostring(2^53), tostring(1e15), tostring(1e16), tostring(1e-5),
              3 .. '', 2.5 .. '', string.format('%d', 7), string.format('%.2f', 1.005),
              string.format('%g', 0.0001), tostring(10 / 2), tostring(7 % 3)
            }, ' ')
            """);

        Assert.Equal(
            "3 3 -0.5 0.1 0.3333333333333333 123456789012 9007199254740992 1000000000000000 10000000000000000 1E-05 3 2.5 7 1.00 0.0001 5 1",
            lua.GetGlobal("r"));
    }

    [Theory]
    [MemberData(nameof(Engines))]
    public void Execute_UsesInvariantCulture(LuaEngineKind kind)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            using var lua = Context(kind);
            lua.Execute("r = tostring(2.5) .. '|' .. (1.25 .. '') .. '|' .. tonumber('3.5')");
            Assert.Equal("2.5|1.25|3.5", lua.GetGlobal("r"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void LuaCSharp_CancelledToken_StopsARunningCall()
    {
        using var lua = Context(LuaEngineKind.LuaCSharp);
        lua.Execute("function spin() local i = 0 while true do i = i + 1 end end");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        Assert.ThrowsAny<OperationCanceledException>(() => lua.CallCancellable("spin", cts.Token));
    }

    [Fact]
    public void Engines_ReportTheirKindByName()
    {
        Assert.True(LuaEngineKinds.TryParse("LuaCSharp", out var a));
        Assert.Equal(LuaEngineKind.LuaCSharp, a);
        Assert.True(LuaEngineKinds.TryParse("lua-csharp", out var b));
        Assert.Equal(LuaEngineKind.LuaCSharp, b);
        Assert.True(LuaEngineKinds.TryParse(" MoonSharp ", out var c));
        Assert.Equal(LuaEngineKind.MoonSharp, c);
        Assert.False(LuaEngineKinds.TryParse("nlua", out _));
        Assert.Equal(LuaEngineKind.LuaCSharp, LuaEngineKinds.Default);
        Assert.Equal("moonsharp", LuaEngineKinds.GetName(LuaEngineKind.MoonSharp));
    }
}
