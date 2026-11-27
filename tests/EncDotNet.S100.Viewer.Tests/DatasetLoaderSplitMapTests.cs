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
    public async Task A_dataset_loaded_while_split_is_shown_in_every_map()
    {
        // Split map step 2, Q1: whichever pane is active.
        var first = new MapViewsTests.FakeMapView("map-1");
        var second = new MapViewsTests.FakeMapView("map-2");
        _views.Add(first);
        _views.Add(second);
        _views.Activate(second);
        _loader.Initialize(_views, options: null);
        var entry = CapEntry("gale");

        await _loader.LoadAsync(entry, Ct);

        Assert.True(entry.IsVisibleIn(first.Id));
        Assert.True(entry.IsVisibleIn(second.Id));
    }

    [Fact]
    public async Task A_gridded_S104_surface_loads_hidden_in_every_map()
    {
        // A load policy hides in every map (split map step 2 §2.3), and in a
        // map opened after it too.
        var first = new MapViewsTests.FakeMapView("map-1");
        var second = new MapViewsTests.FakeMapView("map-2");
        _views.Add(first);
        _views.Add(second);
        _loader.Initialize(_views, options: null);
        var path = Path.Combine(_dir, "104XX00_gridded.h5");
        EncDotNet.S100.Testing.Scenarios.Hdf5.S104FixtureBuilder.WriteFile(
            path,
            [new EncDotNet.S100.Testing.Scenarios.Hdf5.S104FixtureBuilder.SpecRow { WaterLevelHeight = 1.5f, WaterLevelTrend = 2 }],
            1,
            1,
            useF64GridAttrs: false,
            useUnsignedCounts: false,
            productSpecification: "INT.IHO.S-104.2.0");
        var entry = new DatasetEntry(path, "S-104");

        await _loader.LoadAsync(entry, Ct);

        Assert.True(entry.IsLoaded);
        Assert.False(entry.IsVisibleIn(first.Id));
        Assert.False(entry.IsVisibleIn(second.Id));
        Assert.False(entry.IsVisibleIn(new MapViewId("map-3")));
        Assert.False(entry.IsVisibleAnywhere);
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
    public async Task Hiding_a_dataset_in_one_map_hides_it_in_that_maps_session_only()
    {
        // Split map step 2 §2.2: each session gets the entry's state with its
        // own map's visibility, and a change in one map re-sets that map only.
        var first = new MapViewsTests.FakeMapView("map-1");
        var second = new MapViewsTests.FakeMapView("map-2");
        _views.Add(first);
        _views.Add(second);
        _loader.Initialize(_views, options: null);
        var entry = CapEntry("gale");
        await _loader.LoadAsync(entry, Ct);
        var firstSets = first.Session.SetDatasets.Count;

        entry.SetVisibleIn(second.Id, false);

        Assert.True(first.Session.GetDataset(entry.Id)?.Dataset.IsVisible);
        Assert.False(second.Session.GetDataset(entry.Id)?.Dataset.IsVisible);
        Assert.Equal(firstSets, first.Session.SetDatasets.Count);
        // Still held, so the Layer stack can switch it back on there.
        Assert.Contains(entry.Id, second.Session.Order);

        entry.SetVisible(true);

        Assert.True(first.Session.GetDataset(entry.Id)?.Dataset.IsVisible);
        Assert.True(second.Session.GetDataset(entry.Id)?.Dataset.IsVisible);

        // The display hide (an AML band) still hides it in every map.
        entry.IsHiddenByDisplay = true;

        Assert.False(first.Session.GetDataset(entry.Id)?.Dataset.IsVisible);
        Assert.False(second.Session.GetDataset(entry.Id)?.Dataset.IsVisible);
    }

    [Fact]
    public async Task A_pane_opened_from_a_pane_that_hides_a_dataset_never_shows_it()
    {
        // Step 2 §2.1–§2.2: the new map's visibility is seeded from the active
        // map before the set catches its session up, and the catch-up asks
        // the entry, so the session never gets the dataset visible.
        var datasets = new DatasetsViewModel(new FakeDatasetLoaderService());
        using var visibility = new MapDatasetVisibility(_views, datasets);
        var first = new MapViewsTests.FakeMapView("map-1");
        var second = new MapViewsTests.FakeMapView("map-2");
        _views.Add(first);
        _views.Add(second);
        _loader.Initialize(_views, options: null);
        var entry = CapEntry("gale");
        datasets.Entries.Add(entry);
        await _loader.LoadAsync(entry, Ct);
        entry.SetVisibleIn(second.Id, false);
        _views.Activate(second);

        var third = new MapViewsTests.FakeMapView("map-3");
        _views.Add(third);

        Assert.False(entry.IsVisibleIn(third.Id));
        var sets = third.Session.SetDatasets.Where(d => d.Id == entry.Id).ToArray();
        Assert.NotEmpty(sets);
        Assert.All(sets, d => Assert.False(d.IsVisible));
        Assert.True(first.Session.GetDataset(entry.Id)?.Dataset.IsVisible);

        // Showing it in the new pane re-sets that pane alone.
        entry.SetVisibleIn(third.Id, true);

        Assert.True(third.Session.GetDataset(entry.Id)?.Dataset.IsVisible);
        Assert.False(second.Session.GetDataset(entry.Id)?.Dataset.IsVisible);
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
