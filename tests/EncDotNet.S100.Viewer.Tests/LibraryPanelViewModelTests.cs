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

    private LibraryPanelViewModel CreateViewModel() => new(_library, _importer, _loader, _downloader, action => action());

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

        Assert.True(vm.IsEmpty);
        Assert.Empty(vm.Nodes);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Collections_appear_as_nodes_with_source_children_and_the_first_is_selected()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        var node = Assert.Single(vm.Nodes);
        Assert.Equal("Charts", node.Name);
        Assert.Equal("2", node.Status);
        Assert.Single(node.Children);
        Assert.Same(node, vm.SelectedNode);
        Assert.Equal(["US5WA51M", "US5WA52M"], vm.Items.Select(i => i.Name));
        Assert.All(vm.Items, i => Assert.Equal(LibraryAvailability.Local, i.Availability));
    }

    [Fact]
    public async Task Filter_narrows_items_and_updates_the_summary()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        vm.FilterText = "52m";

        Assert.Equal("US5WA52M", Assert.Single(vm.Items).Name);
        Assert.Contains("1", vm.ItemsSummary);
        Assert.Contains("2", vm.ItemsSummary);
    }

    [Fact]
    public async Task State_segments_filter_the_list_and_count_each_state()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var total = vm.Items.Count;
        Assert.True(total > 0);

        Assert.True(vm.IsStateAll);
        Assert.Equal((total, total, 0, 0), (vm.AllCount, vm.LocalCount, vm.OnlineCount, vm.UpdatesCount));
        Assert.Equal(total.ToString(System.Globalization.CultureInfo.CurrentCulture), vm.ItemsSummary);

        vm.IsStateOnline = true;
        Assert.Equal(LibraryStateFilter.Online, vm.StateFilter);
        Assert.Empty(vm.Items);
        Assert.StartsWith("0 of ", vm.ItemsSummary);

        // A newer edition online moves them from Local to Updates.
        _downloader.Outdated = true;
        _downloader.RaiseChanged();
        Assert.Equal((0, total), (vm.LocalCount, vm.UpdatesCount));

        vm.IsStateUpdates = true;
        Assert.Equal(total, vm.Items.Count);

        // Updating them (no newer edition any more) empties the segment.
        _downloader.Outdated = false;
        _downloader.RaiseChanged();
        Assert.Empty(vm.Items);
    }

    [Fact]
    public async Task Library_changes_update_nodes_in_place_and_keep_selection()
    {
        await AddS57CollectionAsync("First");
        using var vm = CreateViewModel();
        var first = vm.SelectedNode;
        vm.SelectedItem = vm.Items[1];

        await AddS57CollectionAsync("Second");

        Assert.Equal(2, vm.Nodes.Count);
        Assert.Same(first, vm.Nodes[0]);
        Assert.Same(first, vm.SelectedNode);
        Assert.Equal("US5WA52M", vm.SelectedItem!.Name);
    }

    [Fact]
    public async Task Selecting_a_source_node_lists_only_its_items()
    {
        var collection = await AddS57CollectionAsync();
        _library.AddSources(collection.Id, [new ExchangeSetSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet("extra"))]);
        await _library.WhenIdle();
        using var vm = CreateViewModel();

        Assert.Equal(4, vm.Items.Count);
        vm.SelectedNode = vm.Nodes[0].Children[1];

        Assert.Equal(2, vm.Items.Count);
        Assert.All(vm.Items, i => Assert.Equal(vm.Nodes[0].Children[1].Id, i.Source.Id));
    }

    [Fact]
    public async Task Remove_command_removes_the_selected_node()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        Assert.True(vm.RemoveCommand.CanExecute(null));
        vm.RemoveCommand.Execute(null);

        Assert.True(vm.IsEmpty);
        Assert.Null(vm.SelectedNode);
        Assert.Empty(_library.Collections);
    }

    [Fact]
    public async Task Add_commands_target_the_selected_collection()
    {
        var collection = await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        vm.AddOnlineCatalogueCommand.Execute(null);

        Assert.Equal(("online", (Guid?)collection.Id), _importer.Calls.Single());
    }

    [Fact]
    public async Task Connect_to_a_shared_feed_targets_the_selected_collection()
    {
        var collection = await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        vm.AddSharedFeedCommand.Execute(null);

        Assert.Equal(("feed", (Guid?)collection.Id), _importer.Calls.Single());
    }

    [Fact]
    public async Task Collections_and_sources_can_be_renamed_in_place()
    {
        await AddS57CollectionAsync("Charts");
        using var vm = CreateViewModel();
        var collection = vm.Nodes.Single();

        vm.RenameCommand.Execute(null);
        Assert.True(collection.IsRenaming);
        Assert.Equal("Charts", collection.RenameText);
        collection.RenameText = "  Harbour charts ";
        vm.CommitRenameCommand.Execute(null);
        await _library.WhenIdle();

        Assert.False(collection.IsRenaming);
        Assert.Equal("Harbour charts", Assert.Single(_library.Collections).Definition.Name);

        vm.SelectedNode = vm.Nodes.Single().Children.Single();
        vm.RenameCommand.Execute(null);
        vm.SelectedNode!.RenameText = "Survey 2026";
        vm.CommitRenameCommand.Execute(null);
        await _library.WhenIdle();

        Assert.Equal("Survey 2026", Assert.Single(Assert.Single(_library.Collections).Sources).Definition.DisplayName);
        Assert.Equal("Survey 2026", vm.Nodes.Single().Children.Single().Name);

        // Escape leaves the name alone.
        vm.RenameCommand.Execute(null);
        vm.SelectedNode!.RenameText = "Something else";
        vm.CancelRenameCommand.Execute(null);
        Assert.Equal("Survey 2026", vm.Nodes.Single().Children.Single().Name);
    }

    [Fact]
    public void Session_catalogue_can_be_kept_but_not_removed()
    {
        var path = LibraryTestContext.Datasets("S128", "S128_TDS_sample.gml");
        _library.AddSessionCatalogue("sample", path, S128Dataset.Open(path));
        using var vm = CreateViewModel();

        vm.SelectedNode = vm.Nodes[0].Children[0];

        Assert.False(vm.RemoveCommand.CanExecute(null));
        Assert.True(vm.KeepInLibraryCommand.CanExecute(null));
        Assert.All(vm.Items, i => Assert.Equal(LibraryAvailability.Listed, i.Availability));
    }

    private static GeoPosition CentreOf(LibraryItemViewModel item) =>
        new((item.Item.Bounds!.Value.South + item.Item.Bounds.Value.North) / 2,
            (item.Item.Bounds.Value.West + item.Item.Bounds.Value.East) / 2);

    [Fact]
    public async Task A_tap_selects_the_top_hit_and_leaves_the_list_alone()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var node = vm.SelectedNode;
        var items = vm.Items.ToArray();
        var point = CentreOf(items[1]);

        vm.SelectTapHits(point, [items[1], items[0]], sameSpot: false);

        Assert.Same(items[1], vm.SelectedItem);
        Assert.Equal(items, vm.Items);
        Assert.Same(node, vm.SelectedNode);
        Assert.False(vm.HasLocation);
        Assert.True(vm.HasTap);
        Assert.Equal("2 here", vm.TapHitsText);
        Assert.Equal("· 1 of 2", vm.TapPositionText);

        // The same spot again steps to the next hit; Next does the same and wraps round.
        vm.SelectTapHits(point, [items[1], items[0]], sameSpot: true);
        Assert.Same(items[0], vm.SelectedItem);
        Assert.Equal("· 2 of 2", vm.TapPositionText);
        vm.NextTapHitCommand.Execute(null);
        Assert.Same(items[1], vm.SelectedItem);

        // A new spot starts from the top hit again.
        vm.SelectTapHits(point, [items[0], items[1]], sameSpot: false);
        Assert.Same(items[0], vm.SelectedItem);
    }

    [Fact]
    public async Task List_these_lists_every_dataset_at_the_tapped_point()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var target = vm.Items[0];

        vm.SelectTapHits(CentreOf(target), [target], sameSpot: false);
        Assert.False(vm.NextTapHitCommand.CanExecute(null));
        vm.ListTapHitsCommand.Execute(null);

        Assert.True(vm.HasLocation);
        Assert.False(vm.HasTap);
        Assert.Contains(vm.Items, i => i.Name == target.Name);
        Assert.Equal(target.Name, vm.SelectedItem!.Name);
        Assert.Equal(vm.Items.Count, vm.LocationHitCount);

        vm.ClearLocationCommand.Execute(null);
        Assert.False(vm.HasLocation);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task Clearing_a_tap_hides_the_banner_and_clears_the_selection()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        vm.SelectTapHits(CentreOf(vm.Items[0]), [vm.Items[0]], sameSpot: false);

        vm.ClearTapCommand.Execute(null);

        Assert.False(vm.HasTap);
        Assert.Null(vm.SelectedItem);
        Assert.Equal(2, vm.Items.Count);

        // A tap where nothing is outlined clears a hand-picked selection too.
        vm.SelectedItem = vm.Items[1];
        vm.ClearTap();
        Assert.Null(vm.SelectedItem);
    }

    [Fact]
    public async Task What_ends_a_tap()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var items = vm.Items.ToArray();
        var point = CentreOf(items[0]);

        // Picking a row that is one of the hits keeps the banner; another row ends it.
        vm.SelectTapHits(point, [items[0], items[1]], sameSpot: false);
        vm.SelectedItem = items[1];
        Assert.True(vm.HasTap);
        vm.SelectTapHits(point, [items[0]], sameSpot: false);
        vm.SelectedItem = items[1];
        Assert.False(vm.HasTap);
        Assert.Same(items[1], vm.SelectedItem);

        // The text filter ends it.
        vm.SelectTapHits(point, [items[0]], sameSpot: false);
        vm.FilterText = "US5";
        Assert.False(vm.HasTap);

        // So does the state filter.
        vm.SelectTapHits(point, [vm.Items[0]], sameSpot: false);
        vm.StateFilter = LibraryStateFilter.Local;
        Assert.False(vm.HasTap);

        // And selecting a tree node.
        vm.SelectTapHits(point, [vm.Items[0]], sameSpot: false);
        vm.SelectedNode = vm.Nodes[0].Children[0];
        Assert.False(vm.HasTap);
    }

    [Fact]
    public async Task Taps_hit_only_the_outlines_drawn_at_the_scale()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var target = vm.Items[0];
        var point = CentreOf(target);

        // The synthetic cells are band 5 (harbour): outlined at harbour scale, not at overview.
        var harbour = EncDotNet.S100.Viewer.Services.LibraryCoverageOverlayController.Hits(vm.Items, null, point, 20_000);
        Assert.Contains(harbour, h => ReferenceEquals(h, target));
        Assert.Empty(EncDotNet.S100.Viewer.Services.LibraryCoverageOverlayController.Hits(vm.Items, null, point, 10_000_000));

        // The selection is always drawn, so it can always be hit.
        var selected = EncDotNet.S100.Viewer.Services.LibraryCoverageOverlayController.Hits(vm.Items, target, point, 10_000_000);
        Assert.Same(target, Assert.Single(selected));

        // Only listed datasets are candidates.
        vm.FilterText = "nothing matches this";
        Assert.Empty(EncDotNet.S100.Viewer.Services.LibraryCoverageOverlayController.Hits(vm.Items, null, point, 20_000));
    }

    [Fact]
    public async Task Centering_on_a_dataset_raises_its_centre()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        GeoPosition? requested = null;
        vm.CenterRequested += (_, p) => requested = p;

        vm.CenterOnSelected();
        Assert.Null(requested);

        vm.SelectedItem = vm.Items[0];
        vm.CenterOnSelected();

        Assert.Equal(CentreOf(vm.Items[0]), requested);
    }

    [Fact]
    public void The_centre_of_bounds_crossing_the_antimeridian_is_across_it()
    {
        var centre = LibraryPanelViewModel.CenterOf(new GeoBounds(50, 170, 60, -170));

        Assert.Equal(55, centre.Latitude);
        Assert.Equal(180, Math.Abs(centre.Longitude), 6);
    }

    [Fact]
    public async Task Zoom_to_raises_the_selected_datasets_bounds()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        GeoBounds? requested = null;
        vm.ZoomRequested += (_, b) => requested = b;

        Assert.False(vm.ZoomToCommand.CanExecute(null));
        vm.SelectedItem = vm.Items[0];
        vm.ZoomToCommand.Execute(null);

        Assert.Equal(vm.Items[0].Item.Bounds, requested);
    }

    [Fact]
    public async Task Load_commands_hand_items_to_the_loader()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        Assert.False(vm.LoadCommand.CanExecute(null));
        vm.SelectedItem = vm.Items[1];
        vm.LoadCommand.Execute(null);
        vm.LoadAsYouPanCommand.Execute(null);

        Assert.Equal((false, 1), (_loader.Calls[0].Defer, _loader.Calls[0].Count));
        Assert.Equal((true, 2), (_loader.Calls[1].Defer, _loader.Calls[1].Count));
    }

    [Fact]
    public async Task Loader_changes_refresh_availability()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        Assert.Equal(LibraryAvailability.Local, vm.Items[0].Availability);

        _loader.State = LibraryLoadState.Loaded;
        _loader.RaiseChanged();

        Assert.Equal(LibraryAvailability.Loaded, vm.Items[0].Availability);
        Assert.Equal("Loaded", vm.Items[0].AvailabilityText);
    }

    [Fact]
    public void Online_items_can_be_downloaded_then_are_loaded()
    {
        var item = LibraryDownloadServiceTests.Cell();
        var source = new LibrarySource(new NoaaEncFeedSource(Guid.NewGuid(), null, NoaaEncFeedSource.DefaultCatalogUri, NoaaEncFilter.All),
            new SourceIndex(Guid.NewGuid(), DateTimeOffset.UnixEpoch, "fp", [item], []), LibrarySourceState.Ready);
        var vmItem = new LibraryItemViewModel(item, source, _loader.StateOf, _downloader);

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
        Assert.True(vm.HasBulkBar);
        Assert.False(vm.HasDownloadable);
        Assert.Equal("Nothing to download", vm.BulkScope);

        _downloader.CanDownloadAll = true;
        _downloader.Outdated = true;
        _downloader.RaiseChanged();
        Assert.True(vm.HasDownloadable);
        Assert.StartsWith($"{vm.Items.Count} to download", vm.BulkSummary);
        Assert.Equal("All listed datasets", vm.BulkScope);
        vm.FilterText = "52m";
        Assert.Equal("Filtered set", vm.BulkScope);

        _downloader.Progress = new LibraryDownloadProgress(1, 0, 4, 500_000, 1_000_000);
        _downloader.RaiseProgress();
        Assert.True(vm.IsBulkDownloading);
        Assert.False(vm.HasDownloadable);  // the primary button is Cancel now
        Assert.StartsWith("3 to download", vm.BulkSummary);
        Assert.StartsWith("Downloading 2 of 4", vm.BulkScope);
        Assert.True(vm.CancelDownloadsCommand.CanExecute(null));
        vm.CancelDownloadsCommand.Execute(null);
        Assert.True(_downloader.CancelledAll);
    }

    [Fact]
    public async Task The_load_button_loads_a_local_dataset_or_downloads_then_loads_an_online_one()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        vm.SelectedItem = vm.Items[0];

        // Local: loads.
        Assert.True(vm.LoadOrDownloadCommand.CanExecute(null));
        vm.LoadOrDownloadCommand.Execute(null);
        Assert.Equal(0, _downloader.Downloads);
        Assert.Single(_loader.Calls);

        // Outdated (downloadable, still loadable): the button loads the local copy.
        _downloader.CanDownloadAll = true;
        _downloader.Outdated = true;
        vm.SelectedItem.RefreshAvailability();
        Assert.Equal("Load", vm.SelectedItem.LoadTooltip);
    }

    [Fact]
    public async Task Download_only_does_not_load()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        vm.SelectedItem = vm.Items[0];
        _downloader.CanDownloadAll = true;
        _downloader.Outdated = true;
        vm.SelectedItem.RefreshAvailability();

        vm.DownloadOnlyCommand.Execute(null);

        Assert.Equal(1, _downloader.Downloads);
        Assert.Empty(_loader.Calls);
    }

    [Fact]
    public async Task Download_command_downloads_and_then_loads_the_selected_item()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        vm.SelectedItem = vm.Items[0];
        _downloader.CanDownloadAll = true;
        _downloader.Outdated = true;
        vm.SelectedItem.RefreshAvailability();

        Assert.True(vm.DownloadCommand.CanExecute(null));
        vm.DownloadCommand.Execute(null);

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
        using var vm = new LibraryPanelViewModel(library, _importer, _loader, _downloader, action => action());
        vm.SelectedItem = vm.Items.Single();
        var indexed = indexer.Calls;

        vm.DownloadCommand.Execute(null);
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
        using var vm = new LibraryPanelViewModel(library, _importer, _loader, _downloader, action => action());
        vm.SelectedItem = vm.Items.Single();
        var indexed = indexer.Calls;

        vm.DownloadCommand.Execute(null);
        await library.WhenIdle();

        Assert.Equal(1, _downloader.Downloads);
        // Loaded as listed; the source re-indexes so what the index reads from
        // the copy (a SECOM object's signature) appears.
        Assert.Single(_loader.Calls);
        Assert.Equal(indexed + 1, indexer.Calls);
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
        using var vm = new LibraryPanelViewModel(library, _importer, _loader, _downloader, action => action());

        // Base1 is unpacked (a collapsed group); Base2 is still a package.
        Assert.Equal(2, vm.Items.Count);
        var group = vm.Items[0];
        Assert.True(group.IsGroupHeader);
        Assert.Equal("Dunărea 790 - 0 (Base1)", group.Name);
        Assert.Equal((3, false), (group.GroupCount, group.IsExpanded));
        Assert.Equal("Unpacked", Assert.Single(group.Tags).Text);
        Assert.False(group.CanLoad);
        var package = vm.Items[1];
        Assert.True(package.IsPackageEntry);
        Assert.Equal("Dunărea 1750 - 790 (Base2)", package.Name);
        Assert.False(package.IsNameMono);
        Assert.Equal("Package", Assert.Single(package.Tags).Text);
        Assert.StartsWith("Base2 · published 2024-08-23", package.Summary);
        Assert.Equal(4, vm.AllCount);  // counts are datasets, not rows
        Assert.Equal("4", vm.ItemsSummary);  // a collapsed group's datasets count; its header doesn't
        Assert.StartsWith("3 datasets · published ", group.Summary);

        group.ToggleCommand!.Execute(null);
        Assert.Equal(5, vm.Items.Count);
        Assert.All(vm.Items.Skip(1).Take(3), i => Assert.True(i.IsGroupChild));
        Assert.Equal("4", vm.ItemsSummary);
        // Children don't repeat the package's title.
        Assert.All(vm.Items.Skip(1).Take(3), i => Assert.False(i.HasSubtitle));
        vm.SelectedItem = vm.Items[2];
        var selected = vm.SelectedItem;

        // A text match inside a collapsed group opens it, keeping the selection.
        vm.Items[0].ToggleCommand!.Execute(null);
        vm.FilterText = "3R7D";
        Assert.Equal(3, vm.Items.Count);
        Assert.Same(selected, vm.Items.Single(i => ReferenceEquals(i, selected)));
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
