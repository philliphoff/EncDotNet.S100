using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using Mapsui;
using Mapsui.Projections;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Pipelines.Tests;

public class S100MapSessionTests
{
    [Fact]
    public void AddS100ReturnsDisposableSessionOwningTheComposedPieces()
    {
        using var map = new Map();

        using var s100 = IdentitySession(map);

        Assert.NotNull(s100);
        Assert.NotNull(s100.Session);
        Assert.NotNull(s100.Navigator);
    }

    [Fact]
    public void AddS100WithoutCrsTransformFactoryOrRendererThrows()
    {
        using var map = new Map();

        // With neither a CRS transform factory nor an injected renderer on the
        // options there is nothing to build the default dataset renderer from.
        var ex = Assert.Throws<ArgumentException>(
            () => map.AddS100(new S100MapsuiOptions()));
        Assert.Equal("options", ex.ParamName);
    }

    [Fact]
    public async Task AddS100UsesInjectedRendererAndNeedsNoCrsFactory()
    {
        using var map = new Map();
        var renderer = new MapsuiDatasetRenderer(
            new IdentityCrsTransformFactory(), null, new S100MapsuiOptions());

        // A prebuilt renderer already carries a CRS factory, so none is required
        // here; a working render proves the injected renderer is the one used
        // (the default-renderer path would have thrown on the missing factory).
        using var s100 = map.AddS100(
            new S100MapsuiOptions { DatasetRenderer = renderer });
        var id = new MapDatasetId("dataset");

        Assert.True(await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value)));
        Assert.Single(map.Layers);
    }

    [Fact]
    public async Task DisposeDoesNotDisposeAnInjectedProcessorOwner()
    {
        using var map = new Map();
        using var owner = new DatasetProcessorOwner();
        var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new IdentityCrsTransformFactory(),
            ProcessorOwner = owner,
        });
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        await s100.AddDatasetAsync(Dataset(id), processor);

        s100.Dispose();

        // The injected owner is borrowed: the session must not dispose it, so it
        // is still populated (disposal clears its entries) and the processor it
        // holds is left to the owner's lifetime rather than retired here.
        Assert.Equal(0, processor.DisposeCount);
        Assert.Equal(1, owner.Count);
    }

    [Fact]
    public async Task DisposeDisposesASelfCreatedProcessorOwner()
    {
        using var map = new Map();
        var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        await s100.AddDatasetAsync(Dataset(id), processor);

        s100.Dispose();

        // With no injected owner AddS100 created one and owns it, so disposing
        // the session disposes the owner and every processor it holds.
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public async Task LayersAddOverlayLayerInstallsItAboveTheDatasetBand()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value));
        var datasetLayer = Assert.Single(map.Layers);

        var overlay = new Mapsui.Layers.MemoryLayer { Name = "overlay" };
        s100.Layers.AddOverlayLayer(overlay);

        // Installed on the map, above the dataset layer (higher index paints
        // later = on top), through the session rather than Map.Layers directly.
        var layers = map.Layers.ToList();
        Assert.Contains(overlay, layers);
        Assert.True(layers.IndexOf(overlay) > layers.IndexOf(datasetLayer));

        s100.Layers.RemoveOverlayLayer(overlay);
        Assert.DoesNotContain(overlay, map.Layers);
    }

    [Fact]
    public void LayersRejectALayerAlreadyInAnotherBand()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var layer = new Mapsui.Layers.MemoryLayer { Name = "shared" };
        s100.Layers.AddOverlayLayer(layer);

        // A layer can belong to only one managed band, so the same instance
        // cannot also join the tool band.
        Assert.Throws<ArgumentException>(() => s100.Layers.AddToolLayer(layer));
    }

    [Fact]
    public void LayersThrowsAfterDispose()
    {
        using var map = new Map();
        var s100 = IdentitySession(map);

        s100.Dispose();

        Assert.Throws<ObjectDisposedException>(() => s100.Layers);
    }

    [Fact]
    public void InstrumentedLayerRequestRepaintPrefersTheSinkThenFallsBackToDataHasChanged()
    {
        var layer = new InstrumentedMemoryLayer();
        var dataChanged = 0;
        layer.DataChanged += (_, _) => dataChanged++;

        // No sink wired: a repaint falls back to DataHasChanged so a session-less
        // render still refreshes.
        layer.RequestRepaint();
        Assert.Equal(1, dataChanged);

        // With a sink: the repaint routes to it and does not also fire the
        // DataHasChanged fallback.
        var sinkCalls = 0;
        layer.RequestRedraw = () => sinkCalls++;
        layer.RequestRepaint();
        Assert.Equal(1, sinkCalls);
        Assert.Equal(1, dataChanged);
    }

    [Fact]
    public async Task AddS100StampsRedrawSinkOnInstalledDatasetLayers()
    {
        using var map = new Map();
        var marshalled = 0;
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new IdentityCrsTransformFactory(),
            RedrawMarshal = action =>
            {
                marshalled++;
                action();
            },
        });
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value));

        // The session stamped its redraw action onto the installed vector layer,
        // so a background publish routes through it — here driving the marshal.
        var layer = Assert.IsType<InstrumentedMemoryLayer>(Assert.Single(map.Layers));
        Assert.NotNull(layer.RequestRedraw);
        layer.RequestRepaint();
        Assert.Equal(1, marshalled);
    }

    [Fact]
    public async Task AddS100DefaultRedrawInvalidatesTheMap()
    {
        using var map = new Map();
        var refreshes = 0;
        map.RefreshGraphicsRequest += (_, _) => refreshes++;

        // No RedrawMarshal supplied: the default redraw is Map.RefreshGraphics,
        // which every Mapsui control repaints from.
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new IdentityCrsTransformFactory(),
        });
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value));

        var layer = Assert.IsType<InstrumentedMemoryLayer>(Assert.Single(map.Layers));
        var before = refreshes;
        layer.RequestRepaint();
        Assert.True(refreshes > before);
    }

    [Fact]
    public void AddS100IsIdempotentForRendererRegistration()
    {
        using var map1 = new Map();
        using var map2 = new Map();

        using var s1 = IdentitySession(map1);
        using var s2 = IdentitySession(map2);

        Assert.NotSame(s1, s2);
    }

    [Fact]
    public async Task AddDatasetAsyncRegistersProcessorAndInstallsLayer()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);

        var added = await s100.AddDatasetAsync(Dataset(id), processor);

        Assert.True(added);
        Assert.Equal(1, processor.RenderCount);
        Assert.Single(map.Layers);
        Assert.NotNull(s100.GetDataset(id));
    }

    [Fact]
    public async Task AddDatasetAsyncRejectsDuplicateIdentity()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        Assert.True(await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value)));

        // On a duplicate identity the add returns false and does not take
        // ownership, so the caller still owns and must dispose this processor.
        using var duplicate = new StubProcessor(id.Value);
        var second = await s100.AddDatasetAsync(Dataset(id), duplicate);

        Assert.False(second);
        Assert.Single(s100.GetDatasets());
        // The session must not have disposed the rejected processor; ownership
        // stays with the caller (the `using` above disposes it).
        Assert.Equal(0, duplicate.DisposeCount);
    }

    [Fact]
    public async Task AddDatasetAsyncFailsWhenRemovedBeforeLayersInstall()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        var renderStarted =
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processor = new StubProcessor(id.Value)
        {
            RenderStarted = renderStarted,
            Delay = TimeSpan.FromSeconds(30),
        };

        var add = s100.AddDatasetAsync(Dataset(id), processor);
        await renderStarted.Task; // the render is in flight

        // Reentrant removal retires the processor before the render installs
        // layers, so RenderAsync returns null with ownership lost.
        Assert.True(s100.RemoveDataset(id));
        processor.ReleaseDelayedRender.TrySetResult();

        await Assert.ThrowsAsync<InvalidOperationException>(() => add);
        Assert.Empty(map.Layers);
        Assert.Empty(s100.GetDatasets());
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public async Task RemoveDatasetRemovesLayerAndDisposesProcessor()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        await s100.AddDatasetAsync(Dataset(id), processor);

        Assert.True(s100.RemoveDataset(id));

        Assert.Empty(map.Layers);
        Assert.Empty(s100.GetDatasets());
        Assert.Equal(1, processor.DisposeCount);
    }

    [Fact]
    public async Task SetVisibleDisablesTheInstalledLayer()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value));

        s100.SetVisible(id, false);

        Assert.False(Assert.Single(map.Layers).Enabled);
        Assert.False(s100.GetDataset(id)!.Dataset.IsVisible);
    }

    [Fact]
    public async Task SetActiveAndSetOpacityProjectOntoDatasetState()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value));

        s100.SetActive(id, false);
        s100.SetOpacity(id, 0.25);

        var dataset = s100.GetDataset(id)!.Dataset;
        Assert.False(dataset.IsActive);
        Assert.Equal(0.25, dataset.Opacity);
    }

    [Fact]
    public void SetOpacityRejectsOutOfRangeValueNamingTheOpacityParameter()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => s100.SetOpacity(new MapDatasetId("dataset"), 1.5));
        Assert.Equal("opacity", ex.ParamName);
    }

    [Fact]
    public async Task SetPresentationAsyncReRendersDatasets()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        await s100.AddDatasetAsync(Dataset(id), processor);

        await s100.SetPresentationAsync(MapPresentationState.Default);

        Assert.Equal(2, processor.RenderCount);
    }

    [Fact]
    public async Task SetTimeAsyncMovesTheClockForTimeAwareDatasets()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var first = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var times = new[] { first, first.AddMinutes(20), first.AddMinutes(40) };
        var id = new MapDatasetId("current");
        var processor = new StubProcessor(id.Value)
        {
            ProductSpec = "S-111",
            AvailableTimes = times,
        };
        await s100.AddDatasetAsync(
            Dataset(id, productSpec: "S-111", availableTimes: times, currentTime: first),
            processor);

        await s100.SetTimeAsync(first.AddMinutes(20));

        Assert.Equal(first.AddMinutes(20), s100.GetTimeSnapshot().Current);
    }

    [Fact]
    public async Task ZoomToDatasetIsANoOpForUnknownDatasetAndSafeForKnownExtent()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value));

        s100.ZoomToDataset(new MapDatasetId("missing")); // no throw
        Assert.NotNull(s100.GetDataset(id)!.Extent); // the extent ZoomToDataset uses
        s100.ZoomToDataset(id); // no throw
    }

    [Fact]
    public async Task DatasetRenderCompletedIsRaisedThroughTheFacade()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        var kinds = new List<MapSessionRenderKind>();
        s100.DatasetRenderCompleted += (_, e) => kinds.Add(e.Kind);

        await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value));

        Assert.Equal([MapSessionRenderKind.Render], kinds);
    }

    [Fact]
    public async Task DisposeDisposesOwnedProcessorsAndBlocksFurtherUse()
    {
        using var map = new Map();
        var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        await s100.AddDatasetAsync(Dataset(id), processor);

        s100.Dispose();

        Assert.Equal(1, processor.DisposeCount);
        Assert.Throws<ObjectDisposedException>(() => s100.GetDatasets());
        Assert.Throws<ObjectDisposedException>(() => s100.Session);
        Assert.Throws<ObjectDisposedException>(() => s100.Navigator);
    }

    [Fact]
    public async Task LoadAsyncThrowsWhenNoPipelineFactoryConfigured()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);

        // No DatasetPipelineFactory in options; the factory guard fires before
        // the path is touched.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => s100.Datasets.LoadAsync("missing.000"));
    }

    [SkippableFact]
    public async Task LoadAsyncLoadsRealS101CellAndRejectsDuplicate()
    {
        var basePath = Environment.GetEnvironmentVariable("ENCDOTNET_S101_BASE_CELL");
        Skip.If(string.IsNullOrEmpty(basePath), "ENCDOTNET_S101_BASE_CELL not set.");
        Skip.IfNot(File.Exists(basePath!), $"Base cell not found: {basePath}.");

        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = CreateFactory(),
        });

        var id = await s100.Datasets.LoadAsync(basePath!);

        Assert.Equal(Path.GetFileName(basePath!), id.Value);
        Assert.NotNull(s100.GetDataset(id));
        Assert.NotEmpty(map.Layers);

        // Re-loading the same path resolves to the same identity, which is
        // already present, so the add is rejected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => s100.Datasets.LoadAsync(basePath!));
    }

    [Fact]
    public async Task AddS100MapsuiFactoryCreatesUsableSession()
    {
        var provider = new ServiceCollection()
            .AddSingleton<ICrsTransformFactory>(new IdentityCrsTransformFactory())
            .AddS100Mapsui()
            .BuildServiceProvider();

        var factory = provider.GetRequiredService<IS100MapSessionFactory>();
        using var map = new Map();
        using var s100 = factory.Create(map);
        var id = new MapDatasetId("dataset");

        Assert.True(await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value)));
        Assert.Single(map.Layers);
    }

    [Fact]
    public void AddS100MapsuiCreateThrowsWithoutCrsTransformFactory()
    {
        var provider = new ServiceCollection()
            .AddS100Mapsui()
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IS100MapSessionFactory>();
        using var map = new Map();

        Assert.Throws<InvalidOperationException>(() => factory.Create(map));
    }

    [Fact]
    public async Task AddS100MapsuiFactorySharesTheRegisteredProcessorOwner()
    {
        using var owner = new DatasetProcessorOwner();
        var provider = new ServiceCollection()
            .AddSingleton<ICrsTransformFactory>(new IdentityCrsTransformFactory())
            .AddSingleton(owner)
            .AddS100Mapsui()
            .BuildServiceProvider();
        var factory = provider.GetRequiredService<IS100MapSessionFactory>();
        using var map = new Map();
        var s100 = factory.Create(map);
        var id = new MapDatasetId("dataset");
        var processor = new StubProcessor(id.Value);
        await s100.AddDatasetAsync(Dataset(id), processor);

        s100.Dispose();

        // The factory folded the DI-registered owner into the options, so the
        // session borrowed it and left it — and its processors — for the
        // container to dispose.
        Assert.Equal(0, processor.DisposeCount);
        Assert.Equal(1, owner.Count);
    }

    [Fact]
    public void AddS100MapsuiRegistersTheSuppliedOptions()
    {
        var options = new S100MapsuiOptions
        {
            InteroperabilityAuthorityProvider =
                new InteroperabilityAuthorityProvider(new InteroperabilityAuthority()),
        };
        var provider = new ServiceCollection()
            .AddSingleton<ICrsTransformFactory>(new IdentityCrsTransformFactory())
            .AddS100Mapsui(_ => options)
            .BuildServiceProvider();

        Assert.Same(options, provider.GetRequiredService<S100MapsuiOptions>());
        // The options-carrying session still composes.
        using var map = new Map();
        using var s100 = provider.GetRequiredService<IS100MapSessionFactory>().Create(map);
        Assert.NotNull(s100);
    }

    [Fact]
    public void AddS100MapsuiIsIdempotent()
    {
        var services = new ServiceCollection();
        services.AddS100Mapsui();
        services.AddS100Mapsui();

        Assert.Single(services, d => d.ServiceType == typeof(IS100MapSessionFactory));
    }

    [Fact]
    public void AddS100MapsuiFactoryResolvesScopedDependencies()
    {
        // validateScopes: true makes resolving a scoped service from the root
        // provider throw — which is exactly what a singleton factory capturing
        // the root provider would do at Create time.
        var provider = new ServiceCollection()
            .AddScoped<ICrsTransformFactory>(_ => new IdentityCrsTransformFactory())
            .AddS100Mapsui()
            .BuildServiceProvider(validateScopes: true);

        using var scope = provider.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IS100MapSessionFactory>();
        using var map = new Map();

        using var s100 = factory.Create(map);
        Assert.NotNull(s100);
    }

    [Fact]
    public async Task PickAsyncRanksTopmostStackDatasetFirst()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var bottomId = new MapDatasetId("bottom");
        var topId = new MapDatasetId("top");
        await s100.AddDatasetAsync(
            Dataset(bottomId),
            new StubProcessor(bottomId.Value) { Hits = [Hit(0, "b", S100GeometryType.Surface)] });
        await s100.AddDatasetAsync(
            Dataset(topId),
            new StubProcessor(topId.Value) { Hits = [Hit(0, "t", S100GeometryType.Surface)] });
        s100.SetOrder([bottomId, topId]); // top painted last = topmost

        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0 });

        Assert.Equal([topId, bottomId], picks.Select(p => p.DatasetId));
    }

    [Fact]
    public async Task PickAsyncRanksPointBeforeAreaWithinDataset()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value)
            {
                Hits =
                [
                    Hit(0, "area", S100GeometryType.Surface),
                    Hit(1, "point", S100GeometryType.Point),
                ],
            });

        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0 });

        Assert.Equal(["point", "area"], picks.Select(p => p.Info.FeatureRef));
    }

    [Fact]
    public async Task PickAsyncExcludesHiddenAndInactiveDatasets()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var shownId = new MapDatasetId("shown");
        var hiddenId = new MapDatasetId("hidden");
        var inactiveId = new MapDatasetId("inactive");
        await s100.AddDatasetAsync(
            Dataset(shownId),
            new StubProcessor(shownId.Value) { Hits = [Hit(0, "s", S100GeometryType.Point)] });
        await s100.AddDatasetAsync(
            Dataset(hiddenId),
            new StubProcessor(hiddenId.Value) { Hits = [Hit(0, "h", S100GeometryType.Point)] });
        await s100.AddDatasetAsync(
            Dataset(inactiveId),
            new StubProcessor(inactiveId.Value) { Hits = [Hit(0, "i", S100GeometryType.Point)] });
        s100.SetVisible(hiddenId, false);
        s100.SetActive(inactiveId, false);

        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0 });

        Assert.Equal(shownId, Assert.Single(picks).DatasetId);
    }

    [Fact]
    public async Task PickAsyncExcludesFullyTransparentDatasets()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value) { Hits = [Hit(0, "x", S100GeometryType.Point)] });
        s100.SetOpacity(id, 0.0);

        Assert.Empty(await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0 }));
    }

    [Fact]
    public async Task PickAsyncExcludesDatasetScaledOutAtResolution()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        // A cell minimum display scale gives the entry a whole-cell scale
        // window; a resolution far coarser than its cutoff scales it out.
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value)
            {
                CellMinimumDisplayScale = 50_000,
                Hits = [Hit(0, "x", S100GeometryType.Point)],
            });

        Assert.Empty(await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0, Resolution = 100_000 }));
    }

    [Fact]
    public async Task PickAsyncIncludesDatasetWithinScaleWindowAtResolution()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value)
            {
                CellMinimumDisplayScale = 50_000,
                Hits = [Hit(0, "x", S100GeometryType.Point)],
            });

        // A resolution finer (zoomed in) than the cutoff keeps the cell drawn.
        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0, Resolution = 0.001 });

        Assert.Equal(id, Assert.Single(picks).DatasetId);
    }

    [Fact]
    public async Task PickAsyncWithoutResolutionIgnoresScaleWindow()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value)
            {
                CellMinimumDisplayScale = 50_000,
                Hits = [Hit(0, "x", S100GeometryType.Point)],
            });

        // Omitting Resolution disables scale filtering, so the same cell that a
        // coarse resolution would exclude still participates (prior behavior).
        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0 });

        Assert.Equal(id, Assert.Single(picks).DatasetId);
    }

    [Fact]
    public async Task PickAsyncIgnoresResolutionForCellWithoutScaleWindow()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        // No CellMinimumDisplayScale → no scale window → always pickable, even at
        // a very coarse resolution.
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value) { Hits = [Hit(0, "x", S100GeometryType.Point)] });

        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0, Resolution = 100_000 });

        Assert.Equal(id, Assert.Single(picks).DatasetId);
    }

    [Fact]
    public async Task PickAsyncReturnsCoverageSampleWhenNoVectorHit()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("coverage");
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value)
            {
                Hits = [],
                CoverageInfo = new FeatureInfo
                {
                    FeatureRef = "sample",
                    FeatureType = "WaterLevel",
                    Attributes = [],
                },
            });

        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0 });

        var pick = Assert.Single(picks);
        Assert.True(pick.IsCoverage);
        Assert.Equal("sample", pick.Info.FeatureRef);
        // Coverage samples have no vector geometry to outline.
        Assert.Null(pick.Geometry);
    }

    [Fact]
    public async Task PickAsyncPopulatesVectorFeatureGeometry()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        var geometry = new S100FeatureGeometry
        {
            Primitive = S100GeometryType.Point,
            Points = [new GeoPosition(50.5, -1.2)],
        };
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value)
            {
                Hits = [Hit(0, "x", S100GeometryType.Point)],
                FeatureGeometry = geometry,
            });

        var pick = Assert.Single(await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0 }));

        Assert.NotNull(pick.Geometry);
        Assert.Equal(S100GeometryType.Point, pick.Geometry!.Primitive);
        Assert.Equal(new GeoPosition(50.5, -1.2), Assert.Single(pick.Geometry.Points));
    }

    [Fact]
    public async Task PickAsyncHonorsMaxResults()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(
            Dataset(id),
            new StubProcessor(id.Value)
            {
                Hits =
                [
                    Hit(0, "a", S100GeometryType.Point),
                    Hit(1, "b", S100GeometryType.Point),
                ],
            });

        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0, MaxResults = 1 });

        Assert.Single(picks);
    }

    [Fact]
    public async Task PickAsyncReturnsEmptyWhenNothingHit()
    {
        using var map = new Map();
        using var s100 = IdentitySession(map);
        var id = new MapDatasetId("dataset");
        await s100.AddDatasetAsync(Dataset(id), new StubProcessor(id.Value));

        Assert.Empty(await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = 0, Longitude = 0 }));
    }

    [SkippableFact]
    public async Task PickAsyncResolvesFeaturesInRealS101Cell()
    {
        var basePath = Environment.GetEnvironmentVariable("ENCDOTNET_S101_BASE_CELL");
        Skip.If(string.IsNullOrEmpty(basePath), "ENCDOTNET_S101_BASE_CELL not set.");
        Skip.IfNot(File.Exists(basePath!), $"Base cell not found: {basePath}.");

        using var map = new Map();
        using var s100 = map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new ProjNetCrsTransformFactory(),
            DatasetPipelineFactory = CreateFactory(),
        });
        var id = await s100.Datasets.LoadAsync(basePath!);

        // Pick at the cell's extent centroid — a dense S-101 cell's area coverage
        // (e.g. depth areas) all but guarantees a hit there.
        var extent = s100.GetDataset(id)!.Extent!;
        var (longitude, latitude) = SphericalMercator.ToLonLat(
            extent.Centroid.X, extent.Centroid.Y);

        var picks = await s100.Query.PickAsync(
            new GeographicPickQuery { Latitude = latitude, Longitude = longitude });

        Assert.NotEmpty(picks);
        Assert.All(picks, p => Assert.Equal(id, p.DatasetId));
        Assert.NotNull(picks[0].Info);
    }

    // Most tests only need a session over an identity CRS; this keeps the
    // options-building noise out of each test body.
    private static IS100MapSession IdentitySession(Map map) =>
        map.AddS100(new S100MapsuiOptions
        {
            CrsTransformFactory = new IdentityCrsTransformFactory(),
        });

    private static FeatureGeometryHit Hit(
        int ordinal,
        string reference,
        S100GeometryType primitive,
        bool inside = true,
        double distanceMeters = 0.0) =>
        new()
        {
            FeatureRef = reference,
            Ordinal = ordinal,
            FeatureType = "TEST",
            Primitive = primitive,
            Inside = inside,
            DistanceMeters = distanceMeters,
        };

    private static DatasetPipelineFactory CreateFactory()
    {
        var pcManager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
                pcManager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
        }

        return new DatasetPipelineFactory(
            pcManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            new FeatureCatalogueManager(Specification.TryOpenFeatureCatalogue),
            new DisplayPlaneAuthorityProvider());
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
}
