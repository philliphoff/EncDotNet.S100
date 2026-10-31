using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// The cross-load portrayal-instruction cache for S-57 cells: a fresh
/// <see cref="S57DatasetProcessor"/> for a cell portrayed before (a restart, or
/// a cell the map loads again as it pans) serves its rule output from the
/// shared cache instead of re-running the Part 9A Lua rules, and draws the
/// same instructions.
/// </summary>
public class S57PortrayalInstructionCacheProcessorTests
{
    private const string FixtureFile = "US5MA1BO.000";

    [Fact]
    public async Task WarmDiskCache_SecondColdOpen_SkipsRulesAndReproducesInstructions()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Assert.SkipUnless(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var ct = TestContext.Current.CancellationToken;
        var cacheDir = Path.Combine(
            Path.GetTempPath(), "encdotnet-s57-dlistcache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDir);
        try
        {
            var first = CreateProcessor(fixturePath, new DiskPortrayalInstructionCache(cacheDir, maxBytes: 256L * 1024 * 1024));
            var firstResult = await first.BuildVectorPortrayalAsync(cancellationToken: ct);
            Assert.Equal(1, first.RuleRunCount);
            Assert.Equal(0, first.SharedInstructionCacheHits);

            // A new cache over the same folder stands in for a restart.
            var second = CreateProcessor(fixturePath, new DiskPortrayalInstructionCache(cacheDir, maxBytes: 256L * 1024 * 1024));
            var secondResult = await second.BuildVectorPortrayalAsync(cancellationToken: ct);
            Assert.Equal(0, second.RuleRunCount);
            Assert.Equal(1, second.SharedInstructionCacheHits);

            Assert.Equal(firstResult.SubLayers.Count, secondResult.SubLayers.Count);
            for (var i = 0; i < firstResult.SubLayers.Count; i++)
            {
                Assert.Equal(
                    DrawingInstructionSerializer.Serialize(firstResult.SubLayers[i].Instructions),
                    DrawingInstructionSerializer.Serialize(secondResult.SubLayers[i].Instructions));
            }
        }
        finally
        {
            try { Directory.Delete(cacheDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task SharedCache_MarinerChange_RunsRulesAgain()
    {
        var fixturePath = ResolveFixturePath(FixtureFile);
        Assert.SkipUnless(File.Exists(fixturePath),
            $"S-57 fixture not found at expected path: {fixturePath}");

        var ct = TestContext.Current.CancellationToken;
        var cache = new InMemoryPortrayalInstructionCache();
        await CreateProcessor(fixturePath, cache).BuildVectorPortrayalAsync(cancellationToken: ct);

        // The mariner settings are a rule input, so another processor rendering
        // them differently must not reuse the cached output.
        var other = CreateProcessor(fixturePath, cache);
        await other.BuildVectorPortrayalAsync(
            new RenderContext
            {
                Mariner = MarinerSettings.Default with { SimplifiedSymbols = !MarinerSettings.Default.SimplifiedSymbols },
            },
            ct);

        Assert.Equal(1, other.RuleRunCount);
        Assert.Equal(0, cache.Hits);
        Assert.Equal(2, cache.Misses);
    }

    private static S57DatasetProcessor CreateProcessor(string fixturePath, IPortrayalInstructionCache cache)
    {
        var catalogueManager = new PortrayalCatalogueManager();
        catalogueManager.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return new S57DatasetProcessor(
            fixturePath,
            catalogueManager,
            new MoonSharpLuaEngine(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)),
            cache);
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
