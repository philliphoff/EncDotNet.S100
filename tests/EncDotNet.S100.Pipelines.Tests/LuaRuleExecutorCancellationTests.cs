using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// A running <c>PortrayalMain</c> stops at the next feature it emits once its
/// token is cancelled (#1266), rather than running every rule to the end.
/// </summary>
public class LuaRuleExecutorCancellationTests
{
    private const int FeatureCount = 1000;

    private const string MainLua = """
        function PortrayalCreateContextParameter(id, type, default) return { id = id } end
        function PortrayalInitializeContextParameters(parameters) end
        function PortrayalMain()
          for i = 1, 1000 do
            HostPortrayalEmit('f' .. i, 'PointInstruction:X', '')
          end
          return true
        end
        """;

    private sealed class RuleSource : ILuaRuleSource
    {
        public IReadOnlyList<LuaContextParameter> ContextParameters => [];

        public ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<string>>(["main.lua"]);

        public ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(fileName == "main.lua" ? MainLua : null);
    }

    /// <summary>Records emits and runs <see cref="OnEmit"/> after each.</summary>
    private sealed class Provider : ILuaDataProvider, ILuaDataProviderFactory
    {
        private readonly List<EmittedInstruction> _emitted = [];

        public Action<int>? OnEmit { get; init; }

        public IReadOnlyList<EmittedInstruction> EmittedInstructions => _emitted;

        public IReadOnlyList<string> PostLoadScripts => [];

        public ILuaDataProvider Create(MarinerSettings mariner) => this;

        public string? GetFeatureTypeCode(string featureRef) => null;

        public void RegisterHostFunctions(ILuaContext lua) =>
            lua.SetGlobal("HostPortrayalEmit", (Func<string, string, string, bool>)((featureRef, instructions, observed) =>
            {
                _emitted.Add(new EmittedInstruction { FeatureRef = featureRef, InstructionString = instructions, ObservedParameters = observed });
                OnEmit?.Invoke(_emitted.Count);
                return true;
            }));
    }

    private static LuaRuleExecutor Executor(Provider provider) =>
        new(TestLuaEngine.Create(), new RuleSource(), provider, "TEST", []);

    [Fact]
    public async Task Cancelling_MidRun_StopsAtTheNextFeature()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider = new Provider { OnEmit = count => { if (count == 3) cts.Cancel(); } };

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Executor(provider).ExecuteRawAsync(MarinerSettings.Default, cts.Token));

        Assert.Equal(cts.Token, ex.CancellationToken);
        Assert.Equal(3, provider.EmittedInstructions.Count);
    }

    [Fact]
    public async Task ACancellableTokenThatIsNotCancelled_RunsEveryFeature()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var provider = new Provider();

        var emitted = await Executor(provider).ExecuteRawAsync(MarinerSettings.Default, cts.Token);

        Assert.Equal(FeatureCount, emitted.Count);
    }
}
