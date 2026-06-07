using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Processor-level coverage of the persisted (disk-backed) pattern-clip cache:
/// with a warm <see cref="DiskPatternClipCache"/>, the <em>second</em> cold open
/// of a cell serves its pattern-fill priority clip from disk (a clip-cache hit),
/// while the first cold open recomputes it. Requires a real S-101 trial cell and
/// is skipped when absent so CI stays green.
/// </summary>
public class S101DiskPatternClipCacheProcessorTests
{
    // The densest known real S-101 trial cell (~64k-vertex M_QUAL coverage),
    // never committed (real ENC data is not checked in). Present only locally.
    private static string DenseCellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "Complete S10X datasets", "S-101 Trial Cells",
        "101GB00GB302045.000");

    private static PortrayalCatalogueManager CreateCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }
        return manager;
    }

    [SkippableFact]
    public async Task WarmDiskCache_SecondColdOpen_IsClipCacheHit()
    {
        Skip.IfNot(File.Exists(DenseCellPath), $"Dense S-101 trial cell not present: {DenseCellPath}");

        var cacheDir = Path.Combine(
            Path.GetTempPath(), "encdotnet-clipcache-proc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        try
        {
            var sharedCache = new DiskPatternClipCache(cacheDir, maxBytes: 256L * 1024 * 1024);

            var catalogueManager = CreateCatalogueManager();
            var fcManager = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);
            var factory = new DatasetPipelineFactory(
                catalogueManager,
                new MoonSharpLuaEngine(),
                new ProjNetCrsTransformFactory(),
                fcManager,
                new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthorityProvider(
                    new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthority()),
                sharedCache);

            // First cold open: the disk cache is empty, so the clip is computed
            // (a miss) and persisted. This processor sees no clip-cache hits.
            var first = (S101DatasetProcessor)factory.CreateProcessor(DenseCellPath);
            await first.RenderAsync();
            Assert.Equal(0, first.PatternClipCacheHits);

            // Second cold open (simulates reopening the cell, even after a
            // restart): a brand-new processor over the same shared disk cache.
            // Its area render must be served from disk — a clip-cache hit — so
            // the multi-second NetTopologySuite overlay is skipped.
            var second = (S101DatasetProcessor)factory.CreateProcessor(DenseCellPath);
            await second.RenderAsync();
            Assert.True(
                second.PatternClipCacheHits >= 1,
                "Second cold open should hit the warm disk pattern-clip cache.");
        }
        finally
        {
            try { Directory.Delete(cacheDir, recursive: true); }
            catch { /* best effort */ }
        }
    }
}
