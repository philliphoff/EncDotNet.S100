using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for issue #450: an S-57 dataset is portrayed with the
/// S-101 catalogue but must still honor the ECDIS display state threaded on
/// the render context. Before the fix <see cref="S57DatasetProcessor"/> never
/// called <c>EcdisDisplayExtensions.ApplyTo</c>, so every feature rendered
/// regardless of the selected display category.
/// </summary>
public class S57DatasetProcessorEcdisFilterTests
{
    private const string FixtureFile = "US5MA1BO.000";

    private static int InstructionCount(EncDotNet.S100.Datasets.Pipelines.Portrayal.VectorPortrayalResult result)
        => result.SubLayers.Sum(s => s.Instructions.Count);

    [Fact]
    public async Task DisplayCategory_FiltersInstructions_Monotonically()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Assert.SkipUnless(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var luaEngine = new MoonSharpLuaEngine();
        var catalogueManager = new PortrayalCatalogueManager();
        catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var featureCatalogueManager = new FeatureCatalogueManager(
            spec => Specification.TryOpenFeatureCatalogue(spec));

        var processor = new S57DatasetProcessor(
            fixturePath, catalogueManager, luaEngine, featureCatalogueManager);

        var all = InstructionCount(await processor.BuildVectorPortrayalAsync(
            new RenderContext { EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.All } },
            TestContext.Current.CancellationToken));
        var standard = InstructionCount(await processor.BuildVectorPortrayalAsync(
            new RenderContext { EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.Standard } },
            TestContext.Current.CancellationToken));
        var displayBase = InstructionCount(await processor.BuildVectorPortrayalAsync(
            new RenderContext { EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.DisplayBase } },
            TestContext.Current.CancellationToken));

        // Narrowing the display category must never add instructions. Before
        // the fix all three were identical (the unfiltered set), so this holds
        // trivially; the strict check below proves filtering is actually applied.
        Assert.True(displayBase <= standard, $"DisplayBase ({displayBase}) > Standard ({standard})");
        Assert.True(standard <= all, $"Standard ({standard}) > All ({all})");

        // A real ENC cell carries non-base content, so DisplayBase must drop at
        // least one instruction relative to All. If the cell happened to be
        // base-only, skip rather than fail.
        Assert.SkipWhen(displayBase == all,
            "Fixture has only Display Base content; cannot observe category filtering.");
        Assert.True(displayBase < all,
            $"Expected DisplayBase ({displayBase}) to filter out content present in All ({all}).");
    }

    [Fact]
    public async Task DisplayStateChange_RefiltersCachedRuleOutput_WithoutRerunningRules()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Assert.SkipUnless(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var ct = TestContext.Current.CancellationToken;
        var processor = CreateProcessor(fixturePath);

        // Display category, viewing groups and display planes only filter the
        // rule output, so toggling them must not re-run the Lua rules.
        var hiddenVg = new EcdisDisplaySettings
        {
            Category = EcdisDisplayCategory.All,
            HiddenViewingGroups = new Dictionary<string, IReadOnlySet<int>> { ["S-101"] = new HashSet<int> { 27010 } },
        };
        var settings = new[]
        {
            new EcdisDisplaySettings { Category = EcdisDisplayCategory.All },
            new EcdisDisplaySettings { Category = EcdisDisplayCategory.DisplayBase },
            hiddenVg,
            new EcdisDisplaySettings
            {
                Category = EcdisDisplayCategory.Standard,
                HiddenDisplayPlanes = new HashSet<EncDotNet.S100.Pipelines.Vector.DisplayPlane> { EncDotNet.S100.Pipelines.Vector.DisplayPlane.OverRadar },
            },
        };

        var cachedCounts = new List<int>();
        foreach (var ecdis in settings)
        {
            cachedCounts.Add(InstructionCount(await processor.BuildVectorPortrayalAsync(
                new RenderContext { EcdisDisplay = ecdis }, ct)));
        }

        Assert.Equal(1, processor.RuleRunCount);

        // Each re-filtered result matches a fresh processor rendering that
        // display state from scratch.
        for (var i = 0; i < settings.Length; i++)
        {
            var fresh = InstructionCount(await CreateProcessor(fixturePath).BuildVectorPortrayalAsync(
                new RenderContext { EcdisDisplay = settings[i] }, ct));
            Assert.Equal(fresh, cachedCounts[i]);
        }

        Assert.True(cachedCounts[1] < cachedCounts[0], "DisplayBase should filter out content present in All.");

        // A mariner setting is a rule input, so changing it re-runs the rules.
        await processor.BuildVectorPortrayalAsync(
            new RenderContext
            {
                EcdisDisplay = settings[0],
                Mariner = MarinerSettings.Default with { SimplifiedSymbols = !MarinerSettings.Default.SimplifiedSymbols },
            },
            ct);
        Assert.Equal(2, processor.RuleRunCount);
    }

    private static S57DatasetProcessor CreateProcessor(string fixturePath)
    {
        var catalogueManager = new PortrayalCatalogueManager();
        catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return new S57DatasetProcessor(
            fixturePath,
            catalogueManager,
            new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)));
    }

    private static string ResolveFixturePath(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return Path.Combine("tests", "datasets", "S57", "US5MA1BO", fileName);
    }
}
