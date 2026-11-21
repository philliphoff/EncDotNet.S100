using EncDotNet.S100.Core;
using EncDotNet.S100.Core.Caching;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Scene;
using Mapsui;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Pipelines.Tests;

/// <summary>
/// Cell scenes on disk (map rendering slice 11): the scene serializer, the
/// stored-scene envelope, and the session's publish-then-swap path.
/// </summary>
public sealed class CellSceneStoreTests
{
    [Fact]
    public void SceneRoundTripsEveryOpTypeExactly()
    {
        var scene = SampleScene();

        var bytes = VectorSceneSerializer.Serialize(scene);
        var read = VectorSceneSerializer.Deserialize(bytes);

        // The format is deterministic, so writing the read scene again gives
        // the same bytes: every field survived.
        Assert.Equal(bytes, VectorSceneSerializer.Serialize(read));
        Assert.Equal(scene.Ops.Count, read.Ops.Count);
        Assert.Equal(1_000_000d, read.ScaleMinimumCap);
        Assert.True(read.PreferLiveDrawing);

        var point = Assert.IsType<PointPaintOp>(read.Ops[3]);
        Assert.Equal((10.5, 20.25), point.World);
        Assert.Equal("<svg id='a'/>", point.Symbol!.Value.ProcessedSvg);
        Assert.Equal(SymbolRotationCrs.Geographic, point.RotationCrs);
        Assert.NotNull(point.PlacementArea);

        var text = Assert.IsType<TextPaintOp>(read.Ops[4]);
        Assert.Same(point.PlacementArea, text.PlacementArea);
        Assert.Equal(new RgbaColor(1, 2, 3, 4), text.HaloColor);
        Assert.Null(text.BackColor);
        Assert.Equal(TextHorizontalAlignment.Start, text.HorizontalAlignment);

        var line = Assert.IsType<LinePaintOp>(read.Ops[1]);
        Assert.Equal([4f, 2f], line.DashArrayPx);
        // Shared resources come back shared.
        Assert.Same(point.Symbol!.Value.ProcessedSvg, line.Symbols[0].Symbol.ProcessedSvg);

        var pattern = Assert.IsType<PatternAreaPaintOp>(read.Ops[2]);
        Assert.Equal("DIAMOND1", pattern.PatternReference);
        Assert.Equal([1, 2, 3], pattern.TilePng);
        Assert.Single(pattern.WorldHoles);
        Assert.Equal(5000d, pattern.ScaleMinimum);
        Assert.Null(pattern.ScaleMaximum);
    }

    [Fact]
    public void TruncatedSceneIsRejected()
    {
        var bytes = VectorSceneSerializer.Serialize(SampleScene());

        Assert.Throws<InvalidDataException>(() => VectorSceneSerializer.Deserialize(bytes.AsSpan(0, bytes.Length / 2)));
        Assert.Throws<InvalidDataException>(() => VectorSceneSerializer.Deserialize([1, 2, 3, 4, 5]));
    }

    [Fact]
    public void StoredCellSceneRoundTripsThroughItsEnvelope()
    {
        var stored = new StoredCellScene
        {
            Metadata = new DatasetMetadata { Spec = new SpecRef("S-101", new SpecVersion(2, 0, 0)), HorizontalCrsEpsg = 4326 },
            Info = "info",
            Spec = new SpecRef("S-101", new SpecVersion(2, 0, 0)),
            Extent = new MRect(1, 2, 3, 4),
            LayerNames = ["layer-0"],
            CellMinimumDisplayScale = 90000,
            CellCompilationScale = null,
            CellMaximumDisplayScale = 12000,
            CoverageGeometry = new GeometryFactory().CreatePolygon(
                [new Coordinate(0, 0), new Coordinate(0, 1), new Coordinate(1, 1), new Coordinate(0, 0)]),
            Layers =
            [
                new StoredSceneLayer
                {
                    LayerKey = "layer-0",
                    LayerName = "cell-v1",
                    Product = "S-101",
                    Opacity = 0.5,
                    Plane = EncDotNet.S100.Interoperability.S98DisplayPlane.BaseChartUnder,
                    WithinPlanePriority = 7,
                    SourceDatasetId = "cell",
                    SourceFeatureType = "DepthArea",
                    SourceScaleDenominator = 22000,
                    ProductLayerSet = "S-101|scope",
                    StyleStateHash = "abc",
                    Scene = SampleScene(),
                },
            ],
        };

        var read = CellSceneStore.TryDecode(CellSceneStore.Encode(stored));

        Assert.NotNull(read);
        Assert.Equal(4326, read.Metadata.HorizontalCrsEpsg);
        Assert.Equal(stored.Spec, read.Spec);
        Assert.Equal(stored.Extent, read.Extent);
        Assert.Equal(["layer-0"], read.LayerNames);
        Assert.Equal(90000, read.CellMinimumDisplayScale);
        Assert.Null(read.CellCompilationScale);
        Assert.Equal(12000, read.CellMaximumDisplayScale);
        Assert.True(stored.CoverageGeometry.EqualsExact(read.CoverageGeometry));
        var layer = Assert.Single(read.Layers);
        Assert.Equal(0.5, layer.Opacity);
        Assert.Equal(7, layer.WithinPlanePriority);
        Assert.Equal("DepthArea", layer.SourceFeatureType);
        Assert.Equal(22000, layer.SourceScaleDenominator);
        Assert.Equal("abc", layer.StyleStateHash);
        Assert.Equal(VectorSceneSerializer.Serialize(stored.Layers[0].Scene), VectorSceneSerializer.Serialize(layer.Scene));

        Assert.Null(CellSceneStore.TryDecode([1, 2, 3]));
    }

