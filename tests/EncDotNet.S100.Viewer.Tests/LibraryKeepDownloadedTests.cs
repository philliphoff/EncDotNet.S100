using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// "Keep downloaded" for online sources (#809): the Library tree's toggle and
/// the Add to Library option set <see cref="CollectionSource.Sync"/>.
/// </summary>
public sealed class LibraryKeepDownloadedTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly LibrarySync _sync;

    public LibraryKeepDownloadedTests()
    {
        // Not initialized: no background indexing (and no network) runs.
        _library = _context.CreateService();
        _sync = new LibrarySync(_library, new LibraryDownloads(
            LibraryDownloads.ManagedFolders(new HttpClient(), Path.Combine(_context.Root, "downloads"))));
    }

    public void Dispose()
    {
        _sync.Dispose();
        _library.Dispose();
        _context.Dispose();
    }

    private LibraryPanelViewModel Panel() =>
        new(_library, new RecordingLibraryImporter(), new FakeLibraryLoader(), new FakeLibraryDownloader(), a => a(), sync: _sync);

    [Fact]
    public void An_online_source_can_be_kept_downloaded_from_its_menu()
    {
        var noaa = new NoaaEncFeedSource(Guid.NewGuid(), "NOAA", NoaaEncFeedSource.DefaultCatalogUri, NoaaEncFilter.All);
        var folder = new LocalFolderSource(Guid.NewGuid(), "Charts", _context.Root);
        _library.AddCollection("Mixed", [noaa, folder]);
        using var panel = Panel();
        var collection = Assert.Single(panel.Nodes);

        panel.SelectedNode = collection.Children.Single(n => n.Source?.Id == folder.Id);
        Assert.False(panel.CanSyncSelected);  // a local folder is always current

        panel.SelectedNode = collection.Children.Single(n => n.Source?.Id == noaa.Id);
        Assert.True(panel.CanSyncSelected);
        Assert.False(panel.SelectedSyncs);

        panel.ToggleSyncCommand.Execute(null);

        var stored = _library.Collections.Single().Sources.Single(s => s.Id == noaa.Id).Definition;
        Assert.True(stored.Sync);
        Assert.False(_library.Collections.Single().Sources.Single(s => s.Id == folder.Id).Definition.Sync);

        // On the collection: the toggle applies to its online sources only.
        panel.SelectedNode = panel.Nodes.Single();
        Assert.True(panel.SelectedSyncs);
        panel.ToggleSyncCommand.Execute(null);
        Assert.False(_library.Collections.Single().Sources.Single(s => s.Id == noaa.Id).Definition.Sync);
    }

    [Fact]
    public async Task Add_to_library_offers_keep_downloaded_for_online_feeds()
    {
        var feedUri = new Uri("http://machine.test:8100/feed.json");
        var known = EncDotNet.S100.Collections.KnownSources.KnownCatalogueSources.FromUrl(
            feedUri, EncDotNet.S100.Collections.KnownSources.KnownCatalogueFormat.S100Feed, "Shared charts");
        var feed = new EncDotNet.S100.Collections.Feeds.S100FeedDocument(
            EncDotNet.S100.Collections.Feeds.S100Feed.FormatName, 1, "Shared charts",
            new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero), "f1",
            [new CollectionItem { Key = "A", ProductSpec = "S-101", Name = "A", Location = new RemoteItemLocation(new Uri(feedUri, "items/A.zip"), 1024) }]);
        var vm = new AddToLibraryDialogViewModel(_library, null, loadS100Feed: (_, _) => Task.FromResult(feed));

        vm.Initialize(known, targetCollectionId: null);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);
        Assert.True(vm.CanKeepDownloaded);
        Assert.False(vm.KeepDownloaded);  // off by default

        vm.KeepDownloaded = true;
        Assert.True(vm.BuildSource().Sync);

        vm.Initialize(AddToLibraryKind.Folder, _context.Root, targetCollectionId: null);
        Assert.False(vm.CanKeepDownloaded);
        Assert.False(vm.BuildSource().Sync);
    }
}
