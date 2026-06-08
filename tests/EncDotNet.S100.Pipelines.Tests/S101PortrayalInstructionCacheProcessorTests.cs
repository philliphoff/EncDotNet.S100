using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Processor-level coverage of the cross-load portrayal-instruction cache: with
/// a warm shared <see cref="DiskPortrayalInstructionCache"/>, the <em>second</em>
/// cold open of a cell (a brand-new processor — simulating reopening the cell or
/// restarting) serves its prepared display list from the cache, skipping the
/// multi-second MoonSharp Part 9A Lua run, and reproduces the same instruction
/// list. Requires a real S-101 trial cell and is skipped when absent so CI stays
/// green.
/// </summary>
public class S101PortrayalInstructionCacheProcessorTests
{
    // Real S-101 trial cells are never committed; present only locally. Pick the
    // first available cell so the test runs wherever a sample is installed.
    private static string? FindCell()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads", "Complete S10X datasets", "S-101 Trial Cells");
        if (!Directory.Exists(dir))
            return null;
        return Directory.EnumerateFiles(dir, "*.000", SearchOption.AllDirectories)
            .OrderBy(static p => p, StringComparer.Ordinal)
            .FirstOrDefault();
    }

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

    private static DatasetPipelineFactory CreateFactory(IPortrayalInstructionCache instructionCache) =>
        new(
            CreateCatalogueManager(),
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthorityProvider(
                new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthority()),
            sharedPatternClipCache: null,
            sharedInstructionCache: instructionCache);

    [SkippableFact]
    public async Task WarmDiskCache_SecondColdOpen_ReusesPreparedInstructions()
    {
        var cell = FindCell();
        Skip.If(cell is null, "No S-101 trial cell present.");

        var cacheDir = Path.Combine(
            Path.GetTempPath(), "encdotnet-dlistcache-proc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        try
        {
            var sharedCache = new DiskPortrayalInstructionCache(cacheDir, maxBytes: 256L * 1024 * 1024);

            // First cold open: the cache is empty, so the Lua pipeline runs (a
            // miss) and the prepared list is persisted. This fresh processor sees
            // no shared-cache hits.
            var firstFactory = CreateFactory(sharedCache);
            var first = (S101DatasetProcessor)firstFactory.CreateProcessor(cell!);
            var firstResult = await first.RenderAsync();
            Assert.Equal(0, first.SharedInstructionCacheHits);
            Assert.Equal(1, sharedCache.Misses);

            // Second cold open: a brand-new processor (and a brand-new factory,
            // so nothing is shared except the on-disk cache) re-opening the same
            // cell. Its render must be served from the warm cache — a hit — so
            // the MoonSharp Part 9A Lua run is skipped.
            var secondFactory = CreateFactory(sharedCache);
            var second = (S101DatasetProcessor)secondFactory.CreateProcessor(cell!);
            var secondResult = await second.RenderAsync();

            Assert.True(
                second.SharedInstructionCacheHits >= 1,
                "Second cold open should hit the warm disk portrayal-instruction cache.");

            // The cached list reproduces the same display list: the Info string
            // embeds the instruction count, so equal Info ⇒ equal instruction
            // count from the cache vs. a fresh Lua run.
            Assert.Equal(firstResult.Info, secondResult.Info);
        }
        finally
        {
            try { Directory.Delete(cacheDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [SkippableFact]
    public async Task DefaultProcessor_FallsBackToInMemoryCache_AndRenders()
    {
        var cell = FindCell();
        Skip.If(cell is null, "No S-101 trial cell present.");

        // Construct the processor directly via the public path ctor WITHOUT a
        // shared instruction cache, so it falls back to its own bounded in-memory
        // instruction cache. This asserts the fallback path renders successfully.
        var processor = new S101DatasetProcessor(
            cell!,
            CreateCatalogueManager(),
            new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue));
        var result = await processor.RenderAsync();

        Assert.NotNull(result);
        Assert.NotEmpty(result.Layers);
    }
}
