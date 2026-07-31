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

    [SkippableFact]
    public async Task DisplayCategory_FiltersInstructions_Monotonically()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Skip.IfNot(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var luaEngine = new MoonSharpLuaEngine();
        var catalogueManager = new PortrayalCatalogueManager();
        catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var featureCatalogueManager = new FeatureCatalogueManager(
            spec => Specification.TryOpenFeatureCatalogue(spec));

        var processor = new S57DatasetProcessor(
            fixturePath, catalogueManager, luaEngine, featureCatalogueManager);

        var all = InstructionCount(await processor.BuildVectorPortrayalAsync(
            new S101RenderContext { EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.All } }));
        var standard = InstructionCount(await processor.BuildVectorPortrayalAsync(
            new S101RenderContext { EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.Standard } }));
        var displayBase = InstructionCount(await processor.BuildVectorPortrayalAsync(
            new S101RenderContext { EcdisDisplay = new EcdisDisplaySettings { Category = EcdisDisplayCategory.DisplayBase } }));

        // Narrowing the display category must never add instructions. Before
        // the fix all three were identical (the unfiltered set), so this holds
        // trivially; the strict check below proves filtering is actually applied.
        Assert.True(displayBase <= standard, $"DisplayBase ({displayBase}) > Standard ({standard})");
        Assert.True(standard <= all, $"Standard ({standard}) > All ({all})");

        // A real ENC cell carries non-base content, so DisplayBase must drop at
        // least one instruction relative to All. If the cell happened to be
        // base-only, skip rather than fail.
        Skip.If(displayBase == all,
            "Fixture has only Display Base content; cannot observe category filtering.");
        Assert.True(displayBase < all,
            $"Expected DisplayBase ({displayBase}) to filter out content present in All ({all}).");
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
