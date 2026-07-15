using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Regression tests for the S-57 out-of-scale-band declutter gap (follow-up to
/// issue #450). An S-57 cell is portrayed with the S-101 catalogue, but before
/// the fix <see cref="S57DatasetProcessor"/> emitted no
/// <c>OutOfBandMinDisplayScale</c> and left <c>ApplyOutOfBandCap</c> false, so
/// the cell never disappeared when zoomed out beyond its compilation scale —
/// unlike an S-101 cell driven by <c>DataCoverage</c> / <c>minimumDisplayScale</c>.
/// The fix derives the cap from the S-57 DSPM compilation scale (CSCL).
/// </summary>
public class S57DatasetProcessorScaleBandTests
{
    private const string FixtureFile = "US5MA1BO.000";

    private static S57DatasetProcessor CreateProcessor(string fixturePath)
    {
        var luaEngine = new MoonSharpLuaEngine();
        var catalogueManager = new PortrayalCatalogueManager();
        catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        var featureCatalogueManager = new FeatureCatalogueManager(
            spec => Specification.TryOpenFeatureCatalogue(spec));
        return new S57DatasetProcessor(
            fixturePath, catalogueManager, luaEngine, featureCatalogueManager);
    }

    [SkippableFact]
    public async Task BuildVectorPortrayal_DerivesOutOfBandCap_FromCompilationScale()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Skip.IfNot(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var processor = CreateProcessor(fixturePath);

        var result = await processor.BuildVectorPortrayalAsync(new S101RenderContext());

        // The cell must carry an out-of-band denominator derived from its
        // compilation scale (a real ENC cell always declares CSCL > 0).
        Assert.NotNull(result.OutOfBandMinDisplayScale);
        Assert.True(result.OutOfBandMinDisplayScale > 0,
            $"Expected a positive out-of-band denominator, got {result.OutOfBandMinDisplayScale}.");

        // The ungated whole-cell window carries the same compilation scale so
        // the viewer can hide the whole cell (extent border included) when
        // zoomed out, matching an exchange-set-loaded cell.
        Assert.Equal(result.OutOfBandMinDisplayScale, result.CellMinimumDisplayScale);

        // The single S-57 sub-layer must carry the declutter cap so the
        // renderer suppresses the cell out of band.
        var subLayer = Assert.Single(result.SubLayers);
        Assert.True(subLayer.ApplyOutOfBandCap,
            "Expected the s57.main sub-layer to carry the out-of-scale-band cap.");
    }

    [SkippableFact]
    public async Task BuildVectorPortrayal_HonorsIgnoreScaleMinimum()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Skip.IfNot(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var processor = CreateProcessor(fixturePath);

        var result = await processor.BuildVectorPortrayalAsync(new S101RenderContext
        {
            Mariner = new MarinerSettings { IgnoreScaleMinimum = true },
        });

        // The mariner override disables the per-feature cap, matching the S-101
        // path...
        Assert.Null(result.OutOfBandMinDisplayScale);
        // ...but the ungated whole-cell window still reports the cell's scale
        // (the viewer applies its own IgnoreScaleMinimum gate).
        Assert.NotNull(result.CellMinimumDisplayScale);
        Assert.True(result.CellMinimumDisplayScale > 0);
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
