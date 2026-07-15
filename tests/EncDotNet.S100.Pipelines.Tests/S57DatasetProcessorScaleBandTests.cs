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
/// <c>CellMinimumDisplayScale</c>, so the standalone-loaded cell never
/// disappeared when zoomed out beyond its compilation scale — unlike an
/// exchange-set cell driven by <c>CATALOG.XML</c>. The fix derives the
/// whole-cell zoom-out window from the S-57 DSPM compilation scale (CSCL).
///
/// S-57 deliberately does <b>not</b> apply the per-feature out-of-band cap
/// (<c>OutOfBandMinDisplayScale</c> / <c>ApplyOutOfBandCap</c>): CSCL is always
/// present, so a per-feature cap would silently blank the whole cell (no extent
/// border) at any whole-cell-fit view. The whole-cell window handles zoom-out
/// suppression with a border instead.
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
    public async Task BuildVectorPortrayal_DerivesWholeCellWindow_FromCompilationScale()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Skip.IfNot(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var processor = CreateProcessor(fixturePath);

        var result = await processor.BuildVectorPortrayalAsync(new S101RenderContext());

        // The ungated whole-cell window carries the compilation scale so the
        // viewer can hide the whole cell (extent border included) when zoomed
        // out, matching an exchange-set-loaded cell (a real ENC cell always
        // declares CSCL > 0).
        Assert.NotNull(result.CellMinimumDisplayScale);
        Assert.True(result.CellMinimumDisplayScale > 0,
            $"Expected a positive whole-cell denominator, got {result.CellMinimumDisplayScale}.");

        // The per-feature out-of-band cap is deliberately NOT applied for S-57
        // (it would blank the whole cell with no placeholder — the whole-cell
        // window does the job with an extent border instead).
        Assert.Null(result.OutOfBandMinDisplayScale);
        var subLayer = Assert.Single(result.SubLayers);
        Assert.False(subLayer.ApplyOutOfBandCap,
            "The s57.main sub-layer must not carry the per-feature out-of-band cap.");
    }

    [SkippableFact]
    public async Task BuildVectorPortrayal_WholeCellWindow_IsUngated()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Skip.IfNot(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var processor = CreateProcessor(fixturePath);

        var result = await processor.BuildVectorPortrayalAsync(new S101RenderContext
        {
            Mariner = new MarinerSettings { IgnoreScaleMinimum = true },
        });

        // The whole-cell window is ungated: it still reports the cell's scale
        // even under IgnoreScaleMinimum (the viewer applies its own gate). The
        // per-feature cap remains unused regardless.
        Assert.Null(result.OutOfBandMinDisplayScale);
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
