using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class LibraryPanelViewModelTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly RecordingLibraryImporter _importer = new();
    private readonly FakeLibraryLoader _loader = new();
    private readonly FakeLibraryDownloader _downloader = new();

    public LibraryPanelViewModelTests()
    {
        _library = _context.CreateService();
        _library.Initialize();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private LibraryPanelViewModel CreateViewModel() => TestLibrary.Panel(_library, _importer, _loader, _downloader, action => action(), action => action(), TimeProvider.System, feedHealth: null, notifications: null, viewTime: null, timeFormat: null, sync: null);

    private async Task<DatasetCollection> AddS57CollectionAsync(string name = "Charts")
    {
        var collection = _library.AddCollection(name, [new ExchangeSetSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet(name))]);
        await _library.WhenIdle();
        return collection;
    }

    [Fact]
    public void Empty_library_shows_the_empty_state()
    {
        using var vm = CreateViewModel();

        Assert.True(vm.Tree.IsEmpty);
        Assert.Empty(vm.Tree.Nodes);
        Assert.Empty(vm.List.Items);
    }

    [Fact]
    public async Task Collections_appear_as_nodes_with_source_children_and_the_first_is_selected()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        var node = Assert.Single(vm.Tree.Nodes);
        Assert.Equal("Charts", node.Name);
        Assert.Equal("2", node.Status);
        Assert.Single(node.Children);
        Assert.Same(node, vm.Tree.SelectedNode);
        Assert.Equal(["US5WA51M", "US5WA52M"], vm.List.Items.Select(i => i.Name));
        Assert.All(vm.List.Items, i => Assert.Equal(LibraryAvailability.Local, i.Availability));
    }

    [Fact]
    public async Task Filter_narrows_items_and_updates_the_summary()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        vm.List.FilterText = "52m";

        Assert.Equal("US5WA52M", Assert.Single(vm.List.Items).Name);
        Assert.Contains("1", vm.List.ItemsSummary);
        Assert.Contains("2", vm.List.ItemsSummary);
    }

    [Fact]
    public async Task State_segments_filter_the_list_and_count_each_state()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var total = vm.List.Items.Count;
        Assert.True(total > 0);

        Assert.True(vm.List.IsStateAll);
        Assert.Equal((total, total, 0, 0), (vm.List.AllCount, vm.List.LocalCount, vm.List.OnlineCount, vm.List.UpdatesCount));
        Assert.Equal(total.ToString(System.Globalization.CultureInfo.CurrentCulture), vm.List.ItemsSummary);

        vm.List.IsStateOnline = true;
        Assert.Equal(LibraryStateFilter.Online, vm.List.StateFilter);
        Assert.Empty(vm.List.Items);
        Assert.StartsWith("0 of ", vm.List.ItemsSummary);

        // A newer edition online moves them from Local to Updates.
        _downloader.Outdated = true;
        _downloader.RaiseChanged();
        Assert.Equal((0, total), (vm.List.LocalCount, vm.List.UpdatesCount));

        vm.List.IsStateUpdates = true;
        Assert.Equal(total, vm.List.Items.Count);

        // Updating them (no newer edition any more) empties the segment.
        _downloader.Outdated = false;
        _downloader.RaiseChanged();
        Assert.Empty(vm.List.Items);
    }

    [Fact]
    public async Task Library_changes_update_nodes_in_place_and_keep_selection()
    {
        await AddS57CollectionAsync("First");
        using var vm = CreateViewModel();
        var first = vm.Tree.SelectedNode;
        vm.List.SelectedItem = vm.List.Items[1];

        await AddS57CollectionAsync("Second");

        Assert.Equal(2, vm.Tree.Nodes.Count);
        Assert.Same(first, vm.Tree.Nodes[0]);
        Assert.Same(first, vm.Tree.SelectedNode);
        Assert.Equal("US5WA52M", vm.List.SelectedItem!.Name);
    }

    [Fact]
    public async Task Selecting_a_source_node_lists_only_its_items()
    {
        var collection = await AddS57CollectionAsync();
        _library.AddSources(collection.Id, [new ExchangeSetSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet("extra"))]);
        await _library.WhenIdle();
        using var vm = CreateViewModel();

        Assert.Equal(4, vm.List.Items.Count);
        vm.Tree.SelectedNode = vm.Tree.Nodes[0].Children[1];

        Assert.Equal(2, vm.List.Items.Count);
        Assert.All(vm.List.Items, i => Assert.Equal(vm.Tree.Nodes[0].Children[1].Id, i.Source.Id));
    }

    [Fact]
    public async Task Remove_command_removes_the_selected_node()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        Assert.True(vm.Tree.RemoveCommand.CanExecute(null));
        vm.Tree.RemoveCommand.Execute(null);

        Assert.True(vm.Tree.IsEmpty);
        Assert.Null(vm.Tree.SelectedNode);
        Assert.Empty(_library.Collections);
    }

    [Fact]
    public async Task Add_commands_target_the_selected_collection()
    {
        var collection = await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        vm.Tree.AddOnlineCatalogueCommand.Execute(null);

        Assert.Equal(("online", (Guid?)collection.Id), _importer.Calls.Single());
    }

    [Fact]
    public async Task Connect_to_a_shared_feed_targets_the_selected_collection()
    {
        var collection = await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        vm.Tree.AddSharedFeedCommand.Execute(null);

        Assert.Equal(("feed", (Guid?)collection.Id), _importer.Calls.Single());
    }

    [Fact]
    public async Task Collections_and_sources_can_be_renamed_in_place()
    {
        await AddS57CollectionAsync("Charts");
        using var vm = CreateViewModel();
        var collection = vm.Tree.Nodes.Single();

        vm.Tree.RenameCommand.Execute(null);
        Assert.True(collection.IsRenaming);
        Assert.Equal("Charts", collection.RenameText);
        collection.RenameText = "  Harbour charts ";
        vm.Tree.CommitRenameCommand.Execute(null);
        await _library.WhenIdle();

        Assert.False(collection.IsRenaming);
        Assert.Equal("Harbour charts", Assert.Single(_library.Collections).Definition.Name);

        vm.Tree.SelectedNode = vm.Tree.Nodes.Single().Children.Single();
        vm.Tree.RenameCommand.Execute(null);
        vm.Tree.SelectedNode!.RenameText = "Survey 2026";
        vm.Tree.CommitRenameCommand.Execute(null);
        await _library.WhenIdle();

        Assert.Equal("Survey 2026", Assert.Single(Assert.Single(_library.Collections).Sources).Definition.DisplayName);
        Assert.Equal("Survey 2026", vm.Tree.Nodes.Single().Children.Single().Name);

        // Escape leaves the name alone.
        vm.Tree.RenameCommand.Execute(null);
        vm.Tree.SelectedNode!.RenameText = "Something else";
        vm.Tree.CancelRenameCommand.Execute(null);
        Assert.Equal("Survey 2026", vm.Tree.Nodes.Single().Children.Single().Name);
    }

    [Fact]
    public void Session_catalogue_can_be_kept_but_not_removed()
    {
        var path = LibraryTestContext.Datasets("S128", "S128_TDS_sample.gml");
        _library.AddSessionCatalogue("sample", path, S128Dataset.Open(path));
        using var vm = CreateViewModel();

        vm.Tree.SelectedNode = vm.Tree.Nodes[0].Children[0];

        Assert.False(vm.Tree.RemoveCommand.CanExecute(null));
        Assert.True(vm.Tree.KeepInLibraryCommand.CanExecute(null));
        Assert.All(vm.List.Items, i => Assert.Equal(LibraryAvailability.Listed, i.Availability));
    }

    private static GeoPosition CentreOf(LibraryItemViewModel item) =>
        new((item.Item.Bounds!.Value.South + item.Item.Bounds.Value.North) / 2,
            (item.Item.Bounds.Value.West + item.Item.Bounds.Value.East) / 2);

    [Fact]
    public async Task A_tap_selects_the_top_hit_and_leaves_the_list_alone()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var node = vm.Tree.SelectedNode;
        var items = vm.List.Items.ToArray();
        var point = CentreOf(items[1]);

        vm.Tap.SelectTapHits(point, [items[1], items[0]], sameSpot: false);

        Assert.Same(items[1], vm.List.SelectedItem);
        Assert.Equal(items, vm.List.Items);
        Assert.Same(node, vm.Tree.SelectedNode);
        Assert.False(vm.Tap.HasLocation);
        Assert.True(vm.Tap.HasTap);
        Assert.Equal("2 here", vm.Tap.TapHitsText);
        Assert.Equal("· 1 of 2", vm.Tap.TapPositionText);

        // The same spot again steps to the next hit; Next does the same and wraps round.
        vm.Tap.SelectTapHits(point, [items[1], items[0]], sameSpot: true);
        Assert.Same(items[0], vm.List.SelectedItem);
        Assert.Equal("· 2 of 2", vm.Tap.TapPositionText);
        vm.Tap.NextTapHitCommand.Execute(null);
        Assert.Same(items[1], vm.List.SelectedItem);

        // A new spot starts from the top hit again.
        vm.Tap.SelectTapHits(point, [items[0], items[1]], sameSpot: false);
        Assert.Same(items[0], vm.List.SelectedItem);
    }

    [Fact]
    public async Task List_these_lists_every_dataset_at_the_tapped_point()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var target = vm.List.Items[0];

        vm.Tap.SelectTapHits(CentreOf(target), [target], sameSpot: false);
        Assert.False(vm.Tap.NextTapHitCommand.CanExecute(null));
        vm.Tap.ListTapHitsCommand.Execute(null);

        Assert.True(vm.Tap.HasLocation);
        Assert.False(vm.Tap.HasTap);
        Assert.Contains(vm.List.Items, i => i.Name == target.Name);
        Assert.Equal(target.Name, vm.List.SelectedItem!.Name);
        Assert.Equal(vm.List.Items.Count, vm.Tap.LocationHitCount);

        vm.Tap.ClearLocationCommand.Execute(null);
        Assert.False(vm.Tap.HasLocation);
        Assert.Equal(2, vm.List.Items.Count);
    }

    [Fact]
    public async Task Clearing_a_tap_hides_the_banner_and_clears_the_selection()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        vm.Tap.SelectTapHits(CentreOf(vm.List.Items[0]), [vm.List.Items[0]], sameSpot: false);

        vm.Tap.ClearTapCommand.Execute(null);

        Assert.False(vm.Tap.HasTap);
        Assert.Null(vm.List.SelectedItem);
        Assert.Equal(2, vm.List.Items.Count);

        // A tap where nothing is outlined clears a hand-picked selection too.
        vm.List.SelectedItem = vm.List.Items[1];
        vm.Tap.ClearTap();
        Assert.Null(vm.List.SelectedItem);
    }

    [Fact]
    public async Task What_ends_a_tap()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var items = vm.List.Items.ToArray();
        var point = CentreOf(items[0]);

        // Picking a row that is one of the hits keeps the banner; another row ends it.
        vm.Tap.SelectTapHits(point, [items[0], items[1]], sameSpot: false);
        vm.List.SelectedItem = items[1];
        Assert.True(vm.Tap.HasTap);
        vm.Tap.SelectTapHits(point, [items[0]], sameSpot: false);
        vm.List.SelectedItem = items[1];
        Assert.False(vm.Tap.HasTap);
        Assert.Same(items[1], vm.List.SelectedItem);

        // The text filter ends it.
        vm.Tap.SelectTapHits(point, [items[0]], sameSpot: false);
        vm.List.FilterText = "US5";
        Assert.False(vm.Tap.HasTap);

        // So does the state filter.
        vm.Tap.SelectTapHits(point, [vm.List.Items[0]], sameSpot: false);
        vm.List.StateFilter = LibraryStateFilter.Local;
        Assert.False(vm.Tap.HasTap);

        // And selecting a tree node.
        vm.Tap.SelectTapHits(point, [vm.List.Items[0]], sameSpot: false);
        vm.Tree.SelectedNode = vm.Tree.Nodes[0].Children[0];
        Assert.False(vm.Tap.HasTap);
    }

    [Fact]
    public async Task Taps_hit_only_the_outlines_drawn_at_the_scale()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var target = vm.List.Items[0];
        var point = CentreOf(target);

        // The synthetic cells are band 5 (harbour): outlined at harbour scale, not at overview.
        var harbour = EncDotNet.S100.Viewer.Services.LibraryCoverageOverlayController.Hits(vm.List.Items, null, point, 20_000);
        Assert.Contains(harbour, h => ReferenceEquals(h, target));
        Assert.Empty(EncDotNet.S100.Viewer.Services.LibraryCoverageOverlayController.Hits(vm.List.Items, null, point, 10_000_000));

        // The selection is always drawn, so it can always be hit.
        var selected = EncDotNet.S100.Viewer.Services.LibraryCoverageOverlayController.Hits(vm.List.Items, target, point, 10_000_000);
        Assert.Same(target, Assert.Single(selected));

        // Only listed datasets are candidates.
        vm.List.FilterText = "nothing matches this";
        Assert.Empty(EncDotNet.S100.Viewer.Services.LibraryCoverageOverlayController.Hits(vm.List.Items, null, point, 20_000));
    }

    [Fact]
    public async Task Centering_on_a_dataset_raises_its_centre()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        GeoPosition? requested = null;
        vm.Details.CenterRequested += (_, p) => requested = p;

        vm.Details.CenterOnItem();
        Assert.Null(requested);

        vm.List.SelectedItem = vm.List.Items[0];
        vm.Details.CenterOnItem();

        Assert.Equal(CentreOf(vm.List.Items[0]), requested);
    }

    [Fact]
    public void The_centre_of_bounds_crossing_the_antimeridian_is_across_it()
    {
        var centre = LibraryDetailsViewModel.CenterOf(new GeoBounds(50, 170, 60, -170));

        Assert.Equal(55, centre.Latitude);
        Assert.Equal(180, Math.Abs(centre.Longitude), 6);
    }

    [Fact]
    public async Task Zoom_to_raises_the_selected_datasets_bounds()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        GeoBounds? requested = null;
        vm.Details.ZoomRequested += (_, b) => requested = b;

        Assert.False(vm.Details.ZoomToCommand.CanExecute(null));
        vm.List.SelectedItem = vm.List.Items[0];
        vm.Details.ZoomToCommand.Execute(null);

        Assert.Equal(vm.List.Items[0].Item.Bounds, requested);
    }

    [Fact]
    public async Task Load_commands_hand_items_to_the_loader()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        Assert.False(vm.Details.LoadCommand.CanExecute(null));
        vm.List.SelectedItem = vm.List.Items[1];
        vm.Details.LoadCommand.Execute(null);
        vm.List.LoadAsYouPanCommand.Execute(null);

        Assert.Equal((false, 1), (_loader.Calls[0].Defer, _loader.Calls[0].Count));
        Assert.Equal((true, 2), (_loader.Calls[1].Defer, _loader.Calls[1].Count));
    }

    [Fact]
    public async Task Open_as_route_loads_an_S421_dataset_then_opens_it_as_a_route()
    {
        await AddS57CollectionAsync();
        var library = new TestLibrary(_library, _loader, _downloader);
        using var vm = library.CreatePanel(_importer, action => action(), action => action(), TimeProvider.System, feedHealth: null, viewTime: null, timeFormat: null, sync: null);
        var file = Path.Combine(_context.Root, "RTE-TEST-GBASIC.s421.gml");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "TestData", "S421", "RTE-TEST-GBASIC.s421.gml"), file);
        var folder = new LibrarySource(new LocalFolderSource(Guid.NewGuid(), null, _context.Root), null, LibrarySourceState.Ready);
        var route = library.Rows.Create(new CollectionItem
        {
            Key = "route",
            ProductSpec = "S-421",
            Name = "RTE-TEST-GBASIC",
            Location = new LocalItemLocation(_context.Root, "RTE-TEST-GBASIC.s421.gml", []),
        }, folder);

        // Other products have no Open as route.
        vm.List.SelectedItem = vm.List.Items.First(i => i.Item.ProductSpec != "S-421");
        Assert.False(vm.Details.CanOpenAsRoute);
        Assert.False(vm.Details.OpenAsRouteCommand.CanExecute(null));

        vm.List.SelectedItem = route;
        Assert.True(vm.Details.CanOpenAsRoute);
        _loader.Entry = new DatasetEntry(file, "S-421") { IsLoaded = true };
        vm.Details.OpenAsRouteCommand.Execute(null);

        // An explicit load first, then the route.
        Assert.Equal((false, 1), (_loader.Calls.Single().Defer, _loader.Calls.Single().Count));
        var opened = Assert.Single(library.Routes.Routes.Routes);
        Assert.Same(opened, library.Routes.Routes.ActiveRoute);
    }

    [Fact]
    public async Task Loader_changes_refresh_availability()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        Assert.Equal(LibraryAvailability.Local, vm.List.Items[0].Availability);

        _loader.State = LibraryLoadState.Loaded;
        _loader.RaiseChanged();

        Assert.Equal(LibraryAvailability.Loaded, vm.List.Items[0].Availability);
        Assert.Equal("Loaded", vm.List.Items[0].AvailabilityText);
    }

    [Fact]
    public void Online_items_can_be_downloaded_then_are_loaded()
    {
        var item = LibraryDownloadServiceTests.Cell();
        var source = new LibrarySource(new NoaaEncFeedSource(Guid.NewGuid(), null, NoaaEncFeedSource.DefaultCatalogUri, NoaaEncFilter.All),
            new SourceIndex(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "fp", [item], []), LibrarySourceState.Ready);
        var vmItem = new LibraryItemViewModel(item, source, new LibraryRowContext(TimeProvider.System, null, _loader.StateOf, _downloader, retry: null), collectionName: null);

        Assert.Equal(LibraryAvailability.Online, vmItem.Availability);
        Assert.True(vmItem.CanDownload);
        Assert.False(vmItem.CanLoad);

        _downloader.Downloaded = true;
        vmItem.RefreshAvailability();

        Assert.IsType<LocalItemLocation>(vmItem.EffectiveItem.Location);
        Assert.False(vmItem.CanDownload);
    }

    [Fact]
    public async Task The_bulk_bar_says_what_it_acts_on_and_shows_a_running_download()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        // Nothing online: the bar names the listed datasets and offers only On pan.
        Assert.True(vm.List.HasBulkBar);
        Assert.False(vm.List.HasDownloadable);
        Assert.Equal("Nothing to download", vm.List.BulkScope);

        _downloader.CanDownloadAll = true;
        _downloader.Outdated = true;
        _downloader.RaiseChanged();
        Assert.True(vm.List.HasDownloadable);
        Assert.StartsWith($"{vm.List.Items.Count} to download", vm.List.BulkSummary);
        Assert.Equal("All listed datasets", vm.List.BulkScope);
        vm.List.FilterText = "52m";
        Assert.Equal("Filtered set", vm.List.BulkScope);

        _downloader.Progress = new LibraryDownloadProgress(1, 0, 4, 500_000, 1_000_000);
        _downloader.RaiseProgress();
        Assert.True(vm.List.IsBulkDownloading);
        Assert.False(vm.List.HasDownloadable);  // the primary button is Cancel now
        Assert.StartsWith("3 to download", vm.List.BulkSummary);
        Assert.StartsWith("Downloading 2 of 4", vm.List.BulkScope);
        Assert.True(vm.List.CancelDownloadsCommand.CanExecute(null));
        vm.List.CancelDownloadsCommand.Execute(null);
        Assert.True(_downloader.CancelledAll);
    }

    [Fact]
    public async Task The_load_button_loads_a_local_dataset_or_downloads_then_loads_an_online_one()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        vm.List.SelectedItem = vm.List.Items[0];

        // Local: loads.
        Assert.True(vm.Details.LoadOrDownloadCommand.CanExecute(null));
        vm.Details.LoadOrDownloadCommand.Execute(null);
        Assert.Equal(0, _downloader.Downloads);
        Assert.Single(_loader.Calls);

        // Outdated (downloadable, still loadable): the button loads the local copy.
        _downloader.CanDownloadAll = true;
        _downloader.Outdated = true;
        vm.List.SelectedItem.RefreshAvailability();
        Assert.Equal("Load", vm.List.SelectedItem.LoadTooltip);
    }

    [Fact]
    public async Task Download_only_does_not_load()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        vm.List.SelectedItem = vm.List.Items[0];
        _downloader.CanDownloadAll = true;
        _downloader.Outdated = true;
        vm.List.SelectedItem.RefreshAvailability();

        vm.Details.DownloadOnlyCommand.Execute(null);

        Assert.Equal(1, _downloader.Downloads);
        Assert.Empty(_loader.Calls);
    }

    [Fact]
    public async Task Download_command_downloads_and_then_loads_the_selected_item()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        vm.List.SelectedItem = vm.List.Items[0];
        _downloader.CanDownloadAll = true;
        _downloader.Outdated = true;
        vm.List.SelectedItem.RefreshAvailability();

        Assert.True(vm.Details.DownloadCommand.CanExecute(null));
        vm.Details.DownloadCommand.Execute(null);

        Assert.Equal(1, _downloader.Downloads);
        Assert.Equal((false, 1), (_loader.Calls.Single().Defer, _loader.Calls.Single().Count));
    }

    [Fact]
    public async Task Downloading_a_package_reindexes_its_source_instead_of_loading()
    {
        using var context = new LibraryTestContext();
        var indexer = new PackageIndexer();
        using var library = context.CreateService(new Collections.Indexing.CollectionIndexer([indexer]));
        library.Initialize();
        library.AddCollection("Community", [new ChartCatalogsFeedSource(
            Guid.NewGuid(), null, new Uri("https://example.test/TEST_Catalog.xml"), ChartCatalogsFilter.All)]);
        await library.WhenIdle();
        using var vm = TestLibrary.Panel(library, _importer, _loader, _downloader, action => action(), coalesce: action => action(), time: TimeProvider.System, feedHealth: null, notifications: null, viewTime: null, timeFormat: null, sync: null);
        vm.List.SelectedItem = vm.List.Items.Single();
        var indexed = indexer.Calls;

        vm.Details.DownloadCommand.Execute(null);
        await library.WhenIdle();

        Assert.Equal(1, _downloader.Downloads);
        Assert.Empty(_loader.Calls);
        Assert.Equal(indexed + 1, indexer.Calls);
    }

    [Fact]
    public async Task Downloading_a_feed_item_loads_it_and_reindexes_its_source_once()
    {
        using var context = new LibraryTestContext();
        var indexer = new PackageIndexer { Layout = new PackageLayout("x.000", []) };
        using var library = context.CreateService(new Collections.Indexing.CollectionIndexer([indexer]));
        library.Initialize();
        library.AddCollection("Shared", [new ChartCatalogsFeedSource(
            Guid.NewGuid(), null, new Uri("https://example.test/TEST_Catalog.xml"), ChartCatalogsFilter.All)]);
        await library.WhenIdle();
        using var vm = TestLibrary.Panel(library, _importer, _loader, _downloader, action => action(), coalesce: action => action(), time: TimeProvider.System, feedHealth: null, notifications: null, viewTime: null, timeFormat: null, sync: null);
        vm.List.SelectedItem = vm.List.Items.Single();
        var indexed = indexer.Calls;

        vm.Details.DownloadCommand.Execute(null);
        await library.WhenIdle();

        Assert.Equal(1, _downloader.Downloads);
        // Loaded as listed; the source re-indexes so what the index reads from
        // the copy (a SECOM object's signature) appears.
        Assert.Single(_loader.Calls);
        Assert.Equal(indexed + 1, indexer.Calls);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_open_copy_is_closed_before_a_download_replaces_it_and_open_again_after(bool load)
    {
        // An open copy holds its files open; Windows won't replace them (#1075).
        using var context = new LibraryTestContext();
        var indexer = new PackageIndexer { Layout = new PackageLayout("x.000", []) };
        using var library = context.CreateService(new Collections.Indexing.CollectionIndexer([indexer]));
        library.Initialize();
        library.AddCollection("Shared", [new ChartCatalogsFeedSource(
            Guid.NewGuid(), null, new Uri("https://example.test/TEST_Catalog.xml"), ChartCatalogsFilter.All)]);
        await library.WhenIdle();
        _downloader.Downloaded = true;
        _downloader.Outdated = true;
        _loader.State = LibraryLoadState.Loaded;
        _loader.LoadSetsState = true;
        var order = new List<string>();
        _loader.Closing = () => order.Add("close");
        _downloader.Downloading = () => order.Add("download");
        using var vm = TestLibrary.Panel(library, _importer, _loader, _downloader, action => action(), coalesce: action => action(), time: TimeProvider.System, feedHealth: null, notifications: null, viewTime: null, timeFormat: null, sync: null);
        vm.List.SelectedItem = vm.List.Items.Single();

        (load ? vm.Details.DownloadCommand : vm.Details.DownloadOnlyCommand).Execute(null);
        await library.WhenIdle();

        Assert.Equal(["close", "download"], order);
        Assert.Equal([1], _loader.Closes);
        // Opened once more: by the load, or (download only) as it was.
        Assert.Equal((false, 1), Assert.Single(_loader.Calls));
        Assert.Equal(LibraryLoadState.Loaded, _loader.State);
    }

    [Fact]
    public async Task Unpacked_packages_are_groups_and_undownloaded_ones_are_named_by_description()
    {
        using var context = new LibraryTestContext();
        var indexer = new PackageIndexer { Unpacked = true };
        using var library = context.CreateService(new Collections.Indexing.CollectionIndexer([indexer]));
        library.Initialize();
        library.AddCollection("Romania", [new ChartCatalogsFeedSource(
            Guid.NewGuid(), null, new Uri("https://example.test/RO_IENC_Catalog.xml"), ChartCatalogsFilter.All)]);
        await library.WhenIdle();
        using var vm = TestLibrary.Panel(library, _importer, _loader, _downloader, action => action(), coalesce: action => action(), time: TimeProvider.System, feedHealth: null, notifications: null, viewTime: null, timeFormat: null, sync: null);

        // Base1 is unpacked (a collapsed group); Base2 is still a package.
        Assert.Equal(2, vm.List.Items.Count);
        var group = vm.List.Items[0];
        Assert.True(group.IsGroupHeader);
        Assert.Equal("Dunărea 790 - 0 (Base1)", group.Name);
        Assert.Equal((3, false), (group.GroupCount, group.IsExpanded));
        Assert.Equal("Unpacked", Assert.Single(group.Tags).Text);
        Assert.False(group.CanLoad);
        var package = vm.List.Items[1];
        Assert.True(package.IsPackageEntry);
        Assert.Equal("Dunărea 1750 - 790 (Base2)", package.Name);
        Assert.False(package.IsNameMono);
        Assert.Equal("Package", Assert.Single(package.Tags).Text);
        Assert.StartsWith("Base2 · published 2024-08-23", package.Summary);
        Assert.Equal(4, vm.List.AllCount);  // counts are datasets, not rows
        Assert.Equal("4", vm.List.ItemsSummary);  // a collapsed group's datasets count; its header doesn't
        Assert.StartsWith("3 datasets · published ", group.Summary);

        group.ToggleCommand!.Execute(null);
        Assert.Equal(5, vm.List.Items.Count);
        Assert.All(vm.List.Items.Skip(1).Take(3), i => Assert.True(i.IsGroupChild));
        Assert.Equal("4", vm.List.ItemsSummary);
        // Children don't repeat the package's title.
        Assert.All(vm.List.Items.Skip(1).Take(3), i => Assert.False(i.HasSubtitle));
        vm.List.SelectedItem = vm.List.Items[2];
        var selected = vm.List.SelectedItem;

        // A text match inside a collapsed group opens it, keeping the selection.
        vm.List.Items[0].ToggleCommand!.Execute(null);
        vm.List.FilterText = "3R7D";
        Assert.Equal(3, vm.List.Items.Count);
        Assert.Same(selected, vm.List.Items.Single(i => ReferenceEquals(i, selected)));
    }

    /// <summary>Indexes a community source as a single online package entry.</summary>
    private sealed class PackageIndexer : Collections.Indexing.ICollectionSourceIndexer
    {
        public int Calls { get; private set; }

        public PackageLayout? Layout { get; init; }

        public bool Unpacked { get; init; }

        public bool CanIndex(CollectionSource source) => source is ChartCatalogsFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(
            CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
        {
            Calls++;
            var item = new CollectionItem
            {
                Key = "Base1",
                ProductSpec = "S-57",
                Name = "Base1",
                Location = new RemoteItemLocation(new Uri("https://example.test/p.zip"), null, null, "community/TEST", "Base1", Layout),
            };
            if (Unpacked)
            {
                var remote = new RemoteItemLocation(
                    new Uri("https://example.test/p1.zip"), null, new DateTimeOffset(2025, 10, 23, 15, 17, 0, TimeSpan.Zero), "community/RO", "Base1");
                CollectionItem Cell(string name) => new()
                {
                    Key = "Base1/" + name,
                    ProductSpec = "S-57",
                    Name = name,
                    Title = "Dunărea 790 - 0 (Base1)",
                    Location = remote,
                    Properties = new Dictionary<string, string> { ["package"] = "Base1", ["packageTitle"] = "Dunărea 790 - 0 (Base1)" },
                };
                var entry = new CollectionItem
                {
                    Key = "Base2",
                    ProductSpec = "S-57",
                    Name = "Base2",
                    Title = "Dunărea 1750 - 790 (Base2)",
                    IssueDate = new DateOnly(2024, 8, 23),
                    Location = new RemoteItemLocation(new Uri("https://example.test/p2.zip"), null, null, "community/RO", "Base2"),
                    Properties = new Dictionary<string, string> { ["package"] = "Base2" },
                };
                return ValueTask.FromResult(new SourceIndex(
                    source.Id, DateTimeOffset.UtcNow, null, [Cell("3R7D0000"), Cell("3R7D0004"), entry, Cell("other")], []));
            }

            return ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [item], []));
        }
    }
}
