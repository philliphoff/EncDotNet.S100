using System.Globalization;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// How <see cref="LuaRuleExecutor"/> splits a run across Lua states, on a
/// small catalogue whose main loop has the S-100 Part 9a shape: features that
/// write text to a <c>TextPlacement</c> elsewhere in the cell, a
/// <c>TextPlacement</c> emitted after every other feature, numeric ids that
/// overwrite entries of the feature list, and a rule that looks across the
/// whole cell through <c>portrayalContext:GetFeatures</c>.
/// </summary>
public sealed class LuaShardExecutorTests
{
    private const string Framework = """
        function PortrayalCreateContextParameter(id, type, default) return { id = id } end
        function PortrayalInitializeContextParameters(parameters)
          local items = { Type = 'array:FeaturePortrayalItem' }
          function items:AddFeature(feature)
            local item = { Feature = feature }
            self[#self + 1] = item
            self[feature.ID] = item
          end
          for _, id in ipairs(HostGetFeatureIDs()) do
            items:AddFeature({ ID = id, Code = HostFeatureGetCode(id) })
          end
          portrayalContext = { FeaturePortrayalItems = items }
          function portrayalContext:GetFeatures(code)
            local features = {}
            for _, item in ipairs(self.FeaturePortrayalItems) do
              if code == nil or item.Feature.Code == code then features[#features + 1] = item.Feature end
            end
            return features
          end
        end
        """;

