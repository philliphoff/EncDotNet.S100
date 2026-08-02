using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
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
        Assert.Empty(session.GetLayerStackEntries());
        Assert.Empty(session.GetStackedLayers());
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
    public async Task InactiveDatasetRemainsInStackButCannotDraw()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, new StubProcessor(id.Value)));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, context: null);
        session.SetDataset(Dataset(id, isActive: false));

        Assert.Empty(map.Layers);
        Assert.Single(session.GetLayerStackEntries());
        Assert.Empty(session.GetStackedLayers());
        Assert.False(session.GetDataset(id)!.IsDrawing);
    }

    [Fact]
    public async Task S98OrdersProductsAndVisibleS102SuppressesS101Depth()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var s101Id = new MapDatasetId("s101");
        var s102Id = new MapDatasetId("s102");
        var s101 = new StubProcessor(s101Id.Value)
        {
            ProductSpec = "S-101",
            Plane = S98DisplayPlane.BaseChartUnder,
            FeatureType = "DepthArea",
        };
        var s102 = new StubProcessor(s102Id.Value)
        {
            ProductSpec = "S-102",
            Plane = S98DisplayPlane.Bathymetry,
        };
        Assert.True(owner.TryRegister(s102Id, s102));
        Assert.True(owner.TryRegister(s101Id, s101));
        session.SetDataset(Dataset(s102Id, productSpec: "S-102"));
        session.SetDataset(Dataset(s101Id));
        await session.RenderAsync(s102Id, context: null);
        await session.RenderAsync(s101Id, context: null);

        Assert.Equal(
            ["s101-v1", "s102-v1"],
            map.Layers.Select(layer => layer.Name));
        Assert.Empty(((MemoryLayer)map.Layers.First()).Features);
        Assert.Equal(2, session.GetLayerStackEntries().Count);

        session.SetDataset(Dataset(s102Id, isVisible: false, productSpec: "S-102"));

        Assert.Single(((MemoryLayer)map.Layers.First()).Features);
        Assert.False(map.Layers.ElementAt(1).Enabled);
    }

    [Fact]
    public async Task AuthoritySwapReprojectsTheOwnedStack()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var provider = new InteroperabilityAuthorityProvider(
            new InteroperabilityAuthority());
        using var session = CreateSession(map, owner, provider);
        var underId = new MapDatasetId("under");
        var overId = new MapDatasetId("over");
        Assert.True(owner.TryRegister(
            underId,
            new StubProcessor(underId.Value)
            {
                Plane = S98DisplayPlane.BaseChartUnder,
            }));
        Assert.True(owner.TryRegister(
            overId,
            new StubProcessor(overId.Value)
            {
                Plane = S98DisplayPlane.BaseChartOver,
            }));
        session.SetDataset(Dataset(overId));
        session.SetDataset(Dataset(underId));
        await session.RenderAsync(overId, context: null);
        await session.RenderAsync(underId, context: null);
        Assert.Equal(
            ["under-v1", "over-v1"],
            map.Layers.Select(layer => layer.Name));

        provider.Set(new LoadOrderInteroperabilityAuthority(
            new InteroperabilityAuthority()));

        Assert.Equal(
            ["over-v1", "under-v1"],
            map.Layers.Select(layer => layer.Name));
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
        session.SetMarinerSettings(new MarinerSettings
        {
            IgnoreScaleMinimum = true,
        });
        Assert.Equal(double.MaxValue, ((BaseLayer)layers[0]).MaxVisible);
        session.SetMarinerSettings(new MarinerSettings());
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
        var authority = new ThrowingAuthority();
        using var session = CreateSession(
            map,
            owner,
            new InteroperabilityAuthorityProvider(authority));
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, context: null);
        var original = Assert.Single(map.Layers);
        authority.Throw = true;
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
        var authority = new ThrowingAuthority();
        using var session = CreateSession(
            map,
            owner,
            new InteroperabilityAuthorityProvider(authority));
        var existingId = new MapDatasetId("existing");
        Assert.True(owner.TryRegister(
            existingId,
            new StubProcessor(existingId.Value)));
        session.SetDataset(Dataset(existingId));
        await session.RenderAsync(existingId, context: null);
        var existingLayer = Assert.Single(map.Layers);
        authority.Throw = true;
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
        var authority = new ThrowingAuthority();
        using var session = CreateSession(
            map,
            owner,
            new InteroperabilityAuthorityProvider(authority));
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id), minimumDisplayScale: 50_000);
        await session.RenderAsync(id, context: null);
        var original = Assert.Single(map.Layers);
        var capped = ((BaseLayer)original).MaxVisible;
        authority.Throw = true;
        Assert.Throws<InvalidOperationException>(() => session.ClearLayers(id));
        Assert.Same(original, Assert.Single(map.Layers));
        Assert.Same(original, Assert.Single(session.GetDataset(id)!.Layers));

        Assert.Throws<InvalidOperationException>(() => session.RemoveDataset(id));
        Assert.NotNull(session.GetDataset(id));
        Assert.Equal(0, processor.DisposeCount);

        Assert.Throws<InvalidOperationException>(
            () => session.SetIgnoreScaleMinimum(true));
        authority.Throw = false;
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
    public async Task LaterConcurrentRenderPreventsStaleReplacement()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, context: null);

        processor.Version = 2;
        processor.Delay = TimeSpan.FromSeconds(5);
        processor.RenderStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staleRender = session.RenderAsync(id, context: null);
        await processor.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));

        processor.Version = 3;
        processor.Delay = TimeSpan.Zero;
        processor.RenderStarted = null;
        var current = await session.RenderAsync(id, context: null);
        processor.ReleaseDelayedRender.TrySetResult();
        var stale = await staleRender;

        Assert.NotNull(current);
        Assert.Null(stale);
        Assert.Equal("dataset-v3", Assert.Single(map.Layers).Name);
        Assert.Equal(
            "dataset-v3",
            Assert.Single(session.GetStackedLayers()).Name);
    }

    [Fact]
    public async Task RemovalDuringRenderCannotReinstallStaleLayers()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value)
        {
            Version = 2,
            Delay = TimeSpan.FromSeconds(5),
            RenderStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));

        var render = session.RenderAsync(id, context: null);
        await processor.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(session.RemoveDataset(id));
        processor.ReleaseDelayedRender.TrySetResult();

        Assert.Null(await render);
        Assert.Empty(map.Layers);
        Assert.Empty(session.GetLayerStackEntries());
        Assert.Empty(session.GetStackedLayers());
    }

    [Fact]
    public async Task RemoveAndReaddDuringRenderCannotInstallIntoReplacementEntry()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value)
        {
            Version = 2,
            Delay = TimeSpan.FromSeconds(5),
            RenderStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));

        var staleRender = session.RenderAsync(id, context: null);
        await processor.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(session.RemoveDataset(id, removeProcessor: false));
        session.SetDataset(Dataset(id));
        processor.Version = 3;
        processor.Delay = TimeSpan.Zero;
        processor.RenderStarted = null;
        var current = await session.RenderAsync(id, context: null);
        processor.ReleaseDelayedRender.TrySetResult();

        Assert.NotNull(current);
        Assert.Null(await staleRender);
        Assert.Equal("dataset-v3", Assert.Single(map.Layers).Name);
        Assert.Equal(
            "dataset-v3",
            Assert.Single(session.GetStackedLayers()).Name);
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
        DatasetProcessorOwner owner,
        IInteroperabilityAuthorityProvider? authorityProvider = null) =>
        new(
            new MapsuiLayerBands(map),
            owner,
            new MapsuiDatasetRenderer(new IdentityCrsTransformFactory()),
            authorityProvider ?? new InteroperabilityAuthorityProvider(
                new InteroperabilityAuthority()));

    private static MapDataset Dataset(
        MapDatasetId id,
        bool isVisible = true,
        bool isActive = true,
        string productSpec = "S-101") =>
        new(
            id,
            id.Value,
            new DatasetMetadata
            {
                Spec = new SpecRef(productSpec, new SpecVersion(1, 0, 0)),
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

        public TaskCompletionSource? RenderStarted { get; set; }

        public TaskCompletionSource ReleaseDelayedRender { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string ProductSpec { get; set; } = "S-101";

        public string? FeatureType { get; set; }

        public S98DisplayPlane Plane { get; set; } = S98DisplayPlane.BaseChartUnder;

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public SpecRef Spec =>
            new(ProductSpec, new SpecVersion(1, 0, 0));

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;

        public async Task<VectorPortrayalResult> BuildVectorPortrayalAsync(
            RenderContext? context = null,
            CancellationToken cancellationToken = default)
        {
            var version = Version;
            var delay = Delay;
            RenderStarted?.TrySetResult();
            if (delay > TimeSpan.Zero)
            {
                await Task.WhenAny(
                    Task.Delay(delay, cancellationToken),
                    ReleaseDelayedRender.Task);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var subLayers = Enumerable.Range(0, SubLayerCount)
                .Select(index => new VectorSubLayer
                {
                    LayerKey = $"layer-{index}",
                    LayerName = SubLayerCount == 1
                        ? $"{DatasetId}-v{version}"
                        : $"{DatasetId}-{index}-v{version}",
                    Instructions =
                    [
                        new AreaInstruction
                        {
                            FeatureReference = $"{index + 1}",
                            FillColor = "TEST_FILL",
                        },
                    ],
                    Plane = Plane,
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
                Product = ProductSpec,
                Spec = Spec,
                SourceDatasetId = DatasetId,
                Info = DatasetId,
                LayerNames = subLayers.Select(layer => layer.LayerKey).ToArray(),
                FeatureTags = FeatureType is null
                    ? null
                    : new Dictionary<long, VectorFeatureTag>
                    {
                        [1] = new(FeatureType, null),
                    },
                CellMinimumDisplayScale = CellMinimumDisplayScale,
                CoverageAreas = CoverageAreas,
            };
        }

        public void Dispose() => Interlocked.Increment(ref _disposeCount);
    }

    private sealed class ThrowingAuthority : IInteroperabilityAuthority
    {
        private readonly InteroperabilityAuthority _inner = new();

        public bool Throw { get; set; }

        public S98DisplayPlane GetDefaultPlane(
            string productSpec,
            string? featureTypeOrLayerKind = null) =>
            _inner.GetDefaultPlane(productSpec, featureTypeOrLayerKind);

        public IReadOnlyList<SubLayerStackItem> Sort(
            IEnumerable<SubLayerStackItem> entries)
        {
            if (Throw)
                throw new InvalidOperationException("Projection failed.");
            return _inner.Sort(entries);
        }

        public IReadOnlyList<SubLayerStackItem> ApplyRules(
            IReadOnlyList<SubLayerStackItem> sortedStack,
            IReadOnlyList<LoadedDatasetInfo> loadedDatasets,
            MarinerSettings? mariner = null,
            IReadOnlyCollection<S98InteroperabilityRule>? rules = null) =>
            _inner.ApplyRules(sortedStack, loadedDatasets, mariner, rules);
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
