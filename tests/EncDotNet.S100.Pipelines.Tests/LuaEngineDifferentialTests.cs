using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Portrays every cell of the test corpora with both Lua engines and checks
/// that the rules emit the same output: per feature, the instruction strings
/// and the context parameters they observed, in emission order (map render
/// design, step 0). A difference fails and names the cell and the feature.
/// </summary>
/// <remarks>
/// <para>
/// Each cell is portrayed through the real processor (the same path as the
/// viewer and the CLI), with the engine wrapped so every
/// <c>HostPortrayalEmit</c> call (and S-102's <c>_addInstructions</c>) is
/// recorded on its way to the provider. A cell whose rules fail on both
/// engines agrees when both fail the same way after the same output.
/// </para>
/// <para>
/// One known divergence is neutralised: MoonSharp's <c>table.sort</c> is
/// stable and reference Lua's isn't, so the MoonSharp side gets
/// <see cref="ReferenceLuaSort"/> and the comparison checks everything
/// else. <see cref="MoonSharpStableSort_ReordersOneLightDescription"/> pins
/// what that divergence changes in the corpus.
/// </para>
/// <para>
/// Committed fixtures always run. Real data runs when its environment
/// variable is set and is skipped otherwise: <c>ENCDOTNET_S101_BASE_CELL</c>,
/// <c>ENCDOTNET_S57_BASE_CELL</c>, <c>ENC_DOTNET_PERF_REAL_S101</c> (a
/// cell each) and <c>ENCDOTNET_S57_EXCHANGE_SET</c> (every <c>.000</c> cell
/// under it).
/// </para>
/// </remarks>
public sealed class LuaEngineDifferentialTests
{
    private static readonly string[] RealCellVariables =
        ["ENCDOTNET_S101_BASE_CELL", "ENCDOTNET_S57_BASE_CELL", "ENC_DOTNET_PERF_REAL_S101"];

    private const string RealExchangeSetVariable = "ENCDOTNET_S57_EXCHANGE_SET";

    public static TheoryData<string> CorpusCells
    {
        get
        {
            var root = DatasetsRoot();
            var cells = new TheoryData<string>();
            foreach (var path in EnumerateCorpus(root))
            {
                cells.Add(Path.GetRelativePath(root, path).Replace('\\', '/'));
            }

            return cells;
        }
    }

