using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// A remote S-100 catalogue (NOAA's S-102 on AWS, #685) at scale: updates are
/// summarised on the source and the bulk bar rather than tagged on each tile,
/// and zoomed out the map draws one outline per area (handoff B1, B4, B6, E2).
/// </summary>
public sealed class RemoteCatalogueLibraryTests : IDisposable
{
    private static readonly Uri CatalogUri = new("https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/S100_ROOT/CATALOG.XML");

    private readonly LibraryTestContext _context = new();
    private readonly TileDownloader _downloader;
    private readonly LibraryService _library;
    private FeedHealth? _health;

    public RemoteCatalogueLibraryTests()
    {
        _downloader = new TileDownloader(Path.Combine(_context.Root, "downloads"));
        _library = _context.CreateService(new CollectionIndexer([new TileIndexer()]));
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    /// <summary>Four tiles: two port tiles in Boston (edition 3), two transit tiles in Wilmington (edition 5).</summary>
    private static CollectionItem Tile(string name, string folder, int edition, string purpose, double south, double west) => new()
    {
        Key = folder + "/" + name,
        ProductSpec = "S-102",
        Name = name,
        Title = folder.Replace('/', ','),
        Edition = edition,
        Update = 0,
        IssueDate = new DateOnly(2026, 8, 14),
        Bounds = new GeoBounds(south, west, south + 0.1, west + 0.1),
        Location = new RemoteItemLocation(new Uri(CatalogUri, $"../{folder}/{name}262247.h5"), 3_000_000),
        Properties = new Dictionary<string, string>
        {
            [RemoteS100Catalogue.FolderProperty] = folder,
            [RemoteS100Catalogue.NavigationPurposeProperty] = purpose,
            [RemoteS100Catalogue.GridResolutionProperty] = purpose == "port" ? "4" : "16",
            ["notForNavigation"] = "true",
        },
    };

    private async Task<LibraryPanelViewModel> PanelAsync()
    {
        _library.Initialize();
        _library.AddCollection("S-102", [new S100CatalogueFeedSource(Guid.NewGuid(), null, CatalogUri, S100CatalogueFilter.All)]);
        await _library.WhenIdle();
        var panel = new LibraryPanelViewModel(
            _library, new NullImporter(), new NullLoader(), _downloader, action => action(), feedHealth: _ => _health);
        panel.Sync();
        panel.SelectedNode = Source(panel);
        return panel;
    }

    private static LibraryNodeViewModel Source(LibraryPanelViewModel panel) => Assert.Single(Assert.Single(panel.Nodes).Children);

    [Fact]
    public async Task The_source_counts_what_is_local_and_what_has_updates()
    {
        using var panel = await PanelAsync();
        var source = Source(panel);
        Assert.Equal(["Boston", "Wilmington"], source.Children.Select(c => c.Name));
        Assert.Contains("nothing local · not for navigation", source.StatusLine, StringComparison.Ordinal);
        Assert.Equal(LibraryNodeStatusKind.Info, source.StatusKind);

        _downloader.Have("102US005MA1AA", 3);
        _downloader.Have("102US005MA1AB", 3);
        _downloader.RaiseChanged();
        Assert.Contains("2 of 4 local", source.StatusLine, StringComparison.Ordinal);
        Assert.Equal(LibraryNodeStatusKind.Ok, source.StatusKind);

        _downloader.Have("102US005MA1AB", 2);
        _downloader.RaiseChanged();
        Assert.Contains("1 updates", source.StatusLine, StringComparison.Ordinal);
        Assert.Equal(LibraryNodeStatusKind.Warning, source.StatusKind);

        // A one-source collection shows its source's line.
        Assert.Equal(source.StatusLine, Assert.Single(panel.Nodes).StatusLine);
    }

    [Fact]
    public async Task An_outdated_tile_has_an_amber_swatch_and_says_what_is_online_without_a_tag()
    {
        _downloader.Have("102US005MA1AB", 2);
        using var panel = await PanelAsync();

        var outdated = panel.Items.Single(i => i.Name == "102US005MA1AB");
        Assert.Equal(LibraryPrimaryAvailability.Update, outdated.PrimaryAvailability);
        Assert.Empty(outdated.Tags);
        Assert.Equal("S-102 · Port 4 m · Ed 2 → Ed 3 online · " + LibraryItemViewModel.FormatBytes(3_000_000), outdated.Summary);

        var online = panel.Items.Single(i => i.Name == "102US004SC1AA");
        Assert.Equal(LibraryPrimaryAvailability.Online, online.PrimaryAvailability);
        Assert.Equal("S-102 · Transit 16 m · Ed 5 · 2026-08-14 · " + LibraryItemViewModel.FormatBytes(3_000_000), online.Summary);
    }

    [Fact]
    public async Task With_updates_the_bulk_bar_updates_only_the_downloaded_tiles()
    {
        _downloader.Have("102US005MA1AA", 3);
        _downloader.Have("102US005MA1AB", 2);
        using var panel = await PanelAsync();

        Assert.True(panel.IsUpdateMode);
        Assert.Equal("1 updates · " + LibraryItemViewModel.FormatBytes(3_000_000), panel.BulkSummary);
        Assert.Equal("2 of 4 local · newer editions online", panel.BulkScope);
        Assert.Equal("Update 1", panel.DownloadListedText);

        panel.DownloadListedCommand.Execute(null);

        Assert.Equal(["102US005MA1AB"], _downloader.Requested);
    }

    [Fact]
    public async Task Without_updates_the_bulk_bar_downloads_what_is_online()
    {
        _downloader.Have("102US005MA1AA", 3);
        using var panel = await PanelAsync();

        Assert.False(panel.IsUpdateMode);
        Assert.StartsWith("3 to download", panel.BulkSummary, StringComparison.Ordinal);
        Assert.Equal("Download 3", panel.DownloadListedText);
    }

    [Fact]
    public async Task Offline_the_bulk_bar_lists_what_is_local_and_cannot_download()
    {
        _downloader.Have("102US005MA1AA", 3);
        _downloader.Have("102US005MA1AB", 2);
        _health = new FeedHealth(DateTimeOffset.UtcNow, "Connection refused", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-2));
        using var panel = await PanelAsync();

        Assert.True(panel.IsBulkOffline);
        Assert.Equal("Offline · 2 local", panel.BulkSummary);
        Assert.False(panel.HasDownloadable);
        Assert.False(panel.DownloadListedCommand.CanExecute(null));
        Assert.EndsWith("· 2 local", Source(panel).StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Zoomed_out_tiles_are_drawn_as_areas_styled_by_their_tiles()
    {
        _downloader.Have("102US005MA1AA", 3);
        _downloader.Have("102US005MA1AB", 2);
        using var panel = await PanelAsync();

        var areas = LibraryCoverageOverlayController.Areas(panel.Items, scale: 5_000_000);
        Assert.Equal(["Northeast/Boston", "Southeast/Wilmington"], areas.Select(a => a.Area.Folder).Order());
        var boston = areas.Single(a => a.Area.Folder == "Northeast/Boston");
        Assert.Equal(LibraryPrimaryAvailability.Update, LibraryCoverageOverlayController.AreaState(boston.Items));
        Assert.Equal(LibraryPrimaryAvailability.Online,
            LibraryCoverageOverlayController.AreaState(areas.Single(a => a.Area.Folder == "Southeast/Wilmington").Items));

        // The two adjacent Boston tiles are one shape; zoomed out no tile is outlined (or tapped) on its own.
        Assert.Single(boston.Area.Rings);
        Assert.Empty(LibraryCoverageOverlayController.Candidates(panel.Items, scale: 5_000_000));

        // Zoomed in, tiles are drawn as before.
        Assert.Empty(LibraryCoverageOverlayController.Areas(panel.Items, scale: 100_000));
        Assert.Equal(4, LibraryCoverageOverlayController.Candidates(panel.Items, scale: 100_000).Count());
    }

    [Fact]
    public async Task Selecting_an_area_selects_its_node()
    {
        using var panel = await PanelAsync();
        var source = Source(panel);

        panel.SelectArea(source.Id, "Southeast/Wilmington");

        Assert.Same(source.Children.Single(c => c.GroupId == "Southeast/Wilmington"), panel.SelectedNode);
        Assert.True(source.IsExpanded);
        Assert.Equal(2, panel.Items.Count);
    }

    private sealed class TileIndexer : ICollectionSourceIndexer
    {
        public bool CanIndex(CollectionSource source) => source is S100CatalogueFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, null,
            [
                Tile("102US005MA1AA", "Northeast/Boston", 3, "port", 42.3, -71.0),
                Tile("102US005MA1AB", "Northeast/Boston", 3, "port", 42.3, -70.9),
                Tile("102US004SC1AA", "Southeast/Wilmington", 5, "transit", 33.3, -78.0),
                Tile("102US004SC1AB", "Southeast/Wilmington", 5, "transit", 33.3, -77.7),
            ], [])
            {
                Groups = [new SourceIndexGroup("Northeast/Boston", "Boston"), new SourceIndexGroup("Southeast/Wilmington", "Wilmington")],
                PublishedAt = new DateTimeOffset(2026, 9, 30, 18, 0, 0, TimeSpan.Zero),
            });
    }

    /// <summary>Tracks which tiles are downloaded (as real files), at which edition.</summary>
    private sealed class TileDownloader(string root) : ILibraryDownloader
    {
        private readonly Dictionary<string, int> _editions = [];

        public List<string> Requested { get; } = [];

        public event EventHandler? Changed;

        public void Have(string name, int edition)
        {
            Directory.CreateDirectory(Path.Combine(root, name));
            File.WriteAllBytes(Path.Combine(root, name, name + ".h5"), [1]);
            _editions[name] = edition;
        }

        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public CollectionItem Localize(CollectionItem item) => _editions.ContainsKey(item.Name)
            ? item with { Location = new LocalItemLocation(Path.Combine(root, item.Name), item.Name + ".h5", []) }
            : item;

        public bool IsOutdated(CollectionItem item) => _editions.TryGetValue(item.Name, out var local) && local < item.Edition;

        public int? LocalEditionOf(CollectionItem item) => _editions.TryGetValue(item.Name, out var local) ? local : null;

        public bool CanDownload(CollectionItem item) => item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
        {
            Requested.AddRange(items.Select(i => i.Name));
            return Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
        }
    }

    private sealed class NullLoader : ILibraryLoader
    {
        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) => default;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryLoadResult(0, 0));
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
