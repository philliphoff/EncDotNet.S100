using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Pipelines.Tests;

public sealed class MapsuiMapSessionTests
{
    [Fact]
    public async Task RenderReplaceAndRemoveOwnTheDatasetBand()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var processor = new StubProcessor("dataset");
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));

        var first = await session.RenderAsync(id, context: null);
        var firstLayer = Assert.Single(first!.Layers);
        Assert.Same(firstLayer, Assert.Single(map.Layers));

        processor.Version = 2;
        var second = await session.RenderAsync(id, context: null);
        var secondLayer = Assert.Single(second!.Layers);
        Assert.NotSame(firstLayer, secondLayer);
        Assert.Same(secondLayer, Assert.Single(map.Layers));

        Assert.True(session.RemoveDataset(id));
        Assert.Empty(map.Layers);
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public async Task VisibleAndActiveAreIndependentAndOrderIsBottomToTop()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var firstId = new MapDatasetId("first");
        var secondId = new MapDatasetId("second");
        Assert.True(owner.TryRegister(firstId, new StubProcessor(firstId.Value)));
        Assert.True(owner.TryRegister(secondId, new StubProcessor(secondId.Value)));
        session.SetDataset(Dataset(firstId));
        session.SetDataset(Dataset(secondId));
        await session.RenderAsync(firstId, context: null);
        await session.RenderAsync(secondId, context: null);

        session.SetOrder([secondId, firstId]);
        Assert.Equal(
            ["second-v1", "first-v1"],
            map.Layers.Select(layer => layer.Name));

        session.SetDataset(Dataset(firstId, isVisible: false, isActive: true));
        Assert.Equal(2, map.Layers.Count);
        Assert.False(map.Layers.ElementAt(1).Enabled);

        session.SetDataset(Dataset(firstId, isVisible: true, isActive: false));
        Assert.Single(map.Layers);
        Assert.Equal("second-v1", map.Layers.First().Name);
        Assert.True(session.GetDataset(firstId)!.Dataset.IsVisible);
        Assert.False(session.GetDataset(firstId)!.Dataset.IsActive);
    }

    [Fact]
    public async Task OpacityScaleAndSubLayerStateSurviveReplacement()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value) { SubLayerCount = 2 };
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id), minimumDisplayScale: 50_000);
        await session.RenderAsync(id, context: null);

        var renderedState = session.GetDataset(id)!.Dataset;
        Assert.Equal(2, renderedState.SubLayers.Count);
        session.SetDataset(new MapDataset(
            id,
            renderedState.Name,
            renderedState.Metadata,
            opacity: 0.5,
            subLayers:
            [
                new MapDatasetSubLayer("layer-0", "layer-0", opacity: 0.4),
                new MapDatasetSubLayer("layer-1", "layer-1", isVisible: false),
            ]),
            minimumDisplayScale: 50_000);

        processor.Version = 2;
        await session.RenderAsync(id, context: null);

        var layers = session.GetDataset(id)!.Layers;
        Assert.Equal(0.2, layers[0].Opacity, precision: 10);
        Assert.False(layers[1].Enabled);
        Assert.All(
            layers.OfType<BaseLayer>(),
            layer => Assert.True(layer.MaxVisible < double.MaxValue));
        Assert.Equal(
            [0.4, 1.0],
            session.GetDataset(id)!.Dataset.SubLayers.Select(layer => layer.Opacity));

        var capped = ((BaseLayer)layers[0]).MaxVisible;
        session.SetIgnoreScaleMinimum(true);
        Assert.Equal(double.MaxValue, ((BaseLayer)layers[0]).MaxVisible);
        session.SetIgnoreScaleMinimum(false);
        Assert.Equal(capped, ((BaseLayer)layers[0]).MaxVisible);

        var retainedState = session.GetDataset(id)!.Dataset;
        session.ClearLayers(id);
        session.SetDataset(retainedState, minimumDisplayScale: 50_000);
        processor.Version = 3;
        await session.RenderAsync(id, context: null);
        var reloaded = session.GetDataset(id)!;
        Assert.Equal(0.2, reloaded.Layers[0].Opacity, precision: 10);
        Assert.False(reloaded.Layers[1].Enabled);
    }

    [Fact]
    public async Task CancelledReplacementLeavesPreviousLayersInstalled()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, context: null);
        var original = Assert.Single(map.Layers);

        processor.Delay = TimeSpan.FromSeconds(10);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.RenderAsync(id, context: null, cancellation.Token));

        Assert.Same(original, Assert.Single(map.Layers));
        Assert.Same(
            original,
            Assert.Single(session.GetDataset(id)!.Layers));
    }

    [Fact]
    public async Task ProjectionFailureRollsBackReplacement()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, context: null);
        var original = Assert.Single(map.Layers);
        session.SetLayerProjector(datasets =>
        {
            var snapshot = Assert.Single(datasets);
            if (snapshot.Layers.Any(layer => layer.Name.EndsWith(
                    "-v2",
                    StringComparison.Ordinal)))
            {
                throw new InvalidOperationException("Projection failed.");
            }

            return snapshot.Layers
                .Select((layer, index) => new MapsuiProjectedDatasetLayer(
                    snapshot.Dataset.Id,
                    snapshot.LayerKeys?[index] ?? $"layer-{index}",
                    layer))
                .ToArray();
        });

        processor.Version = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RenderAsync(id, context: null));

        Assert.Same(original, Assert.Single(map.Layers));
        Assert.Same(
            original,
            Assert.Single(session.GetDataset(id)!.Layers));
    }

    [Fact]
    public async Task ProjectionFailureRollsBackStateAndRegistration()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var existingId = new MapDatasetId("existing");
        Assert.True(owner.TryRegister(
            existingId,
            new StubProcessor(existingId.Value)));
        session.SetDataset(Dataset(existingId));
        await session.RenderAsync(existingId, context: null);
        var existingLayer = Assert.Single(map.Layers);
        session.SetLayerProjector(datasets =>
        {
            if (datasets.Any(snapshot => !snapshot.Dataset.IsVisible)
                || datasets.Any(snapshot => snapshot.Dataset.Id.Value == "new"))
            {
                throw new InvalidOperationException("Projection failed.");
            }

            return datasets
                .SelectMany(snapshot => snapshot.Layers.Select(
                    (layer, index) => new MapsuiProjectedDatasetLayer(
                        snapshot.Dataset.Id,
                        snapshot.LayerKeys?[index] ?? $"layer-{index}",
                        layer)))
                .ToArray();
        });

        Assert.Throws<InvalidOperationException>(() =>
            session.SetDataset(Dataset(
                existingId,
                isVisible: false)));
        Assert.True(existingLayer.Enabled);
        Assert.True(session.GetDataset(existingId)!.Dataset.IsVisible);

        var newId = new MapDatasetId("new");
        Assert.Throws<InvalidOperationException>(() =>
            session.SetDataset(Dataset(newId)));
        Assert.Null(session.GetDataset(newId));
        Assert.Same(existingLayer, Assert.Single(map.Layers));
    }

    [Fact]
    public async Task ProjectionFailureRollsBackClearRemoveAndScaleSetting()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id), minimumDisplayScale: 50_000);
        await session.RenderAsync(id, context: null);
        var original = Assert.Single(map.Layers);
        var capped = ((BaseLayer)original).MaxVisible;
        var fail = false;
        session.SetLayerProjector(datasets =>
        {
            if (fail)
                throw new InvalidOperationException("Projection failed.");

            var snapshot = Assert.Single(datasets);
            return snapshot.Layers
                .Select((layer, index) => new MapsuiProjectedDatasetLayer(
                    snapshot.Dataset.Id,
                    snapshot.LayerKeys?[index] ?? $"layer-{index}",
                    layer))
                .ToArray();
        });

        fail = true;
        Assert.Throws<InvalidOperationException>(() => session.ClearLayers(id));
        Assert.Same(original, Assert.Single(map.Layers));
        Assert.Same(original, Assert.Single(session.GetDataset(id)!.Layers));

        Assert.Throws<InvalidOperationException>(() => session.RemoveDataset(id));
        Assert.NotNull(session.GetDataset(id));
        Assert.Equal(0, processor.DisposeCount);

        Assert.Throws<InvalidOperationException>(
            () => session.SetIgnoreScaleMinimum(true));
        fail = false;
        session.SetIgnoreScaleMinimum(true);
        Assert.Equal(double.MaxValue, ((BaseLayer)original).MaxVisible);
        session.SetIgnoreScaleMinimum(false);
        Assert.Equal(capped, ((BaseLayer)original).MaxVisible);
    }

    [Fact]
    public async Task LayersChangedCanReenterSessionWithoutOverwritingNewState()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, new StubProcessor(id.Value)));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, context: null);
        var changeCount = 0;
        session.LayersChanged += () =>
        {
            changeCount++;
            if (changeCount == 1)
                session.SetDataset(Dataset(id, isVisible: false));
        };

        session.SetDataset(Dataset(id));

        Assert.Equal(2, changeCount);
        Assert.False(Assert.Single(map.Layers).Enabled);
        Assert.False(session.GetDataset(id)!.Dataset.IsVisible);
    }

    [Fact]
    public async Task FinerVisibleCoverageSuppressesCoarserUntilHidden()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var coarseId = new MapDatasetId("coarse");
        var fineId = new MapDatasetId("fine");
        var coverage = new CoverageArea
        {
            ExteriorRing =
            [
                new GeoPosition(0, 0),
                new GeoPosition(0, 2),
                new GeoPosition(2, 2),
                new GeoPosition(2, 0),
                new GeoPosition(0, 0),
            ],
        };
        Assert.True(owner.TryRegister(
            coarseId,
            new StubProcessor(coarseId.Value)
            {
                CellMinimumDisplayScale = 100_000,
                CoverageAreas = [coverage],
            }));
        Assert.True(owner.TryRegister(
            fineId,
            new StubProcessor(fineId.Value)
            {
                CellMinimumDisplayScale = 20_000,
                CoverageAreas = [coverage],
            }));
        session.SetDataset(Dataset(coarseId));
        session.SetDataset(Dataset(fineId));
        await session.RenderAsync(coarseId, context: null);
        await session.RenderAsync(fineId, context: null);

        var coarseLayer = Assert.Single(session.GetDataset(coarseId)!.Layers);
        Assert.NotNull(CoverageClip.Get(coarseLayer));

        session.SetDataset(Dataset(fineId, isVisible: false));

        Assert.Null(CoverageClip.Get(coarseLayer));
    }

    private static MapsuiMapSession CreateSession(
        Map map,
        DatasetProcessorOwner owner) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()));

    private static MapDataset Dataset(
        MapDatasetId id,
        bool isVisible = true,
        bool isActive = true) =>
        new(
            id,
            id.Value,
            new DatasetMetadata
            {
                Spec = new SpecRef("S-101", new SpecVersion(1, 0, 0)),
            },
            isVisible,
            isActive);

    private sealed class IdentityCrsTransformFactory : ICrsTransformFactory
    {
        public ICrsTransform Create(string sourceCrs, string targetCrs) =>
            IdentityCrsTransform.Instance;
    }

    private sealed class StubProcessor :
        IDatasetProcessor,
        IVectorPortrayalSource,
        IDisposable
    {
        private int _disposeCount;

        public StubProcessor(string datasetId)
        {
            DatasetId = datasetId;
        }

        public string DatasetId { get; }

        public int Version { get; set; } = 1;

        public int SubLayerCount { get; set; } = 1;

        public int? CellMinimumDisplayScale { get; set; }

        public IReadOnlyList<CoverageArea> CoverageAreas { get; set; } = [];

        public TimeSpan Delay { get; set; }

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public SpecRef Spec { get; } =
            new("S-101", new SpecVersion(1, 0, 0));

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public async Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
            RenderContext? context = null,
            CancellationToken cancellationToken = default)
        {
            if (Delay > TimeSpan.Zero)
                await Task.Delay(Delay, cancellationToken);

            var subLayers = Enumerable.Range(0, SubLayerCount)
                .Select(index => new VectorSubLayer
                {
                    LayerKey = $"layer-{index}",
                    LayerName = SubLayerCount == 1
                        ? $"{DatasetId}-v{Version}"
                        : $"{DatasetId}-{index}-v{Version}",
                    Instructions =
                    [
                        new AreaInstruction
                        {
                            FeatureReference = $"feature-{index}",
                            FillColor = "TEST_FILL",
                        },
                    ],
                    Plane = S98DisplayPlane.BaseChartUnder,
                })
                .ToArray();
            return new VectorPortrayalResult
            {
                SubLayers = subLayers,
                Palette = new ColorPalette(
                    "Test",
                    new Dictionary<string, string>
                    {
                        ["TEST_FILL"] = "#336699",
                    }),
                GeometryProvider = new StubGeometryProvider(),
                Product = "S-101",
                Spec = Spec,
                SourceDatasetId = DatasetId,
                Info = DatasetId,
                LayerNames = subLayers.Select(layer => layer.LayerKey).ToArray(),
                CellMinimumDisplayScale = CellMinimumDisplayScale,
                CoverageAreas = CoverageAreas,
            };
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class StubGeometryProvider : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) =>
            new()
            {
                Type = GeometryType.Surface,
                Coordinates =
                [
                    new GeoPosition(0, 0),
                    new GeoPosition(0, 1),
                    new GeoPosition(1, 1),
                    new GeoPosition(1, 0),
                    new GeoPosition(0, 0),
                ],
            };
    }
}
