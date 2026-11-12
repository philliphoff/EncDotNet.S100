using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The Library tree on its own (#1396): nodes, selection, node actions and status lines.</summary>
public sealed class LibraryTreeViewModelTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly FakeLibraryLoader _loader = new();
    private readonly FakeLibraryDownloader _downloader = new();

    public LibraryTreeViewModelTests()
    {
        _library = _context.CreateService(CollectionIndexer.CreateDefault(feeds: [new RemoteIndexer()]));
        _library.Initialize();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private LibraryTreeViewModel Tree(TestLibrary library) =>
        new(_library, new RecordingLibraryImporter(), new LibraryNodeStatus(_downloader, library.Download, sync: null, feedHealth: null, TimeProvider.System),
            timeFormat: null, action => action(), TimeProvider.System, identities: null, openKeys: null);

    private async Task<LibrarySource> OnlineAsync(string name = "Online")
    {
        _library.AddCollection(name, [new ChartCatalogsFeedSource(
            Guid.NewGuid(), null, new Uri("https://example.test/TEST_Catalog.xml"), ChartCatalogsFilter.All)]);
        await _library.WhenIdle();
        return _library.Collections.Single(c => c.Definition.Name == name).Sources.Single();
    }

    [Fact]
    public async Task The_tree_follows_the_Library_and_selects_the_first_collection()
    {
        var library = new TestLibrary(_library, _loader, _downloader);
        using var tree = Tree(library);
        Assert.True(tree.IsEmpty);
        var selections = 0;
        tree.SelectedNodeChanged += (_, _) => selections++;

        await OnlineAsync("First");
        await OnlineAsync("Second");

        Assert.Equal(["First", "Second"], tree.Nodes.Select(n => n.Name));
        Assert.Same(tree.Nodes[0], tree.SelectedNode);
        Assert.Equal(1, selections);
        Assert.False(tree.IsEmpty);

        // Removing the selected collection selects the next.
        tree.RemoveCommand.Execute(null);
        await _library.WhenIdle();
        Assert.Equal(["Second"], tree.Nodes.Select(n => n.Name));
        Assert.Same(tree.Nodes[0], tree.SelectedNode);
    }

    [Fact]
    public async Task A_download_started_outside_the_panel_shows_on_its_sources_status_line()
    {
        var source = await OnlineAsync();
        var library = new TestLibrary(_library, _loader, _downloader);
        using var tree = Tree(library);
        var node = tree.Nodes.Single().Children.Single();
        Assert.Null(node.DownloadStatus);

        // As the Timeline or MCP library_action would: through the workflow, not the panel.
        _downloader.Progress = new LibraryDownloadProgress(0, 0, 2, 0, 2_000);
        _downloader.Status = new LibraryDownloadItemStatus(LibraryDownloadItemState.Running, 0, 1_000);
        await library.Download.RunAsync([.. source.Index!.Items.Select(i => (i, source))], load: false, TestContext.Current.CancellationToken);
        tree.RefreshDownloadStatus();

        Assert.Equal(LibraryNodeText.Downloading(1, 2, 2_000), node.DownloadStatus);
        Assert.Equal(node.DownloadStatus, tree.Nodes.Single().DownloadStatus);

        // Once nothing is downloading, the line goes.
        _downloader.Progress = null;
        tree.RefreshDownloadStatus();
        Assert.Null(node.DownloadStatus);
    }

    /// <summary>Indexes two online cells with a stated layout.</summary>
    private sealed class RemoteIndexer : ICollectionSourceIndexer
    {
        public bool CanIndex(CollectionSource source) => source is ChartCatalogsFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
        {
            CollectionItem Cell(string name) => new()
            {
                Key = name,
                ProductSpec = "S-57",
                Name = name,
                Location = new RemoteItemLocation(new Uri($"https://example.test/{name}.zip"), 1_000, null, "community/TEST", null, new PackageLayout($"{name}.000", [])),
            };
            return ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [Cell("RO1"), Cell("RO2")], []));
        }
    }
}
