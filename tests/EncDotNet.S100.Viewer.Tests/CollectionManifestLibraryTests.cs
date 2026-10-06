using CommunityToolkit.Mvvm.Input;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>Collection manifests in the viewer: the add dialog, "Choose groups…", tree group nodes and item details.</summary>
public sealed class CollectionManifestLibraryTests : IDisposable
{
    private const string ThreeGroups = """
        { "id": "AU", "name": "Australia", "paths": ["AU"] },
        { "id": "BE", "name": "Belgium", "paths": ["BE"] },
        { "id": "PE", "name": "Peru", "paths": ["PE"] }
        """;

    private readonly LibraryTestContext _context = new();
    private readonly LibraryService _library;

    public CollectionManifestLibraryTests()
    {
        _library = _context.CreateService();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    /// <summary>
    /// Writes <c>AU/set</c> and <c>BE/set</c> (each the two-cell synthetic S-57
    /// exchange set; <c>PE</c> is missing) and a manifest over them.
    /// </summary>
    private string CreateManifest(string groups = ThreeGroups)
    {
        if (!Directory.Exists(Path.Combine(_context.Root, "AU")))
        {
            _context.CreateS57ExchangeSet(Path.Combine("AU", "set"));
            _context.CreateS57ExchangeSet(Path.Combine("BE", "set"));
        }

        return WriteManifest(groups);
    }

    private string WriteManifest(string groups)
    {
        var path = Path.Combine(_context.Root, "test.s100collection.json");
        File.WriteAllText(path, $$"""
            { "format": "encdotnet-s100-collection", "version": 1, "title": "Test", "groups": [ {{groups}} ] }
            """);
        return path;
    }

    private async Task<AddToLibraryDialogViewModel> OpenDialogAsync(string path)
    {
        var vm = new AddToLibraryDialogViewModel(_library, null);
        vm.Initialize(AddToLibraryKind.LocalManifest, path, targetCollectionId: null);
        await vm.LoadCatalogAsync();
        return vm;
    }

    [Fact]
    public async Task Dialog_lists_groups_with_paths_and_missing_markers()
    {
        var vm = await OpenDialogAsync(CreateManifest());

        Assert.True(vm.IsManifest);
        Assert.False(vm.HasManifestProblems);
        Assert.Equal("Test", vm.ManifestTitle);
        Assert.Equal("Test", vm.NewCollectionName);
        Assert.Equal(["Australia", "Belgium", "Peru"], vm.Groups.Select(g => g.Label));
        Assert.Equal(["AU", "BE", "PE"], vm.Groups.Select(g => g.PathText));
        Assert.Equal([false, false, true], vm.Groups.Select(g => g.IsMissing));
        Assert.Equal("3 groups · follows the file", vm.EverythingSummary);

        // Everything includes the missing Peru, so the summary warns about it.
        Assert.True(vm.IncludeAll);
        Assert.Contains("Peru", vm.ScopeSummary);
        Assert.True(vm.IsScopeSummaryWarning);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Ticking_a_group_selects_only_it_and_the_name_follows()
    {
        var vm = await OpenDialogAsync(CreateManifest());

        vm.Groups[0].IsSelected = true;

        Assert.True(vm.OnlySelected);
        Assert.Equal("Test — Australia", vm.NewCollectionName);
        Assert.Equal("1 of 3 groups", vm.OnlySelectedSummary);
        Assert.Equal("1 group · Australia", vm.ScopeSummary);
        Assert.False(vm.IsScopeSummaryWarning);

        vm.Groups[1].IsSelected = true;
        Assert.Equal("Test — Australia and Belgium", vm.NewCollectionName);

        vm.ConfirmCommand.Execute(null);

        var collection = Assert.Single(_library.Collections);
        Assert.Equal("Test — Australia and Belgium", collection.Definition.Name);
        var source = Assert.IsType<LocalManifestSource>(Assert.Single(collection.Sources).Definition);
        Assert.Equal(["AU", "BE"], source.Filter.Groups);
        Assert.Equal("Test — Australia and Belgium", source.DisplayName);
    }

    [Fact]
    public async Task Only_selected_with_nothing_ticked_cannot_be_added()
    {
        var vm = await OpenDialogAsync(CreateManifest());

        vm.OnlySelected = true;

        Assert.Equal("Nothing selected. Tick at least one group.", vm.ScopeSummary);
        Assert.True(vm.IsScopeSummaryWarning);
        Assert.False(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task An_edited_name_is_kept_when_the_selection_changes()
    {
        var vm = await OpenDialogAsync(CreateManifest());

        vm.NewCollectionName = "Mine";
        vm.Groups[1].IsSelected = true;

        Assert.Equal("Mine", vm.NewCollectionName);
        Assert.Equal("Added as a source named “Test — Belgium”.", vm.ExistingHint);
    }

    [Fact]
    public async Task Unreadable_manifest_lists_problems_and_reloads_once_fixed()
    {
        var path = CreateManifest("""{ "id": "AU", "paths": ["AU"] }, { "id": "au", "paths": ["BE"] }""");
        var vm = await OpenDialogAsync(path);

        Assert.True(vm.HasManifestProblems);
        Assert.False(vm.ShowsManifestGroups);
        var problem = Assert.Single(vm.ManifestProblems);
        Assert.StartsWith("line 1 · groups[1].id: duplicate", problem);
        Assert.Equal("Fix the manifest to continue", vm.ManifestFooterHint);
        Assert.False(vm.ConfirmCommand.CanExecute(null));

        WriteManifest(ThreeGroups);
        await ((IAsyncRelayCommand)vm.ReloadManifestCommand).ExecuteAsync(null);

        Assert.False(vm.HasManifestProblems);
        Assert.Equal(3, vm.Groups.Count);
        Assert.True(vm.ConfirmCommand.CanExecute(null));
    }

    [Fact]
    public async Task Choose_groups_edits_the_source_in_place()
    {
        var path = CreateManifest();
        var source = new LocalManifestSource(Guid.NewGuid(), "Test — Belgium", path, new LocalManifestFilter { Groups = ["BE"] });
        var collection = _library.AddCollection("Test — Belgium", [source]);

        var vm = new AddToLibraryDialogViewModel(_library, null);
        vm.InitializeEdit(collection.Id, source);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsEditing);
        Assert.False(vm.ShowsTarget);
        Assert.Equal("Save", vm.PrimaryButtonText);
        Assert.Equal("Choose groups", vm.Title);
        Assert.True(vm.OnlySelected);
        Assert.Equal(["BE"], vm.Groups.Where(g => g.IsSelected).Select(g => g.Value));

        vm.Groups[0].IsSelected = true;
        vm.ConfirmCommand.Execute(null);

        var updated = Assert.Single(Assert.Single(_library.Collections).Sources).Definition;
        var manifest = Assert.IsType<LocalManifestSource>(updated);
        Assert.Equal(source.Id, manifest.Id);
        Assert.Equal("Test — Belgium", manifest.DisplayName);
        Assert.Equal(["AU", "BE"], manifest.Filter.Groups);
    }

    [Fact]
    public async Task Tree_shows_a_node_per_group_that_lists_only_its_datasets()
    {
        _library.Initialize();
        var path = CreateManifest();
        _library.AddCollection("Test", [new LocalManifestSource(Guid.NewGuid(), "Test", path, LocalManifestFilter.All)]);
        await _library.WhenIdle();
        using var panel = new LibraryPanelViewModel(
            _library, new NullImporter(), new NullLoader(), new NullDownloader(), action => action());

        var source = Assert.Single(Assert.Single(panel.Nodes).Children);
        Assert.Equal("JSON", source.KindTag);
        Assert.Equal("test.s100collection.json", source.SecondaryName);
        Assert.True(source.CanChooseGroups);
        Assert.Equal(["Australia", "Belgium", "Peru"], source.Children.Select(g => g.Name));
        Assert.Equal(["2", "2", "0"], source.Children.Select(g => g.Status));
        Assert.All(source.Children, g => Assert.False(g.CanRemove || g.CanRename || g.HasKindTag));

        var peru = source.Children[2];
        Assert.Equal("Path not found", peru.StatusLine);
        Assert.True(peru.IsStatusWarning);

        panel.SelectedNode = source.Children[1];
        Assert.Equal(2, panel.Items.Count);
        Assert.All(panel.Items, i => Assert.Equal("BE", i.Item.Properties["group"]));

        var group = panel.Items[0].Details.Single(g => g.Title == "Source").Fields;
        Assert.Contains(group, f => f.Label == "Group" && f.Value == "Belgium (BE)");
        Assert.DoesNotContain(group, f => f.Label is "group" or "groupName" or "Group name");

        // Re-indexing keeps the group node (and the selection).
        var belgium = source.Children[1];
        _library.Refresh();
        await _library.WhenIdle();
        panel.Sync();
        Assert.Same(belgium, panel.SelectedNode);
    }

    [Fact]
    public async Task A_single_group_manifest_has_no_group_nodes()
    {
        _library.Initialize();
        var path = CreateManifest();
        _library.AddCollection("Test", [new LocalManifestSource(
            Guid.NewGuid(), null, path, new LocalManifestFilter { Groups = ["AU"] })]);
        await _library.WhenIdle();
        using var panel = new LibraryPanelViewModel(
            _library, new NullImporter(), new NullLoader(), new NullDownloader(), action => action());

        var source = Assert.Single(Assert.Single(panel.Nodes).Children);
        Assert.Empty(source.Children);
        Assert.Null(source.SecondaryName);
        Assert.Equal("2", source.Status);
    }

    [Fact]
    public void Manifest_files_are_classified_for_adding()
    {
        var manifest = CreateManifest();
        var otherJson = Path.Combine(_context.Root, "other.json");
        File.WriteAllText(otherJson, """{ "format": "encdotnet-s100-feed" }""");

        Assert.Equal(AddToLibraryKind.LocalManifest, LibraryImportCoordinator.Classify(manifest));
        Assert.Equal(AddToLibraryKind.ExchangeSet, LibraryImportCoordinator.Classify(otherJson));
        Assert.Equal(AddToLibraryKind.ExchangeSet, LibraryImportCoordinator.Classify(Path.Combine(_context.Root, "AU", "set")));
        Assert.Equal(AddToLibraryKind.Folder, LibraryImportCoordinator.Classify(_context.Root));
    }

    private sealed class NullLoader : ILibraryLoader
    {
        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) => default;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryLoadResult(0, 0));
    }

    private sealed class NullDownloader : ILibraryDownloader
    {
        public event EventHandler? Changed { add { } remove { } }

        public CollectionItem Localize(CollectionItem item) => item;

        public bool IsOutdated(CollectionItem item) => false;

        public bool CanDownload(CollectionItem item) => false;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryDownloadResult(0, 0, false));
    }

    private sealed class NullImporter : ILibraryImporter
    {
        public Task AddFolderAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddExchangeSetZipAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddOnlineCatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddSharedFeedAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddKnownCatalogueAsync(EncDotNet.S100.Collections.KnownSources.KnownCatalogueSource source, Guid? targetCollectionId) =>
            Task.CompletedTask;

        public Task AddS128CatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddCollectionManifestAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source) => Task.CompletedTask;

        public Task AddPathAsync(string path, Guid? targetCollectionId) => Task.CompletedTask;

        public bool IsInLibrary(string path) => false;
    }
}
