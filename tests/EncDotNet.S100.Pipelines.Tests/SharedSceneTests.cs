using System.Diagnostics;
using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Specifications;
using EncDotNet.S100.TestSupport;
using Mapsui;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Maps that show the same portrayal share one scene and one portrayal
/// result: the engine derives a scene's planes and indexes once, and the
/// renderer hands a second map the scene and result the first one built.
/// </summary>
public sealed class SharedSceneTests
{
    [Fact]
    public void LayersBoundToOneSceneShareItsPlanes()
    {
        var scene = new VectorScene(
        [
            new AreaPaintOp
            {
                FeatureReference = "1",
                WorldShell = [(0, 0), (0, 10), (10, 10), (0, 0)],
                Fill = new RgbaColor(10, 20, 30),
                OutlineColor = new RgbaColor(0, 0, 0, 0),
                OutlineWidthPx = 1,
            },
            new PointPaintOp
            {
                FeatureReference = "2",
                World = (5, 5),
                FallbackColor = new RgbaColor(255, 0, 0),
                FallbackScale = 1,
            },
        ]);
        var first = new object();
        var second = new object();

        S100VectorTileRenderer.BindScene(first, scene);
        S100VectorTileRenderer.BindScene(second, scene);

        Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(first, out var firstBase, out var firstOverlay));
        Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(second, out var secondBase, out var secondOverlay));
        Assert.Same(firstBase, secondBase);
        Assert.Same(firstOverlay, secondOverlay);
    }

    [Fact]
    public async Task TwoMapsShowingOnePortrayalShareItsScene()
    {
        var renderer = new MapsuiDatasetRenderer(new IdentityCrsTransformFactory());
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor("cell") { ContentScope = "cell-bytes" };
        Assert.True(owner.TryRegister(id, processor));

        var (firstLayer, secondLayer) = await RenderOnTwoMapsAsync(renderer, owner, id);

        Assert.NotSame(firstLayer, secondLayer);
        Assert.Same(BaseSceneOf(firstLayer), BaseSceneOf(secondLayer));
        Assert.Equal(2, processor.RenderCount);
        Assert.Equal(1, renderer.SharedScenes.CountFor(processor));
    }

    [Fact]
    public async Task APortrayalWithoutAContentScopeIsNotShared()
    {
        var renderer = new MapsuiDatasetRenderer(new IdentityCrsTransformFactory());
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor("cell");
        Assert.True(owner.TryRegister(id, processor));

        var (firstLayer, secondLayer) = await RenderOnTwoMapsAsync(renderer, owner, id);

        Assert.NotSame(BaseSceneOf(firstLayer), BaseSceneOf(secondLayer));
        Assert.Equal(0, renderer.SharedScenes.CountFor(processor));
    }

    [Fact]
    public async Task AnotherPortrayalOfTheProcessorGetsItsOwnScene()
    {
        var renderer = new MapsuiDatasetRenderer(new IdentityCrsTransformFactory());
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor("cell") { ContentScope = "cell-bytes" };
        Assert.True(owner.TryRegister(id, processor));

        using var firstMap = new Map();
        using var first = CreateSession(firstMap, owner, renderer);
        first.SetDataset(Dataset(id));
        await first.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        var firstLayer = Assert.Single(first.GetDataset(id)!.Layers);

        // A portrayal keyed differently (here, edited cell content) builds its own scene.
        processor.ContentScope = "edited-cell-bytes";
        using var secondMap = new Map();
        using var second = CreateSession(secondMap, owner, renderer);
        second.SetDataset(Dataset(id));
        await second.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        var secondLayer = Assert.Single(second.GetDataset(id)!.Layers);

        Assert.NotSame(BaseSceneOf(firstLayer), BaseSceneOf(secondLayer));
        Assert.Equal(2, renderer.SharedScenes.CountFor(processor));
    }

    [Fact]
    public async Task ScenesAreSharedOnlyThroughOneRenderer()
    {
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId("cell");
        Assert.True(owner.TryRegister(id, new StubProcessor("cell") { ContentScope = "cell-bytes" }));

        using var firstMap = new Map();
        using var first = CreateSession(firstMap, owner, new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()));
        using var secondMap = new Map();
        using var second = CreateSession(secondMap, owner, new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()));
        first.SetDataset(Dataset(id));
        second.SetDataset(Dataset(id));
        await first.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await second.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        Assert.NotSame(
            BaseSceneOf(Assert.Single(first.GetDataset(id)!.Layers)),
            BaseSceneOf(Assert.Single(second.GetDataset(id)!.Layers)));
    }

    [Fact]
    public async Task TwoMapsShowingARealS101CellShareEveryScene()
    {
        var factory = CreateFactory();
        var renderer = new MapsuiDatasetRenderer(new ProjNetCrsTransformFactory());
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId(RealCell);
        Assert.True(owner.TryRegister(id, factory.CreateProcessor(Path.Combine(ResolveFixtureDirectory(), RealCell))));

        using var firstMap = new Map();
        using var first = CreateSession(firstMap, owner, renderer);
        using var secondMap = new Map();
        using var second = CreateSession(secondMap, owner, renderer);
        first.SetDataset(Dataset(id));
        second.SetDataset(Dataset(id));

        var clock = Stopwatch.StartNew();
        await first.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        var firstMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();
        await second.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        var secondMs = clock.Elapsed.TotalMilliseconds;
        TestContext.Current.SendDiagnosticMessage($"{RealCell}: first map {firstMs:F0} ms, second map {secondMs:F0} ms");

        var firstLayers = first.GetDataset(id)!.Layers;
        var secondLayers = second.GetDataset(id)!.Layers;
        Assert.NotEmpty(firstLayers);
        Assert.Equal(firstLayers.Count, secondLayers.Count);
        for (var i = 0; i < firstLayers.Count; i++)
        {
            Assert.NotSame(firstLayers[i], secondLayers[i]);
            Assert.Same(BaseSceneOf(firstLayers[i]), BaseSceneOf(secondLayers[i]));
        }
    }

    [Fact]
    public async Task TwoMapsShowingOnePortrayalShareItsResult()
    {
        var renderer = new MapsuiDatasetRenderer(new IdentityCrsTransformFactory());
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor("cell") { ContentScope = "cell-bytes", SubLayerCount = 2 };
        Assert.True(owner.TryRegister(id, processor));

        using var firstMap = new Map();
        using var first = CreateSession(firstMap, owner, renderer);
        using var secondMap = new Map();
        using var second = CreateSession(secondMap, owner, renderer);
        first.SetDataset(Dataset(id));
        second.SetDataset(Dataset(id));
        await first.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await second.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        // The processor still portrays for each map; the second map keeps
        // the first map's result rather than its own equal copy.
        Assert.Equal(2, processor.RenderCount);
        var shared = Assert.Single(ResultsOf(first));
        Assert.Same(shared, Assert.Single(ResultsOf(second)));
        Assert.Equal(1, renderer.SharedPortrayals.CountFor(processor));
    }

    [Fact]
    public async Task APortrayalResultWithoutAContentScopeIsNotShared()
    {
        var renderer = new MapsuiDatasetRenderer(new IdentityCrsTransformFactory());
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor("cell");
        Assert.True(owner.TryRegister(id, processor));

        using var firstMap = new Map();
        using var first = CreateSession(firstMap, owner, renderer);
        using var secondMap = new Map();
        using var second = CreateSession(secondMap, owner, renderer);
        first.SetDataset(Dataset(id));
        second.SetDataset(Dataset(id));
        await first.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await second.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        Assert.NotSame(Assert.Single(ResultsOf(first)), Assert.Single(ResultsOf(second)));
        Assert.Equal(0, renderer.SharedPortrayals.CountFor(processor));
    }

    [Fact]
    public async Task ADifferentPortrayalKeepsItsOwnResult()
    {
        var renderer = new MapsuiDatasetRenderer(new IdentityCrsTransformFactory());
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor("cell") { ContentScope = "cell-bytes" };
        Assert.True(owner.TryRegister(id, processor));

        using var firstMap = new Map();
        using var first = CreateSession(firstMap, owner, renderer);
        using var secondMap = new Map();
        using var second = CreateSession(secondMap, owner, renderer);
        first.SetDataset(Dataset(id));
        second.SetDataset(Dataset(id));
        await first.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        // Here the second map's portrayal draws on another plane.
        processor.Plane = S98DisplayPlane.BaseChartOver;
        await second.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        Assert.NotSame(Assert.Single(ResultsOf(first)), Assert.Single(ResultsOf(second)));
        Assert.Equal(2, renderer.SharedPortrayals.CountFor(processor));
    }

    [Fact]
    public async Task TwoMapsShowingARealS101CellShareItsResult()
    {
        var factory = CreateFactory();
        var renderer = new MapsuiDatasetRenderer(new ProjNetCrsTransformFactory());
        using var owner = new DatasetProcessorOwner();
        var id = new MapDatasetId(RealCell);
        Assert.True(owner.TryRegister(id, factory.CreateProcessor(Path.Combine(ResolveFixtureDirectory(), RealCell))));

        using var firstMap = new Map();
        using var first = CreateSession(firstMap, owner, renderer);
        using var secondMap = new Map();
        using var second = CreateSession(secondMap, owner, renderer);
        first.SetDataset(Dataset(id));
        second.SetDataset(Dataset(id));
        await first.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await second.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        var shared = Assert.Single(ResultsOf(first));
        Assert.Same(shared, Assert.Single(ResultsOf(second)));
    }

    // The distinct portrayal results a session's chart stack holds.
    private static IReadOnlyList<VectorPortrayalResult> ResultsOf(MapsuiDatasetLayerSession session) =>
        session.GetLayerStackEntries()
            .Select(entry => entry.Item.Payload)
            .OfType<VectorStackPayload>()
            .Select(payload => payload.Result)
            .Distinct(ReferenceEqualityComparer.Instance)
            .Cast<VectorPortrayalResult>()
            .ToList();

    private static async Task<(Mapsui.Layers.ILayer First, Mapsui.Layers.ILayer Second)> RenderOnTwoMapsAsync(
        MapsuiDatasetRenderer renderer,
        DatasetProcessorOwner owner,
        MapDatasetId id)
    {
        using var firstMap = new Map();
        using var first = CreateSession(firstMap, owner, renderer);
        using var secondMap = new Map();
        using var second = CreateSession(secondMap, owner, renderer);
        first.SetDataset(Dataset(id));
        second.SetDataset(Dataset(id));

        await first.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);
        await second.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        return (
            Assert.Single(first.GetDataset(id)!.Layers),
            Assert.Single(second.GetDataset(id)!.Layers));
    }

    private static VectorScene BaseSceneOf(object layer)
    {
        Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(layer, out var baseScene, out _));
        return baseScene;
    }

    private static MapsuiDatasetLayerSession CreateSession(Map map, DatasetProcessorOwner owner, MapsuiDatasetRenderer renderer) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            renderer,
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));

    private static MapDataset Dataset(MapDatasetId id) =>
        new(id, id.Value, new DatasetMetadata { Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)) });

    private const string RealCell = "101AA00DS0020.000";

    private static DatasetPipelineFactory CreateFactory()
    {
        var catalogueManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                catalogueManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new DatasetPipelineFactory(
            catalogueManager,
            TestLuaEngine.Create(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new DisplayPlaneAuthorityProvider());
    }

    /// <summary>Walks up from the test assembly to find the committed S-101 fixture directory.</summary>
    private static string ResolveFixtureDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "datasets", "S101", "S-101", "DATASET_FILES");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Committed S-101 fixture directory not found.");
    }
}