    [Fact]
    public async Task StoreReadsWhatItWrote()
    {
        var root = Path.Combine(Path.GetTempPath(), "s100-scenes-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var contentStore = new ContentStore(root);
            var store = new CellSceneStore(contentStore.Open(CellSceneStore.KindName, 1 << 20, CellSceneStore.KindOptions));
            var key = CellSceneStore.KeyFor("cell");
            Assert.Null(await store.TryReadAsync(key, TestContext.Current.CancellationToken));

            var (stored, _) = await RenderAndCaptureAsync();
            store.Write(key, stored);

            var read = await store.TryReadAsync(key, TestContext.Current.CancellationToken);
            Assert.NotNull(read);
            Assert.Equal(1, store.Hits);
            Assert.Equal(1, store.Misses);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public async Task StoredSceneDrawsBeforeTheProcessorAndIsKeptWhenTheRenderMatches()
    {
        var (stored, _) = await RenderAndCaptureAsync();
        var decoded = CellSceneStore.TryDecode(CellSceneStore.Encode(stored))!;

        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("cell");
        session.SetDataset(Dataset(id));

        Assert.True(await session.PublishStoredAsync(id, MapsuiDatasetRenderer.FromStoredScene(decoded), TestContext.Current.CancellationToken));
        var storedLayer = Assert.Single(session.GetDataset(id)!.Layers);
        Assert.IsType<SyntheticStackPayload>(Assert.Single(session.GetLayerStackEntries()).Item.Payload);
        Assert.True(S100VectorTileRenderer.TryGetPartitionedScene(storedLayer, out var baseScene, out _));
        Assert.NotEmpty(baseScene.Ops);

        // The parsed cell renders the same portrayal: the stored layer stays,
        // now with the processor's own stack payload.
        var processor = new StubProcessor("cell") { ContentScope = "cell-bytes" };
        Assert.True(owner.TryRegister(id, processor));
        var result = await session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        Assert.Equal(1, result!.ReusedStoredLayers);
        Assert.Null(result.SceneLayers);
        Assert.Same(storedLayer, Assert.Single(result.Layers));
        Assert.IsType<VectorStackPayload>(Assert.Single(session.GetLayerStackEntries()).Item.Payload);

        // Once a processor owns the dataset, a stored scene can't replace it.
        Assert.False(await session.PublishStoredAsync(id, MapsuiDatasetRenderer.FromStoredScene(decoded), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StoredSceneIsReplacedWhenThePortrayalDiffers()
    {
        var (stored, _) = await RenderAndCaptureAsync();

        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("cell");
        session.SetDataset(Dataset(id));
        Assert.True(await session.PublishStoredAsync(id, MapsuiDatasetRenderer.FromStoredScene(stored), TestContext.Current.CancellationToken));
        var storedLayer = Assert.Single(session.GetDataset(id)!.Layers);

        // Changed cell bytes key a different tile namespace, so the scene is rebuilt.
        var processor = new StubProcessor("cell") { ContentScope = "edited-cell-bytes" };
        Assert.True(owner.TryRegister(id, processor));
        var result = await session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        Assert.Equal(0, result!.ReusedStoredLayers);
        Assert.NotNull(result.SceneLayers);
        Assert.NotSame(storedLayer, Assert.Single(result.Layers));
    }

    [Fact]
    public async Task RenderWithoutContentScopeCannotBeStored()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("cell");
        Assert.True(owner.TryRegister(id, new StubProcessor("cell")));
        session.SetDataset(Dataset(id));

        var result = await session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        Assert.Null(result!.SceneLayers);
        Assert.Null(StoredCellScene.TryCreate(result, new DatasetMetadata { Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)) }));
    }

    private static async Task<(StoredCellScene Stored, MapsuiDatasetResult Result)> RenderAndCaptureAsync()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("cell");
        var processor = new StubProcessor("cell") { ContentScope = "cell-bytes" };
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));
        var result = await session.RenderAsync(id, MapPresentationState.Default, TestContext.Current.CancellationToken);

