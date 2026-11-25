using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Features;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Quantities;
using EncDotNet.S100.Rendering.Engine.Prediction;
using EncDotNet.S100.TestSupport;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// <see cref="DatasetLoaderService"/> over two maps (split map, #1731): a load,
/// the order, the time, a presentation change and a removal reach both maps'
/// sessions; a map added after a load catches up; a removed map's session is
/// released while the processor stays registered.
/// </summary>
[Collection(nameof(ProductRegistryDefaultCollection))]
public sealed class DatasetLoaderSplitMapTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("split-map-loader-").FullName;
    private readonly DatasetProcessorOwner _owner = new();
    private readonly GlobalTimeService _time = new();
    private readonly MapViews _views = new(new(), new(), new(), new DemandPlannerSet());
    private readonly CollectionLibrary _library;
    private readonly DatasetLoaderService _loader;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public DatasetLoaderSplitMapTests()
    {
        var settings = new ViewerSettings { SettingsFilePath = Path.Combine(_dir, "settings.json") };
        var portrayals = new PortrayalCatalogueManager();
        _library = new CollectionLibrary(
            new CollectionIndexer([]),
            new CollectionLibraryOptions(Path.Combine(_dir, "collections.json"), Path.Combine(_dir, "index-cache")));
        _loader = new DatasetLoaderService(
            settings,
            portrayals,
            TestCatalogues.Registry(settings, portrayals),
            new DatasetPipelineFactory(
                portrayals,
                TestLuaEngine.Create(),
                new ProjNetCrsTransformFactory(),
                new FeatureCatalogueManager(),
                new DisplayPlaneAuthorityProvider()),
            new StubRecentFilesService(),
            _library,
            MapPresentationState.Default,
            _time,
            Notifications.TestNotifications.Create(),
            _owner,
            renderActivityMonitor: null,
            timeProgress: null,
            cellScenes: null);
    }

    public void Dispose()
    {
        _owner.Dispose();
        _library.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private DatasetEntry CapEntry(string name) =>
        new(CapFixtures.CopyFixture(CapFixtures.GaleWarning, Path.Combine(_dir, name + ".xml")), "CAP");

    [Fact]
    public async Task Load_order_time_presentation_and_removal_reach_both_maps()
    {
        var first = new MapViewsTests.FakeMapView("map-1");
        var second = new MapViewsTests.FakeMapView("map-2");
        _views.Add(first);
        _views.Add(second);
        _loader.Initialize(_views, options: null);
        var entry = CapEntry("gale");

        await _loader.LoadAsync(entry, Ct);

        Assert.True(entry.IsLoaded);
        Assert.Contains(entry.Id, first.Session.Drawn);
        Assert.Contains(entry.Id, second.Session.Drawn);

        _loader.SetEntryOrder([entry]);
        var time = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        await _loader.ReRenderAtTimeAsync(time, Ct);
        var presentation = MapPresentationState.Default.WithMariner(
            MapPresentationState.Default.Mariner with { SafetyContour = Depth.FromMetres(12) });
        await _loader.SetPresentationAsync(presentation, Ct);

        foreach (var session in new[] { first.Session, second.Session })
        {
            Assert.Contains($"SetOrder({entry.Id.Value})", session.Calls);
            Assert.Equal(time, session.CurrentTime);
            Assert.Contains("RefreshTime", session.Calls);
            Assert.Contains("Refresh", session.Calls);
            Assert.Same(presentation.Mariner, session.Mariner);
        }

        _loader.RemoveEntry(entry);

        Assert.Empty(first.Session.Order);
        Assert.Empty(second.Session.Order);
        // Each map removes the dataset; the loader retires the processor once.
        Assert.Contains($"RemoveDatasets({entry.Id.Value}, preserve=False, processor=False)", first.Session.Calls);
        Assert.Contains($"RemoveDatasets({entry.Id.Value}, preserve=False, processor=False)", second.Session.Calls);
        Assert.False(_owner.TryAcquire(entry.Id, out _));
    }

    [Fact]
    public async Task A_map_added_after_a_load_catches_up_and_a_removed_map_is_released()
    {
        var first = new MapViewsTests.FakeMapView("map-1");
        _views.Add(first);
        _loader.Initialize(_views, options: null);
        var entry = CapEntry("gale");
        await _loader.LoadAsync(entry, Ct);
        var time = new DateTime(2026, 9, 14, 0, 0, 0, DateTimeKind.Utc);
        await _loader.ReRenderAtTimeAsync(time, Ct);

        var second = new MapViewsTests.FakeMapView("map-2");
        _views.Add(second);

        Assert.Contains(entry.Id, second.Session.Drawn);
        Assert.Equal(time, second.Session.CurrentTime);
        Assert.Same(_time.Clock, second.Session.Clock);

        _views.Remove(second);

        Assert.False(second.Session.HasSubscribers);
        Assert.Empty(second.Session.Order);
        Assert.Contains($"RemoveDatasets({entry.Id.Value}, preserve=False, processor=False)", second.Session.Calls);
        // The first map still shows it, and its processor is still registered.
        Assert.Contains(entry.Id, first.Session.Drawn);
        Assert.True(_owner.TryAcquire(entry.Id, out var lease));
        lease.Dispose();
        var calls = second.Session.Calls.Count;
        await _loader.ReRenderAtTimeAsync(time.AddHours(1), Ct);
        Assert.Equal(calls, second.Session.Calls.Count);
    }

    [Fact]
    public void The_loader_is_ready_only_while_there_is_a_map()
    {
        var map = new MapViewsTests.FakeMapView("map-1");
        _loader.Initialize(_views, options: null);
        Assert.False(_loader.IsInitialized);

        _views.Add(map);
        Assert.True(_loader.IsInitialized);

        _views.Remove(map);
        Assert.False(_loader.IsInitialized);
    }
}
