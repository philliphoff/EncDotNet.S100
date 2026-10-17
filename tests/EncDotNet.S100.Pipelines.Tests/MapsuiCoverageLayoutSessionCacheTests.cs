using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Issue #855: the map session builds a fresh <see cref="MapsuiCoverageRenderer"/>
/// per render, so the grid projection layout must live on the dataset's
/// session entry for re-renders of the same grid to reuse it.
/// </summary>
public sealed class MapsuiCoverageLayoutSessionCacheTests
{
    private const string S102File = "TestData/102US004MI1CI262227.h5";

    private static readonly ProjNetCrsTransformFactory Factory = new();

    [Fact]
    public async Task S102ReRenders_ReuseTheDatasetsLayout()
    {
        using var manager = CreateCatalogueManager();
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = AddS102(session, owner, manager, "bathymetry");
        var ct = TestContext.Current.CancellationToken;

        // A palette switch and a display-mode switch re-portray the dataset
        // with the same grid geometry.
        await session.RenderAsync(id, Presentation(PaletteType.Day), ct);
        await session.RenderAsync(id, Presentation(PaletteType.Dusk), ct);
        await session.RenderAsync(id, Presentation(PaletteType.Night), ct);

        var cache = session.GetCoverageLayoutCache(id);
        Assert.NotNull(cache);
        Assert.Equal(1, cache.Misses);
        Assert.Equal(2, cache.Hits);
    }

    [Fact]
    public async Task EachDatasetOwnsItsLayout_AndRemovalReleasesIt()
    {
        using var manager = CreateCatalogueManager();
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        // The processor names its stack items after the file, so the second
        // dataset needs a file of its own.
        var copy = Path.Combine(Path.GetTempPath(), $"102US004MI1CI262227-{Guid.NewGuid():N}.h5");
        File.Copy(S102File, copy);
        try
        {
            var first = AddS102(session, owner, manager, "first");
            var second = AddS102(session, owner, manager, "second", copy);
            var ct = TestContext.Current.CancellationToken;

            await session.RenderAsync(first, Presentation(PaletteType.Day), ct);
            await session.RenderAsync(second, Presentation(PaletteType.Day), ct);
            await session.RenderAsync(first, Presentation(PaletteType.Dusk), ct);

            var firstCache = session.GetCoverageLayoutCache(first)!;
            var secondCache = session.GetCoverageLayoutCache(second)!;
            Assert.NotSame(firstCache, secondCache);
            Assert.Equal((1L, 1L), (firstCache.Misses, firstCache.Hits));
            Assert.Equal((1L, 0L), (secondCache.Misses, secondCache.Hits));

            Assert.True(session.RemoveDataset(first));
            Assert.Null(session.GetCoverageLayoutCache(first));
            Assert.True(session.RemoveDataset(second));
        }
        finally
        {
            File.Delete(copy);
        }
    }

    [Fact]
    public async Task S98MaskRebuild_ReusesTheLayoutOfTheSameGrid()
    {
        using var manager = CreateCatalogueManager();
        using var processor = new S102DatasetProcessor(S102File, manager, new MoonSharpLuaEngine(), Factory);
        var portrayal = await processor.BuildCoveragePortrayalAsync(
            cancellationToken: TestContext.Current.CancellationToken);
        var grid = Assert.IsType<GridCoverageSubLayer>(Assert.Single(portrayal.SubLayers));
        var renderer = new MapsuiDatasetRenderer(Factory);
        var cache = new CoverageLayoutCache();

        Assert.NotNull(renderer.BuildGridCoverageLayer(grid, cache));
        // An S-98 rule replaces the sub-layer with a masked copy of the same grid.
        Assert.NotNull(renderer.BuildCoverageLayer(grid.WithLandAreaMask([]), cache));

        Assert.Equal((1L, 1L), (cache.Misses, cache.Hits));
    }

    private static MapDatasetId AddS102(
        MapsuiDatasetLayerSession session,
        DatasetProcessorOwner owner,
        PortrayalCatalogueManager manager,
        string name,
        string path = S102File)
    {
        var id = new MapDatasetId(name);
        var processor = new S102DatasetProcessor(path, manager, new MoonSharpLuaEngine(), Factory);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(new MapDataset(id, name, processor.Metadata));
        return id;
    }

    private static MapPresentationState Presentation(PaletteType palette) =>
        new(palette, 1.0, 1.0, new EcdisDisplaySettings(), MarinerSettings.Default);

    private static MapsuiDatasetLayerSession CreateSession(Map map, DatasetProcessorOwner owner) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(Factory),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));

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
}
