using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.Services.LazyLoading;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="ExchangeSetCellPortrayer"/> (#1266): rendering a registered cell
/// in the background stores its portrayal and pattern clip under the keys the
/// map's own load of the cell asks for, so that load runs neither Lua nor the clip.
/// </summary>
public sealed class ExchangeSetCellPortrayerTests : IDisposable
{
    private readonly string _cacheDir = Path.Combine(Path.GetTempPath(), "encdotnet-warm-cache-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_cacheDir, recursive: true); }
        catch { /* best effort */ }
    }

    private static DatasetPipelineFactory Factory(IPortrayalInstructionCache cache)
    {
        var catalogues = new PortrayalCatalogueManager();
        catalogues.SetSource("S-101", Specification.CreatePortrayalCatalogueSource("S-101"));
        return new DatasetPipelineFactory(
            catalogues,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(spec => Specification.TryOpenFeatureCatalogue(spec)),
            new DisplayPlaneAuthorityProvider(),
            cache);
    }

    private static string? FixtureFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S57", "US5MA1BO");
            if (File.Exists(Path.Combine(candidate, "US5MA1BO.000"))) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public async Task BackgroundRender_MakesTheMapsLoadOfTheCellHitBothCaches()
    {
        var folder = FixtureFolder();
        Assert.SkipUnless(folder is not null, "S-57 fixture US5MA1BO not found.");
        var ct = TestContext.Current.CancellationToken;
        using var source = EncDotNet.S100.Core.FileSystemAssetSource.Create(folder!);
        var entry = new DatasetEntry(
            filePath: "US5MA1BO.000", productSpec: "S-57", source: source,
            relativePath: "US5MA1BO.000", displayName: "US5MA1BO");
        var presentation = MapPresentationState.Default.WithMariner(
            MarinerSettings.Default with { SimplifiedSymbols = true });
        var instructionDir = Path.Combine(_cacheDir, "dlist");
        var clipDir = Path.Combine(_cacheDir, "clip");
        var recorder = new PortrayalWarmRecorder(
            new DiskPortrayalInstructionCache(instructionDir, maxBytes: 64L * 1024 * 1024),
            new DiskPatternClipCache(clipDir, maxBytes: 64L * 1024 * 1024));
        var portrayer = new ExchangeSetCellPortrayer(
            Factory(recorder.InstructionCache),
            new MapsuiDatasetRenderer(new ProjNetCrsTransformFactory(), recorder.PatternClipCache),
            recorder,
            () => presentation);

        var keys = await portrayer.PortrayAsync(entry, ct);

        Assert.NotNull(keys);
        Assert.Contains(keys, k => k.StartsWith("dlist:", StringComparison.Ordinal));
        var clipKeys = keys.Count(k => k.StartsWith("clip:", StringComparison.Ordinal));
        Assert.All(keys, k => Assert.True(recorder.IsCached(k), k));

        // The map's load: the loader's own processor path, its own factory,
        // Lua engine and renderer, over the same disk caches (fresh instances,
        // as after a restart).
        var mapInstructions = new DiskPortrayalInstructionCache(instructionDir, maxBytes: 64L * 1024 * 1024);
        var mapClips = new DiskPatternClipCache(clipDir, maxBytes: 64L * 1024 * 1024);
        var processor = DatasetLoaderService.CreateExchangeSetCellProcessor(
            Factory(mapInstructions), source, "US5MA1BO.000", [], "S-57");
        await new MapsuiDatasetRenderer(new ProjNetCrsTransformFactory(), mapClips).RenderAsync(
            processor, presentation.CreateRenderContext(processor), ct);

        Assert.Equal(0, mapInstructions.Misses);
        Assert.Equal(1, mapInstructions.Hits);
        // Every pattern clip the map's render needs was clipped in the background
        // (the fixture may draw none, in which case neither render clips).
        Assert.Equal(0, mapClips.Misses);
        Assert.Equal(clipKeys, mapClips.Hits);
    }

    [Fact]
    public async Task Recorder_NotesOnlyTheKeysOfItsOwnScope()
    {
        var recorder = new PortrayalWarmRecorder(
            new DiskPortrayalInstructionCache(Path.Combine(_cacheDir, "dlist"), maxBytes: 1024 * 1024),
            new DiskPatternClipCache(Path.Combine(_cacheDir, "clip"), maxBytes: 1024 * 1024));
        var cache = recorder.InstructionCache;
        IReadOnlyList<DrawingInstruction> empty = [];

        cache.GetOrCompute("outside", () => empty);
        var first = Task.Run(async () =>
        {
            using var scope = recorder.BeginRecording();
            await cache.GetOrComputeAsync("a", _ => ValueTask.FromResult(empty));
            await Task.Yield();
            cache.GetOrCompute("a", () => empty);
            recorder.PatternClipCache.GetOrCompute("b", () => []);
            return scope.Keys;
        });
        var second = Task.Run(() =>
        {
            using var scope = recorder.BeginRecording();
            cache.GetOrCompute("c", () => empty);
            return scope.Keys;
        });

        Assert.Equal(["dlist:a", "clip:b"], await first);
        Assert.Equal(["dlist:c"], await second);
        Assert.True(recorder.IsCached("dlist:a"));
        Assert.True(recorder.IsCached("clip:b"));
        Assert.False(recorder.IsCached("dlist:never"));
        Assert.False(recorder.IsCached("a"));
    }
}