        Assert.NotNull(result!.SceneLayers);
        var stored = StoredCellScene.TryCreate(result, processor.Metadata);
        Assert.NotNull(stored);
        return (stored, result);
    }

    private static MapsuiDatasetLayerSession CreateSession(Map map, DatasetProcessorOwner owner) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()));

    private static MapDataset Dataset(MapDatasetId id) =>
        new(id, id.Value, new DatasetMetadata { Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)) });

    private static VectorScene SampleScene()
    {
        var area = new PlacementArea(new GeometryFactory().CreatePolygon(
            [new Coordinate(0, 0), new Coordinate(0, 50), new Coordinate(50, 50), new Coordinate(50, 0), new Coordinate(0, 0)]));
        var svg = "<svg id='a'/>";
        var symbol = new ResolvedSymbol(svg, 0.6, 0.1, -0.2);
        return new VectorScene(
        [
            new AreaPaintOp
            {
                FeatureReference = "1",
                WorldShell = [(0, 0), (0, 10), (10, 10), (0, 0)],
                Fill = new RgbaColor(10, 20, 30),
                OutlineColor = new RgbaColor(0, 0, 0, 0),
                OutlineWidthPx = 1.5,
                ScaleMaximum = 1000,
            },
            new LinePaintOp
            {
                FeatureReference = "2",
                World = [(1.25, 2.5), (3, 4)],
                Color = new RgbaColor(9, 8, 7),
                WidthPx = 2,
                DashArrayPx = [4f, 2f],
                DashPhasePx = 1,
                Stroke = false,
                Symbols = [new LineSymbolPlacement(symbol, 3, 180)],
                SymbolIntervalPx = 12,
            },
            new PatternAreaPaintOp
            {
                FeatureReference = "3",
                PatternReference = "DIAMOND1",
                WorldShell = [(0, 0), (0, 5), (5, 5), (0, 0)],
                WorldHoles = [[(1, 1), (1, 2), (2, 2), (1, 1)]],
                TilePng = [1, 2, 3],
                ScaleMinimum = 5000,
            },
            new PointPaintOp
            {
                FeatureReference = "4",
                World = (10.5, 20.25),
                PlacementArea = area,
                Symbol = symbol,
                FallbackColor = new RgbaColor(255, 0, 0),
                FallbackScale = 1,
                Rotation = 45,
                RotationCrs = SymbolRotationCrs.Geographic,
                OffsetXpx = -3,
                OffsetYpx = 4,
            },
            new TextPaintOp
            {
                FeatureReference = "4",
                World = (11, 21),
                PlacementArea = area,
                Text = "Rk",
                FontSizePx = 13,
                ForeColor = new RgbaColor(0, 0, 0),
                HorizontalAlignment = TextHorizontalAlignment.Start,
                VerticalAlignment = TextVerticalAlignment.Top,
                FontWeight = TextFontWeight.Bold,
                HaloColor = new RgbaColor(1, 2, 3, 4),
                HaloWidthPx = 2,
            },
            new PointPaintOp
            {
                FeatureReference = "5",
                World = (0, 0),
                FallbackColor = new RgbaColor(0, 255, 0),
                FallbackScale = 0.5,
            },
        ])
        {
            ScaleMinimumCap = 1_000_000,
            PreferLiveDrawing = true,
        };
    }
}
