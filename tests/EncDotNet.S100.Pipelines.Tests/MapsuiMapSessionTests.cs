using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;

namespace EncDotNet.S100.Pipelines.Tests;

public sealed class MapsuiMapSessionTests
{
    [Fact]
    public void S411ProcessorExposesSharedTimeCapability()
    {
        Assert.True(typeof(ITimeAwareDatasetProcessor)
            .IsAssignableFrom(typeof(S411DatasetProcessor)));
    }

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

        var first = await session.RenderAsync(id, MapPresentationState.Default);
        var firstLayer = Assert.Single(first!.Layers);
        Assert.Same(firstLayer, Assert.Single(map.Layers));

        processor.Version = 2;
        var second = await session.RenderAsync(id, MapPresentationState.Default);
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
    public async Task RenderCreatesProductContextFromPresentationState()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("bathymetry");
        var processor = new StubProcessor(id.Value)
        {
            ProductSpec = "S-102",
        };
        var mariner = MarinerSettings.Default with { FourShades = true };
        var presentation = new MapPresentationState(
            PaletteType.Dusk,
            1.25,
            0.75,
            new EcdisDisplaySettings(),
            mariner);
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id, productSpec: "S-102"));

        await session.RenderAsync(id, presentation);

        var context = Assert.IsType<S102RenderContext>(processor.LastContext);
        Assert.Equal(PaletteType.Dusk, context.Palette);
        Assert.Equal(1.25, context.SymbolScale);
        Assert.Equal(0.75, context.TextScale);
        Assert.Same(presentation.EcdisDisplay, context.EcdisDisplay);
        Assert.Same(mariner, context.Mariner);
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
        await session.RenderAsync(firstId, MapPresentationState.Default);
        await session.RenderAsync(secondId, MapPresentationState.Default);

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
    public async Task RefreshReportraysEveryDatasetButComposesTheStackOnce()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);

        var ids = new[]
        {
            new MapDatasetId("a"),
            new MapDatasetId("b"),
            new MapDatasetId("c"),
        };
        var processors = new List<StubProcessor>();
        foreach (var id in ids)
        {
            var processor = new StubProcessor(id.Value);
            processors.Add(processor);
            Assert.True(owner.TryRegister(id, processor));
            session.SetDataset(Dataset(id));
            await session.RenderAsync(id, MapPresentationState.Default);
        }

        // Each dataset has been portrayed once by the per-cell RenderAsync above.
        Assert.All(processors, p => Assert.Equal(1, p.RenderCount));

        var layersChanged = 0;
        session.LayersChanged += (_, _) => layersChanged++;

        var applied = await session.RefreshAsync(MapPresentationState.Default);

        Assert.True(applied);
        // Every dataset is re-portrayed (a presentation change can alter any
        // cell's output)...
        Assert.All(processors, p => Assert.Equal(2, p.RenderCount));
        // ...but the whole-stack composition — and the LayersChanged notification
        // that drives the live map redraw — happens exactly once for the batch,
        // not once per cell. See MapsuiMapSession.RenderCoreAsync(compose).
        Assert.Equal(1, layersChanged);
        Assert.Equal(3, map.Layers.Count);
    }

    [Fact]
    public async Task RefreshReportraysDatasetsConcurrently()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);

        // Two cells so the assertion holds regardless of core count: the refresh
        // concurrency cap is at least two.
        var processors = new List<StubProcessor>();
        var ids = new[] { new MapDatasetId("a"), new MapDatasetId("b") };
        foreach (var id in ids)
        {
            var processor = new StubProcessor(id.Value);
            processors.Add(processor);
            Assert.True(owner.TryRegister(id, processor));
            session.SetDataset(Dataset(id));
            await session.RenderAsync(id, MapPresentationState.Default);
        }

        // Arm each processor to signal when its portrayal starts and then block
        // until released.
        var started = new List<TaskCompletionSource>();
        foreach (var processor in processors)
        {
            var startedSignal =
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            started.Add(startedSignal);
            processor.RenderStarted = startedSignal;
            processor.Delay = TimeSpan.FromSeconds(30);
        }

        var refresh = session.RefreshAsync(MapPresentationState.Default);

        // Both portrayals must start before either is released — proof the refresh
        // re-portrays concurrently. A serial refresh would block on the first
        // cell's 30s delay and never start the second.
        var allStarted = Task.WhenAll(started.Select(s => s.Task));
        var winner = await Task.WhenAny(allStarted, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(allStarted, winner);

        foreach (var processor in processors)
            processor.ReleaseDelayedRender.TrySetResult();

        Assert.True(await refresh);
        Assert.Equal(2, map.Layers.Count);
        Assert.All(processors, p => Assert.Equal(2, p.RenderCount));
    }

    [Fact]
    public async Task RefreshComposesOnceEvenWhenAnOutOfRangeTimeAwareCellIsCleared()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);

        // Two S-111 (range-gated) cells with non-overlapping time windows sharing
        // one clock: with the clock in the late cell's window, the early cell has
        // no sample and is cleared during the refresh.
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var earlyTimes = new[] { t0, t0.AddMinutes(20) };
        var lateTimes = new[] { t0.AddHours(2), t0.AddHours(2).AddMinutes(20) };
        var earlyId = new MapDatasetId("early");
        var lateId = new MapDatasetId("late");
        Assert.True(owner.TryRegister(
            earlyId,
            new StubProcessor(earlyId.Value) { ProductSpec = "S-111", AvailableTimes = earlyTimes }));
        Assert.True(owner.TryRegister(
            lateId,
            new StubProcessor(lateId.Value) { ProductSpec = "S-111", AvailableTimes = lateTimes }));
        session.SetDataset(Dataset(earlyId, productSpec: "S-111", availableTimes: earlyTimes, currentTime: t0));
        session.SetDataset(Dataset(
            lateId, productSpec: "S-111", availableTimes: lateTimes, currentTime: t0.AddHours(2)));
        await session.RenderAsync(earlyId, MapPresentationState.Default);
        await session.RenderAsync(lateId, MapPresentationState.Default);
        Assert.Equal(2, map.Layers.Count);

        session.SetCurrentTime(t0.AddHours(2));

        // The early cell is now out of range: its clear defers composition to the
        // single post-loop pass rather than composing inline on a
        // Parallel.ForEachAsync worker thread.
        var layersChanged = 0;
        session.LayersChanged += (_, _) => layersChanged++;
        Assert.True(await session.RefreshAsync(MapPresentationState.Default));

        Assert.Equal(1, layersChanged);
        Assert.Single(map.Layers);
    }

    [Fact]
    public async Task ViewportGatedRefreshDefersOffViewCellsAndRevealRefreshesThem()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);

        var nearId = new MapDatasetId("near");
        var farId = new MapDatasetId("far");
        var near = new StubProcessor(nearId.Value)
        {
            GeographicExtent = new GeographicBounds(-0.1, -0.1, 0.1, 0.1),
        };
        var far = new StubProcessor(farId.Value)
        {
            GeographicExtent = new GeographicBounds(99.9, -0.1, 100.1, 0.1),
        };
        Assert.True(owner.TryRegister(nearId, near));
        Assert.True(owner.TryRegister(farId, far));
        session.SetDataset(Dataset(nearId));
        session.SetDataset(Dataset(farId));
        await session.RenderAsync(nearId, MapPresentationState.Default);
        await session.RenderAsync(farId, MapPresentationState.Default);
        Assert.Equal(1, near.RenderCount);
        Assert.Equal(1, far.RenderCount);
        Assert.Equal(2, map.Layers.Count);

        // Viewport-gated refresh covering only the near cell: the far cell is
        // deferred, not re-portrayed.
        Assert.True(await session.RefreshAsync(
            MapPresentationState.Default, MercatorViewport(-1, -1, 1, 1)));
        Assert.Equal(2, near.RenderCount);
        Assert.Equal(1, far.RenderCount);
        Assert.Equal(2, map.Layers.Count);

        // A reveal pass over the far cell's area re-portrays it (and only it).
        Assert.True(await session.RefreshRevealedAsync(MercatorViewport(99, -1, 101, 1)));
        Assert.Equal(2, far.RenderCount);
        Assert.Equal(2, near.RenderCount);
        Assert.Equal(2, map.Layers.Count);

        // A second reveal is a no-op — nothing is stale any more.
        Assert.True(await session.RefreshRevealedAsync(MercatorViewport(99, -1, 101, 1)));
        Assert.Equal(2, far.RenderCount);
        Assert.Equal(2, near.RenderCount);
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
        await session.RenderAsync(id, MapPresentationState.Default);
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
        await session.RenderAsync(s102Id, MapPresentationState.Default);
        await session.RenderAsync(s101Id, MapPresentationState.Default);

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
        await session.RenderAsync(overId, MapPresentationState.Default);
        await session.RenderAsync(underId, MapPresentationState.Default);
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
    public void CompositionUsesOneAuthoritySnapshot()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var failingAuthority = new ThrowingAuthority { Throw = true };
        var provider = new AlternatingAuthorityProvider(
            new InteroperabilityAuthority(),
            failingAuthority);
        using var session = CreateSession(map, owner, provider);
        var id = new MapDatasetId("dataset");

        session.SetDataset(Dataset(id));

        Assert.NotNull(session.GetDataset(id));
        Assert.Equal(1, provider.ReadCount);
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
        await session.RenderAsync(id, MapPresentationState.Default);

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
        await session.RenderAsync(id, MapPresentationState.Default);

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
        await session.RenderAsync(id, MapPresentationState.Default);
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
        await session.RenderAsync(id, MapPresentationState.Default);
        var original = Assert.Single(map.Layers);

        processor.Delay = TimeSpan.FromSeconds(10);
        using var cancellation = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(25));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => session.RenderAsync(
                id,
                MapPresentationState.Default,
                cancellation.Token));

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
        await session.RenderAsync(id, MapPresentationState.Default);
        var original = Assert.Single(map.Layers);
        authority.Throw = true;
        processor.Version = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => session.RenderAsync(id, MapPresentationState.Default));

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
        await session.RenderAsync(existingId, MapPresentationState.Default);
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
        await session.RenderAsync(id, MapPresentationState.Default);
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
        await session.RenderAsync(id, MapPresentationState.Default);
        var changeCount = 0;
        session.LayersChanged += (_, _) =>
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
        await session.RenderAsync(id, MapPresentationState.Default);

        processor.Version = 2;
        processor.Delay = TimeSpan.FromSeconds(5);
        processor.RenderStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var staleRender = session.RenderAsync(id, MapPresentationState.Default);
        await processor.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        processor.Version = 3;
        processor.Delay = TimeSpan.Zero;
        processor.RenderStarted = null;
        var current = await session.RenderAsync(id, MapPresentationState.Default);
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

        var render = session.RenderAsync(id, MapPresentationState.Default);
        await processor.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
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

        var staleRender = session.RenderAsync(id, MapPresentationState.Default);
        await processor.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(session.RemoveDataset(id, removeProcessor: false));
        session.SetDataset(Dataset(id));
        processor.Version = 3;
        processor.Delay = TimeSpan.Zero;
        processor.RenderStarted = null;
        var current = await session.RenderAsync(id, MapPresentationState.Default);
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
        await session.RenderAsync(coarseId, MapPresentationState.Default);
        await session.RenderAsync(fineId, MapPresentationState.Default);

        var coarseLayer = Assert.Single(session.GetDataset(coarseId)!.Layers);
        Assert.NotNull(CoverageClip.Get(coarseLayer));

        session.SetDataset(Dataset(fineId, isVisible: false));

        Assert.Null(CoverageClip.Get(coarseLayer));
    }

    [Fact]
    public void TimeRegistrationAggregatesSamplesAndS111CoverageTolerance()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = new MapDatasetId("current");
        var processor = new StubProcessor(id.Value)
        {
            ProductSpec = "S-111",
            AvailableTimes =
            [
                first,
                first.AddMinutes(20),
                first.AddMinutes(40),
            ],
        };
        Assert.True(owner.TryRegister(id, processor));

        session.SetDataset(Dataset(id, productSpec: "S-111"));

        var time = session.GetTimeSnapshot();
        Assert.Equal(first, time.Minimum);
        Assert.Equal(first.AddMinutes(40), time.Maximum);
        Assert.Equal(first, time.Current);
        Assert.Equal(processor.AvailableTimes, time.Samples);
        var segment = Assert.Single(time.CoverageSegments);
        Assert.Equal(first, segment.Start);
        Assert.Equal(first.AddMinutes(40), segment.End);
        Assert.Equal(
            first,
            session.GetDataset(id)!.Dataset.CurrentTime);
    }

    [Fact]
    public void TimeSnapshotDefensivelyMaterializesCollections()
    {
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var samples = new List<DateTime> { first };
        var segments = new List<MapsuiMapTimeSegment>
        {
            new(first, first.AddHours(1)),
        };
        var snapshot = new MapsuiMapTimeSnapshot
        {
            Samples = samples,
            CoverageSegments = segments,
        };

        samples.Add(first.AddHours(2));
        segments.Clear();

        Assert.Equal([first], snapshot.Samples);
        Assert.Single(snapshot.CoverageSegments);
        Assert.Throws<NotSupportedException>(
            () => ((IList<DateTime>)snapshot.Samples).Add(first.AddHours(3)));
        Assert.Throws<NotSupportedException>(
            () => ((IList<MapsuiMapTimeSegment>)snapshot.CoverageSegments).Clear());
    }

    [Fact]
    public void StaticDatasetRegistrationClearsStaleTimeState()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("static");
        var staleTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(owner.TryRegister(id, new StaticProcessor()));

        session.SetDataset(Dataset(
            id,
            availableTimes: [staleTime],
            currentTime: staleTime));

        Assert.Empty(session.GetDataset(id)!.Dataset.AvailableTimes);
        Assert.Null(session.GetDataset(id)!.Dataset.CurrentTime);
    }

    [Fact]
    public void RangeRecomputeRaisesCurrentTimeChangedWhenClockIsClamped()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var earlyId = new MapDatasetId("early");
        var lateId = new MapDatasetId("late");
        Assert.True(owner.TryRegister(
            earlyId,
            new StubProcessor(earlyId.Value)
            {
                ProductSpec = "S-104",
                AvailableTimes = [first],
            }));
        Assert.True(owner.TryRegister(
            lateId,
            new StubProcessor(lateId.Value)
            {
                ProductSpec = "S-104",
                AvailableTimes = [first.AddHours(6)],
            }));
        var observed = new List<DateTime>();
        session.CurrentTimeChanged += (_, e) => observed.Add(e.CurrentTime);

        session.SetDataset(Dataset(earlyId, productSpec: "S-104"));
        Assert.Equal([first], observed);
        session.SetDataset(Dataset(lateId, productSpec: "S-104"));
        session.SetCurrentTime(first.AddHours(6));
        observed.Clear();

        Assert.True(session.RemoveDataset(lateId));

        Assert.Equal([first], observed);
        Assert.Equal(first, session.GetTimeSnapshot().Current);
    }

    [Fact]
    public async Task TimeRefreshGatesS111WindowsAndRendersNearestSample()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var earlyId = new MapDatasetId("early");
        var lateId = new MapDatasetId("late");
        var early = new StubProcessor(earlyId.Value)
        {
            ProductSpec = "S-111",
            AvailableTimes = [first, first.AddMinutes(20), first.AddMinutes(40)],
        };
        var late = new StubProcessor(lateId.Value)
        {
            ProductSpec = "S-111",
            AvailableTimes =
            [
                first.AddHours(3),
                first.AddHours(3).AddMinutes(20),
                first.AddHours(3).AddMinutes(40),
            ],
        };
        Assert.True(owner.TryRegister(earlyId, early));
        Assert.True(owner.TryRegister(lateId, late));
        session.SetDataset(Dataset(earlyId, productSpec: "S-111"));
        session.SetDataset(Dataset(lateId, productSpec: "S-111"));
        await session.RenderAsync(earlyId, MapPresentationState.Default);

        session.SetCurrentTime(first.AddHours(3).AddMinutes(18));
        await session.RefreshTimeAsync(MapPresentationState.Default);

        Assert.Empty(session.GetDataset(earlyId)!.Layers);
        Assert.Null(session.GetDataset(earlyId)!.Dataset.CurrentTime);
        Assert.Single(session.GetDataset(lateId)!.Layers);
        Assert.Equal(
            first.AddHours(3).AddMinutes(20),
            session.GetDataset(lateId)!.Dataset.CurrentTime);
        var context = Assert.IsType<S111RenderContext>(late.LastContext);
        Assert.Equal(
            first.AddHours(3).AddMinutes(20),
            context.TimeStep);
    }

    [Fact]
    public async Task RapidTimeRefreshesCoalesceToLatestClock()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = new MapDatasetId("current");
        var processor = new StubProcessor(id.Value)
        {
            ProductSpec = "S-111",
            AvailableTimes =
            [
                first,
                first.AddMinutes(20),
                first.AddMinutes(40),
            ],
        };
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id, productSpec: "S-111"));
        await session.RenderAsync(id, MapPresentationState.Default);

        session.SetCurrentTime(first.AddMinutes(20));
        var stale = session.RefreshTimeAsync(MapPresentationState.Default);
        session.SetCurrentTime(first.AddMinutes(40));
        var current = session.RefreshTimeAsync(MapPresentationState.Default);
        await Task.WhenAll(stale, current);

        Assert.Equal(2, processor.RenderCount);
        Assert.Equal(
            first.AddMinutes(40),
            session.GetDataset(id)!.Dataset.CurrentTime);
    }

    [Fact]
    public async Task S411SnapshotIsHiddenUntilClockReachesIssueTime()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var waterId = new MapDatasetId("water");
        var iceId = new MapDatasetId("ice");
        Assert.True(owner.TryRegister(
            waterId,
            new StubProcessor(waterId.Value)
            {
                ProductSpec = "S-104",
                AvailableTimes = [first, first.AddHours(12)],
            }));
        var ice = new StubProcessor(iceId.Value)
        {
            ProductSpec = "S-411",
            AvailableTimes = [first.AddHours(6)],
        };
        Assert.True(owner.TryRegister(iceId, ice));
        session.SetDataset(Dataset(waterId, productSpec: "S-104"));
        session.SetDataset(Dataset(iceId, productSpec: "S-411"));
        Assert.Null(session.GetDataset(iceId)!.Dataset.CurrentTime);

        session.SetCurrentTime(first.AddHours(7));
        await session.RefreshTimeAsync(MapPresentationState.Default);

        Assert.Equal(
            first.AddHours(6),
            session.GetDataset(iceId)!.Dataset.CurrentTime);
        Assert.Single(session.GetDataset(iceId)!.Layers);
    }

    [Fact]
    public async Task RefreshWaitsForInitialRenderWithoutCancellingLoad()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value)
        {
            Delay = TimeSpan.FromSeconds(5),
            RenderStarted = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously),
        };
        Assert.True(owner.TryRegister(id, processor));
        session.SetDataset(Dataset(id));
        var initial = session.RenderAsync(id, MapPresentationState.Default);
        await processor.RenderStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var refresh = session.RefreshAsync(MapPresentationState.Default);
        Assert.False(initial.IsCompleted);
        processor.ReleaseDelayedRender.TrySetResult();

        Assert.NotNull(await initial);
        Assert.True(await refresh);
        Assert.Equal(2, processor.RenderCount);
    }

    [Fact]
    public async Task RefreshFailureDoesNotPreventLaterDatasets()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var failingId = new MapDatasetId("failing");
        var healthyId = new MapDatasetId("healthy");
        Assert.True(owner.TryRegister(
            failingId,
            new StubProcessor(failingId.Value)
            {
                ThrowOnRender = true,
            }));
        var healthy = new StubProcessor(healthyId.Value);
        Assert.True(owner.TryRegister(healthyId, healthy));
        session.SetDataset(Dataset(failingId));
        session.SetDataset(Dataset(healthyId));
        MapSessionDatasetRenderFailedEventArgs? failure = null;
        session.DatasetRenderFailed += (_, e) => failure = e;

        Assert.True(await session.RefreshAsync(MapPresentationState.Default));

        Assert.NotNull(failure);
        Assert.Equal(failingId, failure!.DatasetId);
        Assert.Equal(MapSessionRenderKind.PresentationRefresh, failure.Kind);
        Assert.NotNull(failure.Exception);
        Assert.Equal(1, healthy.RenderCount);
        Assert.Single(session.GetDataset(healthyId)!.Layers);
    }

    [Fact]
    public async Task RenderAsyncRaisesStartedThenCompleted()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, new StubProcessor(id.Value)));
        session.SetDataset(Dataset(id));
        var events = new List<(string Phase, MapSessionRenderKind Kind, MapDatasetId Id)>();
        session.DatasetRenderStarted += (_, e) => events.Add(("started", e.Kind, e.DatasetId));
        session.DatasetRenderCompleted += (_, e) => events.Add(("completed", e.Kind, e.DatasetId));

        Assert.NotNull(await session.RenderAsync(id, MapPresentationState.Default));

        Assert.Equal(
            [
                ("started", MapSessionRenderKind.Render, id),
                ("completed", MapSessionRenderKind.Render, id),
            ],
            events);
    }

    [Fact]
    public async Task RenderStartedNotRaisedWhenProcessorLeaseUnavailable()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        // Register the dataset with the session but not the processor with the
        // owner, so the render's lease acquisition fails.
        session.SetDataset(Dataset(id));
        var started = 0;
        var completed = 0;
        session.DatasetRenderStarted += (_, _) => started++;
        session.DatasetRenderCompleted += (_, _) => completed++;

        Assert.Null(await session.RenderAsync(id, MapPresentationState.Default));

        Assert.Equal(0, started);
        Assert.Equal(0, completed);
    }

    [Fact]
    public async Task RefreshAsyncRaisesLifecycleWithPresentationRefreshKind()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var id = new MapDatasetId("dataset");
        Assert.True(owner.TryRegister(id, new StubProcessor(id.Value)));
        session.SetDataset(Dataset(id));
        await session.RenderAsync(id, MapPresentationState.Default);
        var started = new List<MapSessionRenderKind>();
        var completed = new List<MapSessionRenderKind>();
        session.DatasetRenderStarted += (_, e) => started.Add(e.Kind);
        session.DatasetRenderCompleted += (_, e) => completed.Add(e.Kind);

        Assert.True(await session.RefreshAsync(MapPresentationState.Default));

        Assert.Equal([MapSessionRenderKind.PresentationRefresh], started);
        Assert.Equal([MapSessionRenderKind.PresentationRefresh], completed);
    }

    [Fact]
    public async Task RefreshTimeAsyncRaisesLifecycleWithTimeRefreshKind()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var id = new MapDatasetId("current");
        Assert.True(owner.TryRegister(
            id,
            new StubProcessor(id.Value)
            {
                ProductSpec = "S-111",
                AvailableTimes = [first, first.AddMinutes(20), first.AddMinutes(40)],
            }));
        session.SetDataset(Dataset(id, productSpec: "S-111"));
        await session.RenderAsync(id, MapPresentationState.Default);
        var started = new List<MapSessionDatasetRenderEventArgs>();
        var completed = new List<MapSessionDatasetRenderEventArgs>();
        session.DatasetRenderStarted += (_, e) => started.Add(e);
        session.DatasetRenderCompleted += (_, e) => completed.Add(e);

        session.SetCurrentTime(first.AddMinutes(20));
        await session.RefreshTimeAsync(MapPresentationState.Default);

        Assert.Equal(MapSessionRenderKind.TimeRefresh, Assert.Single(started).Kind);
        Assert.Equal(id, Assert.Single(started).DatasetId);
        Assert.Equal(MapSessionRenderKind.TimeRefresh, Assert.Single(completed).Kind);
    }

    [Fact]
    public async Task LazyReloadRestoresSelectedTimeWithoutRangeChange()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        using var session = CreateSession(map, owner);
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var retainedId = new MapDatasetId("retained");
        var peerId = new MapDatasetId("peer");
        var times = new[] { first, first.AddHours(1) };
        Assert.True(owner.TryRegister(
            retainedId,
            new StubProcessor(retainedId.Value)
            {
                ProductSpec = "S-104",
                AvailableTimes = times,
            }));
        Assert.True(owner.TryRegister(
            peerId,
            new StubProcessor(peerId.Value)
            {
                ProductSpec = "S-104",
                AvailableTimes = times,
            }));
        session.SetDataset(Dataset(retainedId, productSpec: "S-104"));
        session.SetDataset(Dataset(peerId, productSpec: "S-104"));
        session.SetCurrentTime(first.AddHours(1));
        await session.RefreshTimeAsync(MapPresentationState.Default);
        var retainedState = session.GetDataset(retainedId)!.Dataset;

        Assert.True(session.RemoveDataset(retainedId, preserveState: true));
        Assert.True(owner.TryRegister(
            retainedId,
            new StubProcessor(retainedId.Value)
            {
                ProductSpec = "S-104",
                AvailableTimes = times,
            }));
        session.SetDataset(retainedState);

        Assert.Equal(
            first.AddHours(1),
            session.GetDataset(retainedId)!.Dataset.CurrentTime);
        Assert.Equal(
            first.AddHours(1),
            session.GetTimeSnapshot().Current);
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

    private static MRect MercatorViewport(
        double west, double south, double east, double north)
    {
        var (minX, minY) = SphericalMercator.FromLonLat(west, south);
        var (maxX, maxY) = SphericalMercator.FromLonLat(east, north);
        return new MRect(minX, minY, maxX, maxY);
    }

    private static MapDataset Dataset(
        MapDatasetId id,
        bool isVisible = true,
        bool isActive = true,
        string productSpec = "S-101",
        IReadOnlyList<DateTime>? availableTimes = null,
        DateTime? currentTime = null) =>
        new(
            id,
            id.Value,
            new DatasetMetadata
            {
                Spec = new SpecRef(productSpec, new SpecVersion(1, 0, 0)),
            },
            isVisible,
            isActive,
            availableTimes: availableTimes,
            currentTime: currentTime);

    private sealed class StaticProcessor : IDatasetProcessor
    {
        public SpecRef Spec => new("S-101", new SpecVersion(1, 0, 0));

        public FeatureInfo? GetFeatureInfo(string featureRef) => null;
    }

    private sealed class StubProcessor :
        IDatasetProcessor,
        ITimeAwareDatasetProcessor,
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

        public IReadOnlyList<DateTime> AvailableTimes { get; set; } = [];

        public RenderContext? LastContext { get; private set; }

        public int RenderCount { get; private set; }

        public bool ThrowOnRender { get; set; }

        public int SubLayerCount { get; set; } = 1;

        public int? CellMinimumDisplayScale { get; set; }

        public IReadOnlyList<CoverageArea> CoverageAreas { get; set; } = [];

        /// <summary>
        /// When set, flows to the portrayal result's
        /// <c>GeographicExtent</c> so the rendered entry gets a controllable
        /// Web-Mercator extent (used to exercise viewport-gated refresh).
        /// </summary>
        public GeographicBounds? GeographicExtent { get; set; }

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
            LastContext = context;
            RenderCount++;
            if (ThrowOnRender)
                throw new InvalidOperationException("Render failed.");
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
                GeographicExtent = GeographicExtent,
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

    private sealed class AlternatingAuthorityProvider(
        IInteroperabilityAuthority first,
        IInteroperabilityAuthority subsequent) : IInteroperabilityAuthorityProvider
    {
        private int _readCount;

        public int ReadCount => Volatile.Read(ref _readCount);

        public IInteroperabilityAuthority Current =>
            Interlocked.Increment(ref _readCount) == 1 ? first : subsequent;

        public event Action? CurrentChanged
        {
            add { }
            remove { }
        }
    }
}