    [Theory]
    [MemberData(nameof(CorpusCells))]
    public async Task CorpusCell_PortraysTheSameOnBothEngines(string relativePath)
    {
        var path = Path.Combine(DatasetsRoot(), relativePath);
        await AssertSameOutputAsync(path, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task InlandCell_PortraysTheSameOnBothEngines()
    {
        // S-401 through the inland ENC catalogue: the committed NOAA fixture
        // with its DSID product specification set to inland ENC.
        var dir = Directory.CreateTempSubdirectory("lua-diff-inland-").FullName;
        try
        {
            var path = SyntheticS57Cell.Write(
                dir, "U37TEST.000", Datasets.S57.S57ProductSpecification.InlandElectronicNavigationalChart);
            await AssertSameOutputAsync(path, TestContext.Current.CancellationToken);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(RealCells))]
    public async Task RealCell_PortraysTheSameOnBothEngines(string variable, string path)
    {
        Assert.SkipUnless(path.Length > 0 && File.Exists(path), $"{variable} is not set to a cell.");
        await AssertSameOutputAsync(path, TestContext.Current.CancellationToken);
    }

    public static TheoryData<string, string> RealCells
    {
        get
        {
            var cells = new TheoryData<string, string>();
            foreach (var variable in RealCellVariables)
            {
                cells.Add(variable, Environment.GetEnvironmentVariable(variable) ?? "");
            }

            var exchangeSet = Environment.GetEnvironmentVariable(RealExchangeSetVariable);
            if (!string.IsNullOrEmpty(exchangeSet) && Directory.Exists(exchangeSet))
            {
                foreach (var cell in Directory.EnumerateFiles(exchangeSet, "*.000", SearchOption.AllDirectories).Order())
                {
                    cells.Add(RealExchangeSetVariable, cell);
                }
            }
            else
            {
                cells.Add(RealExchangeSetVariable, "");
            }

            return cells;
        }
    }

    /// <summary>
    /// The one difference the stable MoonSharp sort makes in the committed
    /// corpus: a sectored light whose sectors share a range lists its colours
    /// as reference Lua sorts them on Lua-CSharp (<c>WRY</c>) and in source
    /// order on MoonSharp (<c>RWY</c>).
    /// </summary>
    [Fact]
    public async Task MoonSharpStableSort_ReordersOneLightDescription()
    {
        var path = Path.Combine(DatasetsRoot(), "S101", "S-101", "DATASET_FILES", "101AA00DS0019.000");
        Assert.SkipUnless(File.Exists(path), "S-101 fixture 101AA00DS0019.000 not present.");
        var cancellationToken = TestContext.Current.CancellationToken;

        var luaCSharp = await PortrayAsync(path, LuaEngineKind.LuaCSharp, cancellationToken);
        var moonSharp = await PortrayAsync(path, LuaEngineKind.MoonSharp, cancellationToken, referenceSort: false);

        var differing = luaCSharp.Output.Keys
            .Where(f => !luaCSharp.Output[f].SequenceEqual(moonSharp.Output.GetValueOrDefault(f) ?? [], StringComparer.Ordinal))
            .ToList();
        Assert.Equal(["72"], differing);
        Assert.All(luaCSharp.Output["72"], item => Assert.Contains("TextInstruction:Q WRY 25m15/6M", item, StringComparison.Ordinal));
        Assert.All(moonSharp.Output["72"], item => Assert.Contains("TextInstruction:Q RWY 25m15/6M", item, StringComparison.Ordinal));
    }

    private static async Task AssertSameOutputAsync(string path, CancellationToken cancellationToken)
    {
        var luaCSharp = await PortrayAsync(path, LuaEngineKind.LuaCSharp, cancellationToken);
        var moonSharp = await PortrayAsync(path, LuaEngineKind.MoonSharp, cancellationToken);

        Assert.SkipWhen(
            luaCSharp.Output.Count == 0 && moonSharp.Output.Count == 0 && luaCSharp.Error is null && moonSharp.Error is null,
            $"{Path.GetFileName(path)} runs no Lua portrayal.");

        var cell = Path.GetFileName(path);
        Assert.True(
            (luaCSharp.Error is null) == (moonSharp.Error is null),
            $"{cell}: only one engine failed. Lua-CSharp: {luaCSharp.Error?.Message ?? "ok"}; " +
            $"MoonSharp: {moonSharp.Error?.Message ?? "ok"}");

        var features = luaCSharp.Output.Keys.Union(moonSharp.Output.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var differences = new List<string>();
        foreach (var feature in features)
        {
            var a = luaCSharp.Output.GetValueOrDefault(feature) ?? [];
            var b = moonSharp.Output.GetValueOrDefault(feature) ?? [];
            if (!a.SequenceEqual(b, StringComparer.Ordinal))
            {
                differences.Add(
                    $"feature {feature}: Lua-CSharp emitted {a.Count} [{string.Join(" | ", a)}], " +
                    $"MoonSharp emitted {b.Count} [{string.Join(" | ", b)}]");
            }
        }

        Assert.True(
            differences.Count == 0,
            $"{cell}: the engines' portrayal differs for {differences.Count} feature(s):\n" +
            string.Join("\n", differences.Take(20)));

        // The whole-cell hash, so a run's output can be compared to the spike's.
        Assert.Equal(Hash(luaCSharp.Output), Hash(moonSharp.Output));
    }

    private static async Task<(Dictionary<string, List<string>> Output, Exception? Error)> PortrayAsync(
        string path, LuaEngineKind kind, CancellationToken cancellationToken, bool referenceSort = true)
    {
        var engine = new RecordingLuaEngine(
            TestLuaEngine.Create(kind),
            kind == LuaEngineKind.MoonSharp && referenceSort ? ReferenceLuaSort.Install : null);
        using var catalogues = CreateCatalogueManager();
        var factory = new DatasetPipelineFactory(
            catalogues,
            engine,
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new DisplayPlaneAuthorityProvider());

        var processor = factory.CreateProcessor(path);
        Exception? error = null;
        try
        {
            if (processor is IVectorPortrayalSource vector)
            {
                await vector.BuildVectorPortrayalAsync(cancellationToken: cancellationToken);
            }

            if (processor is ICoveragePortrayalSource coverage)
            {
                await coverage.BuildCoveragePortrayalAsync(cancellationToken: cancellationToken);
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Lua portrayal failed", StringComparison.Ordinal))
        {
            // The rules failed; compare how far each engine got.
            error = ex;
        }
        finally
        {
            (processor as IDisposable)?.Dispose();
        }

        return (engine.Emitted, error);
    }

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
            {
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
            }
        }

        return manager;
    }

    private static string Hash(Dictionary<string, List<string>> emitted)
    {
        var sb = new StringBuilder();
        foreach (var (feature, items) in emitted.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            foreach (var item in items)
            {
                sb.Append(feature).Append('\u001f').Append(item).Append('\n');
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static IEnumerable<string> EnumerateCorpus(string root)
    {
        string[] patterns =
        [
            "S101/**/*.000",
            "S57/**/*.000",
            "S131/*.gml",
            "S125/*.gml",
            "S102/*.h5",
        ];

        foreach (var pattern in patterns)
        {
            var slash = pattern.IndexOf('/', StringComparison.Ordinal);
            var dir = Path.Combine(root, pattern[..slash]);
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var recursive = pattern.Contains("**", StringComparison.Ordinal);
            var filePattern = pattern[(pattern.LastIndexOf('/') + 1)..];
            foreach (var file in Directory.EnumerateFiles(
                         dir, filePattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).Order())
            {
                yield return file;
            }
        }
    }

    private static string DatasetsRoot([CallerFilePath] string callerFilePath = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(callerFilePath)!, "..", "datasets"));

    /// <summary>
    /// Wraps an engine so every emit the rules make is recorded, keyed by
    /// feature reference, as "instruction string ‖ observed parameters".
    /// </summary>
    private sealed class RecordingLuaEngine(ILuaEngine inner, string? prelude) : ILuaEngine
    {
        private readonly Lock _gate = new();

        public Dictionary<string, List<string>> Emitted { get; } = new(StringComparer.Ordinal);

        public ILuaContext CreateContext()
        {
            var context = inner.CreateContext();
            if (prelude is not null)
            {
                context.Execute(prelude);
            }

            return new RecordingContext(context, this);
        }

        private void Record(string feature, string item)
        {
            lock (_gate)
            {
                if (!Emitted.TryGetValue(feature, out var items))
                {
                    items = [];
                    Emitted[feature] = items;
                }

                items.Add(item);
            }
        }

        private sealed class RecordingContext(ILuaContext inner, RecordingLuaEngine owner) : ILuaContext
        {
            public void Execute(string source) => inner.Execute(source);

            public void SetModuleLoader(Func<string, string?> loader) => inner.SetModuleLoader(loader);

            public void SetGlobal(string name, object? value)
            {
                value = (name, value) switch
                {
                    ("HostPortrayalEmit", Func<string, string, string, bool> emit) =>
                        (Func<string, string, string, bool>)((feature, instructions, observed) =>
                        {
                            owner.Record(feature, $"{instructions} ‖ {observed}");
                            return emit(feature, instructions, observed);
                        }),
                    ("_addInstructions", Action<string> add) =>
                        (Action<string>)(instructions =>
                        {
                            owner.Record("coverage", instructions);
                            add(instructions);
                        }),
                    _ => value,
                };
                inner.SetGlobal(name, value);
            }

            public object? GetGlobal(string name) => inner.GetGlobal(name);

            public object? Call(string functionName, params object?[] args) => inner.Call(functionName, args);

            public object? CallCancellable(string functionName, CancellationToken cancellationToken, params object?[] args) =>
                inner.CallCancellable(functionName, cancellationToken, args);

            public object?[] CallMultiReturn(string functionName, params object?[] args) =>
                inner.CallMultiReturn(functionName, args);

            public string DescribeError(Exception exception) => inner.DescribeError(exception);

            public void Dispose() => inner.Dispose();
        }
    }
}
