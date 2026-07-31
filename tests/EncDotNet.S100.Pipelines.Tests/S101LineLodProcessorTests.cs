using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Processor-level coverage of the pre-computed line LOD pyramid subsystem
/// (issue #489, PR-3). Opens a real S-101 trial cell twice through a shared
/// <see cref="InMemoryLineLodCache"/> to verify: (a) the first open primes
/// the cache with pyramids for every line feature (misses > 0); (b) the
/// second cold open of the same cell serves those pyramids from the shared
/// cache without recomputing Douglas–Peucker (hits > 0). Requires a real
/// S-101 dataset and is skipped when absent so CI stays green.
/// </summary>
public class S101LineLodProcessorTests
{
    // The densest known real S-101 trial cell, never committed. Present only
    // locally alongside developer downloads.
    private static string DenseCellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "Complete S10X datasets", "S-101 Trial Cells",
        "101GB00302045", "101GB00GB302045", "101GB00GB302045.000");

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

    private static DatasetPipelineFactory CreateFactory(ILineLodCache sharedLineLodCache)
    {
        var catalogueManager = CreateCatalogueManager();
        var fcManager = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);
        return new DatasetPipelineFactory(
            catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            fcManager,
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.DisplayPlaneAuthorityProvider(),
            sharedInstructionCache: null,
            sharedLineLodCache: sharedLineLodCache);
    }

    [SkippableFact]
    public void ProcessorOpen_PopulatesSharedLineLodCache_OnFirstOpenAndReusesOnSecond()
    {
        Skip.IfNot(File.Exists(DenseCellPath), $"Dense S-101 trial cell not present: {DenseCellPath}");

        var sharedCache = new InMemoryLineLodCache();
        var factory = CreateFactory(sharedCache);

        // First cold open — every line feature's Douglas–Peucker pyramid is
        // computed and stored. Miss count grows by (roughly) the number of
        // line features in the cell.
        _ = factory.CreateProcessor(DenseCellPath);
        var missesAfterFirst = sharedCache.Misses;
        var hitsAfterFirst = sharedCache.Hits;

        Assert.True(
            missesAfterFirst > 0,
            "First open must populate the shared line-LOD cache with at least one pyramid.");
        Assert.Equal(0, hitsAfterFirst);

        // Second cold open of the same dataset — brand-new processor, same
        // shared cache. Every pyramid must now be served from cache (hit),
        // and miss count must not grow.
        _ = factory.CreateProcessor(DenseCellPath);

        Assert.Equal(missesAfterFirst, sharedCache.Misses);
        Assert.Equal(missesAfterFirst, sharedCache.Hits);
    }

    [SkippableFact]
    public void ProcessorOpen_WithoutSharedCache_ProducesNoPyramids()
    {
        Skip.IfNot(File.Exists(DenseCellPath), $"Dense S-101 trial cell not present: {DenseCellPath}");

        // Same signature as the other test but with a null shared cache —
        // exercises the fallback path where the renderer's on-demand
        // simplification will be the only line-simplification producer.
        var catalogueManager = CreateCatalogueManager();
        var fcManager = new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue);
        var factory = new DatasetPipelineFactory(
            catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            fcManager,
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.DisplayPlaneAuthorityProvider());

        // Open should succeed without a shared LOD cache. There's no assertion
        // on cache state (there is no cache); the ctor completing without
        // throwing is the observable behaviour, mirroring PR-2's flag-off
        // path.
        var processor = factory.CreateProcessor(DenseCellPath);
        Assert.NotNull(processor);
    }
}
