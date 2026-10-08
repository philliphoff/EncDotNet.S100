using System.Text;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;
using Microsoft.Extensions.Time.Testing;
using FakeSecomServer = EncDotNet.S100.Collections.Tests.SecomTests.FakeSecomServer;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Synced SECOM sources (issue #807): every listed object is kept downloaded,
/// copies the service no longer lists are pruned, and the source re-indexes
/// once so downloaded objects get their bounds.
/// </summary>
public sealed class LibrarySyncTests : IDisposable
{
    private static readonly Uri ServiceUri = new("https://secom.test/api/secom");

    private readonly LibraryContext _context = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (var disposable in _disposables.AsEnumerable().Reverse())
            disposable.Dispose();
        _context.Dispose();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private string Downloads => Path.Combine(_context.Root, "downloads");

    /// <summary>The service's managed folder, normalised as the downloader keeps it (the folder name uses '/').</summary>
    private string ServiceFolder => Path.GetFullPath(Path.Combine(Downloads, SecomSourceIndexer.DownloadFolderFor(ServiceUri)));

    private (CollectionLibrary Library, LibrarySync Sync, FakeSecomServer Server) Create(
        int objects = 3, LibrarySyncOptions? options = null)
    {
        var server = new FakeSecomServer(SecomTests.Summaries(objects))
        {
            DefaultData = reference => Encoding.UTF8.GetBytes($"<S124:Dataset id=\"{reference}\"/>"),
        };
        var http = new HttpClient(server);
        DatasetProbe probe = (path, _) => new DatasetMetadata
        {
            Spec = new SpecRef("S-124", default),
            Extent = new BoundingBox(49, -124, 50, -123),
        };
        var indexer = CollectionIndexer.CreateDefault(feeds:
        [
            new SecomSourceIndexer(http, Path.Combine(_context.Root, "cache"), Downloads, probe, timeProvider: _time),
        ]);
        var library = _context.CreateLibrary(indexer);
        var downloads = new LibraryDownloads(LibraryDownloads.ManagedFolders(http, Downloads));
        var sync = new LibrarySync(library, downloads, options);
        _disposables.Add(library);
        _disposables.Add(sync);
        return (library, sync, server);
    }

    private static SecomSource Source(bool sync = true, SecomFilter? filter = null) =>
        new(Guid.NewGuid(), null, ServiceUri, filter ?? SecomFilter.All) { Sync = sync };

    /// <summary>Waits until indexing and syncing have both stopped (a sync re-indexes, which may sync again).</summary>
    private static async Task SettleAsync(CollectionLibrary library, LibrarySync sync)
    {
        for (var i = 0; i < 10; i++)
        {
            await library.WhenIdle().WaitAsync(Ct);
            await sync.WhenIdle().WaitAsync(Ct);
            await library.WhenIdle().WaitAsync(Ct);
            if (!sync.IsSyncing && !library.Collections.Any(c => c.IsIndexing))
                return;
        }
    }

    /// <summary>Re-reads the service: the indexer reuses a listing for a minute.</summary>
    private async Task RefreshAsync(CollectionLibrary library, LibrarySync sync)
    {
        _time.Advance(TimeSpan.FromMinutes(2));
        library.Refresh();
        await SettleAsync(library, sync);
    }

    private static IReadOnlyList<CollectionItem> ItemsOf(CollectionLibrary library, SecomSource source) =>
        library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == source.Id).Index!.Items;

    [Fact]
    public async Task A_synced_source_downloads_every_object_and_settles_with_bounds()
    {
        var (library, sync, server) = Create();
        var source = Source();
        library.Initialize();
        library.AddCollection("Warnings", [source]);

        await SettleAsync(library, sync);

        var status = sync.StatusOf(source.Id)!;
        Assert.Equal((3, 3, 0), (status.Local, status.Listed, status.Failed));
        Assert.Equal(["NW-0001-26", "NW-0002-26", "NW-0003-26"], Directory.EnumerateDirectories(ServiceFolder).Select(Path.GetFileName).Order());
        Assert.All(ItemsOf(library, source), i => Assert.NotNull(i.Bounds));

        // Settled: a refresh with nothing new downloads nothing more.
        var gets = server.Requests.Count(r => r.AbsolutePath.EndsWith("/object", StringComparison.Ordinal));
        await RefreshAsync(library, sync);
        Assert.Equal(gets, server.Requests.Count(r => r.AbsolutePath.EndsWith("/object", StringComparison.Ordinal)));
        Assert.Equal(0, sync.StatusOf(source.Id)!.Downloaded);
    }

    [Fact]
    public async Task Objects_no_longer_listed_or_cancelled_are_pruned_unless_another_source_lists_them()
    {
        var (library, sync, server) = Create(objects: 4);
        var synced = Source();
        library.Initialize();
        library.AddCollection("Warnings", [synced]);
        await SettleAsync(library, sync);
        Assert.Equal(4, Directory.EnumerateDirectories(ServiceFolder).Count());

        // A plain (not synced) source of the same service still lists object 4.
        var scoped = Source(sync: false, new SecomFilter { ProductSpecs = ["S-124"] });
        library.AddCollection("Other", [scoped]);
        await SettleAsync(library, sync);

        // The service drops object 3 and cancels object 2; the plain source keeps listing 4 too.
        var summaries = SecomTests.Summaries(4).ToArray();
        summaries[1]["info_status"] = "CANCELLED";
        server.Summaries = [summaries[0], summaries[1], summaries[3]];
        await RefreshAsync(library, sync);

        // The last status is the settling pass after the prune's re-index.
        var status = sync.StatusOf(synced.Id)!;
        Assert.Equal(["NW-0001-26", "NW-0004-26"], Directory.EnumerateDirectories(ServiceFolder).Select(Path.GetFileName).Order());
        Assert.Equal(2, status.Listed);
    }

    [Fact]
    public async Task A_copy_in_use_is_not_pruned()
    {
        var inUse = new HashSet<string>(StringComparer.Ordinal);
        var (library, sync, server) = Create(objects: 2, new LibrarySyncOptions { IsInUse = inUse.Contains });
        var source = Source();
        library.Initialize();
        library.AddCollection("Warnings", [source]);
        await SettleAsync(library, sync);

        inUse.Add(Path.Combine(ServiceFolder, "NW-0002-26"));
        server.Summaries = SecomTests.Summaries(1);
        await RefreshAsync(library, sync);
        Assert.Equal(2, Directory.EnumerateDirectories(ServiceFolder).Count());

        inUse.Clear();
        await RefreshAsync(library, sync);
        Assert.Equal(["NW-0001-26"], Directory.EnumerateDirectories(ServiceFolder).Select(Path.GetFileName));
    }

    [Fact]
    public async Task A_sync_over_the_size_cap_downloads_nothing()
    {
        var (library, sync, _) = Create(objects: 3, new LibrarySyncOptions { MaxBytes = 10_000 });
        var source = Source();
        library.Initialize();
        library.AddCollection("Warnings", [source]);

        await SettleAsync(library, sync);

        var status = sync.StatusOf(source.Id)!;
        Assert.Equal(0, status.Downloaded);
        Assert.Equal(5001 + 5002 + 5003, status.NeededBytes);
        Assert.False(Directory.Exists(ServiceFolder) && Directory.EnumerateDirectories(ServiceFolder).Any());
    }

    [Fact]
    public async Task Nothing_is_pruned_from_a_stale_listing()
    {
        var (library, sync, server) = Create(objects: 2);
        var source = Source();
        library.Initialize();
        library.AddCollection("Warnings", [source]);
        await SettleAsync(library, sync);

        server.Down = true;
        await RefreshAsync(library, sync);

        Assert.True(sync.StatusOf(source.Id)!.PruneSkipped);
        Assert.Equal(2, Directory.EnumerateDirectories(ServiceFolder).Count());
    }

    [Fact]
    public async Task Sources_without_sync_are_left_alone()
    {
        var (library, sync, _) = Create();
        var source = Source(sync: false);
        library.Initialize();
        library.AddCollection("Warnings", [source]);

        await SettleAsync(library, sync);

        Assert.Null(sync.StatusOf(source.Id));
        Assert.False(Directory.Exists(ServiceFolder));
    }

    // ── Other online kinds (#809): kept downloaded and current, never pruned ──

    private static string NoaaCatalogue(int update) => $$"""
        <?xml version="1.0" encoding="UTF-8" ?>
        <EncProductCatalog>
          <cell>
            <name>US4OH1MK</name>
            <lname>Ohio River</lname>
            <cscale>40000</cscale>
            <status>Active</status>
            <states><state>OH</state></states>
            <zipfile_location>https://charts.test/ENCs/US4OH1MK.zip</zipfile_location>
            <zipfile_datetime_iso8601>2026-09-25T04:46:56Z</zipfile_datetime_iso8601>
            <zipfile_size>10279</zipfile_size>
            <edtn>1</edtn>
            <updn>{{update}}</updn>
          </cell>
        </EncProductCatalog>
        """;

    /// <summary>Serves a NOAA catalogue (replaceable) and the US4OH1MK cell zip, counting downloads.</summary>
    private sealed class NoaaServer : HttpMessageHandler
    {
        private readonly byte[] _zip = File.ReadAllBytes(TestPaths.Fixture("US4OH1MK.zip"));

        public string Catalogue { get; set; } = NoaaCatalogue(1);

        public int Downloads { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith(".zip", StringComparison.Ordinal))
            {
                Downloads++;
                return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new ByteArrayContent(_zip) });
            }

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(Catalogue) });
        }
    }

    [Fact]
    public async Task A_synced_noaa_source_keeps_its_cells_current_and_prunes_nothing()
    {
        var server = new NoaaServer();
        var http = new HttpClient(server);
        var feeds = new NoaaEncFeedIndexer(http, Path.Combine(_context.Root, "cache"),
            new FeedCacheOptions { RevalidationInterval = TimeSpan.Zero });
        var library = _context.CreateLibrary(CollectionIndexer.CreateDefault(feeds: [feeds]));
        var downloads = new LibraryDownloads(LibraryDownloads.ManagedFolders(http, Downloads));
        var sync = new LibrarySync(library, downloads);
        _disposables.Add(library);
        _disposables.Add(sync);
        library.Initialize();

        // A copy downloaded by hand from elsewhere shares the NOAA folder.
        var manual = Path.Combine(Downloads, "noaa-enc", "US5XX01M");
        Directory.CreateDirectory(manual);
        File.WriteAllText(Path.Combine(manual, ".source.json"), "{}");

        var source = new NoaaEncFeedSource(Guid.NewGuid(), null, new Uri("https://charts.test/ENCs/ENCProdCat.xml"), NoaaEncFilter.All)
        {
            Sync = true,
        };
        library.AddCollection("Ohio", [source]);
        await SettleAsync(library, sync);

        Assert.Equal(1, server.Downloads);
        Assert.Equal((1, 1, 0), (sync.StatusOf(source.Id)!.Local, sync.StatusOf(source.Id)!.Listed, sync.StatusOf(source.Id)!.Failed));
        Assert.True(File.Exists(Path.Combine(Downloads, "noaa-enc", "US4OH1MK", ".source.json")));

        // A new update is downloaded on the next refresh; the hand-made copy is kept.
        server.Catalogue = NoaaCatalogue(2);
        library.Refresh();
        await SettleAsync(library, sync);
        Assert.Equal(2, server.Downloads);
        Assert.Equal(1, sync.StatusOf(source.Id)!.Local);
        Assert.True(Directory.Exists(manual));

        // Up to date: nothing more.
        library.Refresh();
        await SettleAsync(library, sync);
        Assert.Equal(2, server.Downloads);
    }

    [Fact]
    public void Online_kinds_can_sync_and_local_ones_cannot()
    {
        var (library, sync, _) = Create();
        Assert.True(sync.CanSync(new NoaaEncFeedSource(Guid.NewGuid(), null, NoaaEncFeedSource.DefaultCatalogUri, NoaaEncFilter.All)));
        Assert.True(sync.CanSync(Source(sync: false)));
        Assert.False(sync.CanSync(new LocalFolderSource(Guid.NewGuid(), null, "/charts")));
        Assert.False(sync.IsSynced(new LocalFolderSource(Guid.NewGuid(), null, "/charts") { Sync = true }));
        Assert.NotNull(library);
    }
}
