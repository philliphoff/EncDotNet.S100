using EncDotNet.S100.Core;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using Mapsui;
using Microsoft.Extensions.DependencyInjection;

namespace EncDotNet.S100.Pipelines.Tests;

public class S100MapSessionTests
{
    [Fact]
    public void AddS100ReturnsDisposableSessionOwningTheComposedPieces()
    {
        using var map = new Map();

        using var s100 = map.AddS100(new IdentityCrsTransformFactory());

        Assert.NotNull(s100);
        Assert.NotNull(s100.Session);
        Assert.NotNull(s100.Navigator);
    }

    [Fact]
    public void AddS100WithoutCrsTransformFactoryThrows()
    {
        using var map = new Map();

        var ex = Assert.Throws<ArgumentNullException>(
            () => map.AddS100(null!));
        Assert.Equal("crsTransformFactory", ex.ParamName);
    }

    [Fact]
    public void AddS100IsIdempotentForRendererRegistration()
    {
        using var map1 = new Map();
        using var map2 = new Map();

        using var s1 = map1.AddS100(new IdentityCrsTransformFactory());
        using var s2 = map2.AddS100(new IdentityCrsTransformFactory());

        Assert.NotSame(s1, s2);
    }

    [Fact]
    public async Task AddDatasetAsyncRegistersProcessorAndInstallsLayer()
    {
        using var map = new Map();
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());

        var ex = Assert.Throws<ArgumentOutOfRangeException>(
            () => s100.SetOpacity(new MapDatasetId("dataset"), 1.5));
        Assert.Equal("opacity", ex.ParamName);
    }

    [Fact]
    public async Task SetPresentationAsyncReRendersDatasets()
    {
        using var map = new Map();
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        var s100 = map.AddS100(new IdentityCrsTransformFactory());
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
        using var s100 = map.AddS100(new IdentityCrsTransformFactory());

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
        using var s100 = map.AddS100(
            new ProjNetCrsTransformFactory(),
            new S100MapsuiOptions { DatasetPipelineFactory = CreateFactory() });

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