    private const string Main = Framework + "\n" + """
        function PortrayalMain()
          local all = {}
          for _, f in ipairs(portrayalContext:GetFeatures()) do all[f.ID] = f end
          local marks = #portrayalContext:GetFeatures('Mark')
          local textPlacements = {}
          for _, item in ipairs(portrayalContext.FeaturePortrayalItems) do
            local f = item.Feature
            if f.Code ~= 'TextPlacement' then
              if f.Code == 'Bad' then error('bad feature ' .. f.ID) end
              local tp = HostTextPlacementOf(f.ID)
              if tp ~= 0 then
                local t = all[tp]
                t._text = (t._text or '') .. f.ID .. ','
              end
              HostPortrayalEmit(f.ID, f.Code .. ';marks:' .. marks, 'observed')
            else
              textPlacements[#textPlacements + 1] = f
            end
          end
          for _, f in ipairs(textPlacements) do
            if f._text then HostPortrayalEmit(f.ID, 'TP;' .. f._text, 'observed') end
          end
          EXTRA
          return true
        end
        """;

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(8)]
    public async Task ShardedRun_EmitsWhatAFullRunEmits(int shards)
    {
        var full = await RunAsync(Main.Replace("EXTRA", "", StringComparison.Ordinal), sharding: null);
        var sharded = await RunAsync(
            Main.Replace("EXTRA", "", StringComparison.Ordinal),
            new LuaShardOptions { MaxShards = shards, MinFeaturesPerShard = 10 });

        Assert.Equal(1, full.Contexts);
        Assert.Equal(shards, sharded.Contexts);
        Assert.Contains(full.Emitted, e => e.Contains("|TP;", StringComparison.Ordinal));
        Assert.Equal(full.Emitted, sharded.Emitted);
    }

    [Fact]
    public async Task ShardedRun_WithoutTheGrouping_DiffersFromAFullRun()
    {
        // The test's own check: a TextPlacement split from the features that
        // write to it loses their text, so the grouping is what keeps parity.
        var main = Main.Replace("EXTRA", "", StringComparison.Ordinal);
        var full = await RunAsync(main, sharding: null);
        var sharded = await RunAsync(main, new LuaShardOptions { MaxShards = 4, MinFeaturesPerShard = 10 }, group: false);

        Assert.NotEqual(full.Emitted, sharded.Emitted);
    }

    [Fact]
    public async Task EmitTheMergeCannotPlace_FallsBackToAFullRun()
    {
        // An emit for no visited feature: the merge can't tell where it goes,
        // so the cell is portrayed again in one state.
        var main = Main.Replace("EXTRA", "HostPortrayalEmit('summary', 'done', '')", StringComparison.Ordinal);
        var full = await RunAsync(main, sharding: null);
        var sharded = await RunAsync(main, new LuaShardOptions { MaxShards = 4, MinFeaturesPerShard = 10 });

        Assert.Equal(5, sharded.Contexts);
        Assert.Equal(full.Emitted, sharded.Emitted);
    }

    [Fact]
    public async Task FailingShard_ReportsTheRuleFailure()
    {
        var main = Main.Replace("EXTRA", "", StringComparison.Ordinal);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => RunAsync(
            main, new LuaShardOptions { MaxShards = 4, MinFeaturesPerShard = 10 }, badFeature: 151));

        Assert.Contains("Lua portrayal failed", ex.Message, StringComparison.Ordinal);
        Assert.Contains("bad feature", ex.Message, StringComparison.Ordinal);
    }

    private static async Task<(List<string> Emitted, int Contexts)> RunAsync(
        string main, LuaShardOptions? sharding, bool group = true, int badFeature = -1)
    {
        var engine = new CountingLuaEngine(TestLuaEngine.Create());
        var executor = new LuaRuleExecutor(
            engine, new RuleSource(main), new Cell(group, badFeature), "TEST", [], sharding: sharding);
        var emitted = await executor.ExecuteRawAsync(MarinerSettings.Default, TestContext.Current.CancellationToken);
        return ([.. emitted.Select(e => $"{e.FeatureRef}|{e.InstructionString}|{e.ObservedParameters}")], engine.Contexts);
    }

    private sealed class RuleSource(string main) : ILuaRuleSource
    {
        public IReadOnlyList<LuaContextParameter> ContextParameters => [];

        public ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<string>>(["main.lua"]);

        public ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<string?>(fileName == "main.lua" ? main : null);
    }

    /// <summary>
    /// 300 features with ids in a scrambled order (so the ids overwrite
    /// entries of the Lua feature list): every tenth is a TextPlacement, every
    /// seventh a Mark, and each other feature writes its text to a
    /// TextPlacement far from it in the list.
    /// </summary>
    private sealed class Cell(bool group, int badFeature) : ILuaDataProviderFactory
    {
        private const int Count = 300;

        private static readonly double[] Ids = [.. Enumerable.Range(0, Count).Select(i => (double)((i * 7 % 401) + 1))];

        private static string Code(int index) =>
            index % 10 == 0 ? "TextPlacement" : index % 7 == 0 ? "Mark" : "Feature";

        // The TextPlacement a feature writes to, as an index into Ids.
        private static int? TextPlacementOf(int index) =>
            Code(index) == "TextPlacement" ? null : (index * 37 % (Count / 10)) * 10;

        public ILuaDataProvider Create(MarinerSettings mariner) => new Provider(group, badFeature);

        private sealed class Provider(bool group, int badFeature) : ILuaDataProvider, ILuaShardGrouping
        {
            private readonly List<EmittedInstruction> _emitted = [];

            public IReadOnlyList<EmittedInstruction> EmittedInstructions => _emitted;

            public IReadOnlyList<string> PostLoadScripts => [];

            public string? GetFeatureTypeCode(string featureRef) => null;

            public void RegisterHostFunctions(ILuaContext lua)
            {
                lua.SetGlobal("HostGetFeatureIDs", (Func<List<object>>)(() => [.. Ids.Select(id => (object)id)]));
                lua.SetGlobal("HostFeatureGetCode", (Func<double, string>)(id =>
                {
                    var index = Array.IndexOf(Ids, id);
                    return index == badFeature ? "Bad" : Code(index);
                }));
                lua.SetGlobal("HostTextPlacementOf", (Func<double, double>)(id =>
                    TextPlacementOf(Array.IndexOf(Ids, id)) is { } tp ? Ids[tp] : 0));
                lua.SetGlobal("HostPortrayalEmit", (Func<string, string, string, bool>)((featureRef, instructions, observed) =>
                {
                    _emitted.Add(new EmittedInstruction { FeatureRef = featureRef, InstructionString = instructions, ObservedParameters = observed });
                    return true;
                }));
            }

            public IReadOnlyList<int> GetShardGroups(IReadOnlyList<string> featureRefs) =>
            [
                .. featureRefs.Select((featureRef, position) =>
                {
                    var index = Array.IndexOf(Ids, double.Parse(featureRef, CultureInfo.InvariantCulture));
                    return !group ? position : TextPlacementOf(index) ?? index;
                }),
            ];
        }
    }

    private sealed class CountingLuaEngine(ILuaEngine inner) : ILuaEngine
    {
        private int _contexts;

        public int Contexts => Volatile.Read(ref _contexts);

        public ILuaContext CreateContext()
        {
            Interlocked.Increment(ref _contexts);
            return inner.CreateContext();
        }
    }
}
