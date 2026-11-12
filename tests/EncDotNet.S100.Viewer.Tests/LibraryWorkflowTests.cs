using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Library's open and download workflows (#1394), which the panel, the
/// Timeline and MCP <c>library_action</c> share.
/// </summary>
public sealed class LibraryWorkflowTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly LabelLoader _loader = new();
    private readonly FakeLibraryDownloader _downloader = new();

    public LibraryWorkflowTests()
    {
        _library = _context.CreateService(CollectionIndexer.CreateDefault(feeds: [new RemoteIndexer()]));
        _library.Initialize();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private async Task<LibrarySource> OnlineAsync()
    {
        _library.AddCollection("Online", [new ChartCatalogsFeedSource(
            Guid.NewGuid(), null, new Uri("https://example.test/TEST_Catalog.xml"), ChartCatalogsFilter.All)]);
        await _library.WhenIdle();
        return _library.Collections.Single(c => !c.IsSession).Sources.Single();
    }

    private static IReadOnlyList<(CollectionItem, LibrarySource)> Items(LibrarySource source) =>
        [.. source.Index!.Items.Select(item => (item, source))];

    [Fact]
    public async Task A_download_with_load_opens_the_local_copies_under_their_sources_row()
    {
        var source = await OnlineAsync();
        var library = new TestLibrary(_library, _loader, _downloader);
        IReadOnlyList<(CollectionItem Item, LibrarySource Source)>? started = null;
        library.Download.Starting += (_, items) => started = items;
        _downloader.Downloaded = true;

        var outcome = await library.Download.RunAsync(Items(source), load: true, TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Download.Downloaded);
        Assert.Equal(2, outcome.Load!.Opened);
        Assert.Equal(2, started!.Count);
        Assert.All(_loader.Opened, opened =>
        {
            Assert.IsType<LocalItemLocation>(opened.Item.Location);
            Assert.Equal(new LibrarySourceLabel(source.Id, library.Names.Of(source.Id)), opened.Label);
        });
        Assert.Equal(2, _loader.Opened.Count);
    }

    [Fact]
    public async Task A_download_only_opens_nothing()
    {
        var source = await OnlineAsync();
        var library = new TestLibrary(_library, _loader, _downloader);
        _downloader.Downloaded = true;

        var outcome = await library.Download.RunAsync(Items(source), load: false, TestContext.Current.CancellationToken);

        Assert.Equal(2, outcome.Download.Downloaded);
        Assert.Null(outcome.Load);
        Assert.Empty(_loader.Opened);
    }

    [Fact]
    public async Task Opening_labels_items_with_their_source_and_leaves_items_without_one_unlabelled()
    {
        var source = await OnlineAsync();
        var library = new TestLibrary(_library, _loader, _downloader);
        var loose = new CollectionItem
        {
            Key = "loose",
            ProductSpec = "S-57",
            Name = "loose",
            Location = new LocalItemLocation(_context.Root, "loose.000", []),
        };

        await library.Open.RunAsync([(source.Index!.Items[0], source), (loose, null)], defer: true, TestContext.Current.CancellationToken);

        Assert.True(_loader.Deferred);
        Assert.Equal(source.Id, _loader.Opened[0].Label!.SourceId);
        Assert.Null(_loader.Opened[1].Label);
    }

    [Fact]
    public async Task Source_names_match_the_Library_tree_and_fall_back_to_the_id()
    {
        var source = await OnlineAsync();
        using var panel = TestLibrary.Panel(_library, new RecordingLibraryImporter(), _loader, _downloader, action => action(), action => action(), TimeProvider.System, feedHealth: null, notifications: null, viewTime: null, timeFormat: null, sync: null);
        var names = new LibrarySourceNames(_library);

        var node = panel.Tree.Nodes.SelectMany(c => c.Children.Prepend(c)).First(n => !n.IsGroup && n.Source?.Id == source.Id);
        Assert.Equal(node.Name, names.Of(source.Id));
        var unknown = Guid.NewGuid();
        Assert.Equal(unknown.ToString("N")[..8], names.Of(unknown));
    }

    /// <summary>Indexes two online cells with a stated layout (not packages).</summary>
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

    /// <summary>Records each opened item with the Datasets row it opens under.</summary>
    private sealed class LabelLoader : ILibraryLoader
    {
        public List<(CollectionItem Item, LibrarySourceLabel? Label)> Opened { get; } = [];

        public bool Deferred { get; private set; }

        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) => LibraryLoadState.None;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
            LoadAsync(items, defer, _ => null, cancellationToken);

        public Task<LibraryLoadResult> LoadAsync(
            IReadOnlyList<CollectionItem> items, bool defer, Func<CollectionItem, LibrarySourceLabel?> sourceOf, CancellationToken cancellationToken = default)
        {
            Deferred = defer;
            Opened.AddRange(items.Select(item => (item, sourceOf(item))));
            return Task.FromResult(new LibraryLoadResult(items.Count, 0));
        }
    }
}
