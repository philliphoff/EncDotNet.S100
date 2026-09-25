using EncDotNet.S100.Collections;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class LibraryPanelViewModelTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly LibraryService _library;
    private readonly RecordingImporter _importer = new();

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

    private LibraryPanelViewModel CreateViewModel() => new(_library, _importer, action => action());

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
