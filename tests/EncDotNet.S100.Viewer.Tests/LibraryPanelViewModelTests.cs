using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class LibraryPanelViewModelTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly LibraryService _library;
    private readonly RecordingImporter _importer = new();
    private readonly FakeLoader _loader = new();
    private readonly FakeDownloader _downloader = new();

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

        vm.AddNoaaFeedCommand.Execute(null);

        Assert.Equal(("noaa", (Guid?)collection.Id), _importer.Calls.Single());
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

    [Fact]
    public async Task Tapping_the_map_lists_the_datasets_there_and_cycles_on_repeat()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();
        var bounds = vm.Items.Select(i => i.Item.Bounds!.Value).ToArray();
        // A point inside both synthetic cells' footprints, if they overlap;
        // otherwise inside the first.
        var both = bounds[0].Intersects(bounds[1]);
        var point = new GeoPosition(
            (Math.Max(bounds[0].South, bounds[1].South) + Math.Min(bounds[0].North, bounds[1].North)) / 2,
            (Math.Max(bounds[0].West, bounds[1].West) + Math.Min(bounds[0].East, bounds[1].East)) / 2);
        if (!both)
            point = new GeoPosition((bounds[0].South + bounds[0].North) / 2, (bounds[0].West + bounds[0].East) / 2);

        Assert.True(vm.SelectAt(point));

        Assert.True(vm.HasLocation);
        Assert.Contains(vm.SelectedItem!, vm.Items);
        var first = vm.SelectedItem!.Name;
        if (vm.Items.Count > 1)
        {
            vm.SelectAt(point);
            Assert.NotEqual(first, vm.SelectedItem!.Name);
        }

        vm.ClearLocationCommand.Execute(null);
        Assert.False(vm.HasLocation);
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public async Task Tapping_where_nothing_is_covered_changes_nothing()
    {
        await AddS57CollectionAsync();
        using var vm = CreateViewModel();

        Assert.False(vm.SelectAt(new GeoPosition(-60, 0)));
        Assert.False(vm.HasLocation);
        Assert.Equal(2, vm.Items.Count);
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
        Assert.Equal("LOADED", vm.Items[0].AvailabilityText);
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

    private sealed class FakeDownloader : ILibraryDownloader
    {
        public bool Downloaded { get; set; }

        public bool Outdated { get; set; }

        public bool CanDownloadAll { get; set; }

        public int Downloads { get; private set; }

        public event EventHandler? Changed;

        public CollectionItem Localize(CollectionItem item) =>
            Downloaded && item.Location is RemoteItemLocation
                ? item with { Location = new LocalItemLocation("/tmp/x", "x.000", []) }
                : item;

        public bool IsOutdated(CollectionItem item) => Outdated;

        public bool CanDownload(CollectionItem item) => CanDownloadAll || item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
        {
            Downloads += items.Count;
            Changed?.Invoke(this, EventArgs.Empty);
            return Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
        }
    }

    private sealed class FakeLoader : ILibraryLoader
    {
        public LibraryLoadState State { get; set; }

        public List<(bool Defer, int Count)> Calls { get; } = [];

        public event EventHandler? Changed;

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public LibraryLoadState StateOf(CollectionItem item) => State;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default)
        {
            Calls.Add((defer, items.Count));
            return Task.FromResult(new LibraryLoadResult(items.Count, 0));
        }
    }

    private sealed class RecordingImporter : ILibraryImporter
    {
        public List<(string Kind, Guid? Target)> Calls { get; } = [];

        public Task AddFolderAsync(Guid? targetCollectionId) => Record("folder", targetCollectionId);

        public Task AddExchangeSetZipAsync(Guid? targetCollectionId) => Record("zip", targetCollectionId);

        public Task AddNoaaFeedAsync(Guid? targetCollectionId) => Record("noaa", targetCollectionId);

        public Task AddS128CatalogueAsync(Guid? targetCollectionId) => Record("s128", targetCollectionId);

        public Task AddPathAsync(string path, Guid? targetCollectionId) => Record("path", targetCollectionId);

        public bool IsInLibrary(string path) => false;

        private Task Record(string kind, Guid? target)
        {
            Calls.Add((kind, target));
            return Task.CompletedTask;
        }
    }
}
