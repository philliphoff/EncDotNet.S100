using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Scripting.MoonSharp;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The Lua rule executor drops the framework's empty type-check and profiling
/// calls before it loads the rules (#1275), and only where dropping them cannot
/// change what the rules do.
/// </summary>
public class LuaNoOpCallStripperTests
{
    // CheckType and Debug.StartPerformance emit a marker, so a call that
    // survives shows up in the emitted instructions. Each marker gets its own
    // feature ref, since the executor drops repeated emits.
    private const string Framework = """
        function PortrayalCreateContextParameter(id, type, default) return { id = id } end
        function PortrayalInitializeContextParameters(parameters) end
        function Mark(name) n = (n or 0) + 1 HostPortrayalEmit('mark' .. n, name, '') end
        function CheckType(...) Mark('CheckType') end
        Debug = { StartPerformance = function(...) Mark('StartPerformance') end }
        function Value() Mark('Value') return 1 end
        """;

    private sealed class RuleSource(string main) : ILuaRuleSource
    {
        public IReadOnlyList<LuaContextParameter> ContextParameters => [];

        public ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<string>>(["main.lua"]);

        public ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(fileName == "main.lua" ? main : null);
    }

    private sealed class Provider : ILuaDataProvider, ILuaDataProviderFactory
    {
        private readonly List<EmittedInstruction> _emitted = [];

        public IReadOnlyList<EmittedInstruction> EmittedInstructions => _emitted;

        public IReadOnlyList<string> PostLoadScripts => [];

        public ILuaDataProvider Create(MarinerSettings mariner) => this;

        public string? GetFeatureTypeCode(string featureRef) => null;

        public void RegisterHostFunctions(ILuaContext lua) =>
            lua.SetGlobal("HostPortrayalEmit", (Func<string, string, string, bool>)((featureRef, instructions, observed) =>
            {
                _emitted.Add(new EmittedInstruction { FeatureRef = featureRef, InstructionString = instructions, ObservedParameters = observed });
                return true;
            }));
    }

    private static async Task<List<string>> RunAsync(string portrayalMainBody, string extra = "")
    {
        var main = Framework + extra + "\nfunction PortrayalMain()\n" + portrayalMainBody + "\nreturn true\nend\n";
        var provider = new Provider();
        var executor = new LuaRuleExecutor(new MoonSharpLuaEngine(), new RuleSource(main), provider, "TEST", []);
        var emitted = await executor.ExecuteRawAsync(MarinerSettings.Default, TestContext.Current.CancellationToken);
        return [.. emitted.Select(e => e.InstructionString)];
    }

    [Fact]
    public async Task CallsWithPlainArguments_AreRemoved()
    {
        var calls = await RunAsync("""
            local x = 'a'
            CheckType(x, 'string')
              CheckType(x, "string", 3); -- trailing comment
            CheckType()
            Debug.StartPerformance('Lua Code - Total')
            """);

        Assert.Empty(calls);
    }

    [Fact]
    public async Task CallsOnCrlfLines_AreRemoved()
    {
        var calls = await RunAsync(
            "local x = 'a'\r\nCheckType(x, 'string')\r\nDebug.StartPerformance('x') -- comment\r\n");

        Assert.Empty(calls);
    }

    [Fact]
    public async Task CallsWhoseArgumentsCouldHaveSideEffects_AreKept()
    {
        var calls = await RunAsync("""
            local t = { 'a' }
            CheckType(Value(), 'number')
            CheckType(t[1], 'string')
            CheckType(t.Type, 'string')
            """);

        Assert.Equal(["Value", "CheckType", "CheckType", "CheckType"], calls);
    }

    [Fact]
    public async Task CallsThatAreNotAWholeLine_AreKept()
    {
        var calls = await RunAsync("""
            local x = 'a'
            if x then CheckType(x, 'string') end
            local y = CheckType(x, 'string')
            """);

        Assert.Equal(["CheckType", "CheckType"], calls);
    }

    [Fact]
    public async Task TypeChecksTurnedOn_NothingIsRemoved()
    {
        var calls = await RunAsync(
            "local x = 'a'\nCheckType(x, 'string')",
            "\nfunction EnableChecks() TypeSystemChecks(true) end\n");

        Assert.Equal(["CheckType"], calls);
    }

    [Fact]
    public async Task RemovedCalls_KeepLineNumbersInErrors()
    {
        // error() is on line 10 of main.lua: six framework lines, the
        // function header, two removed calls, then the error.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync("""
            CheckType()
            Debug.StartPerformance('x')
            error('boom')
            """));

        Assert.Contains("(10,", ex.Message);
    }
}
