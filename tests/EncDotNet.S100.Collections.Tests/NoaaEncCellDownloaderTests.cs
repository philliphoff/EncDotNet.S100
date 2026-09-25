using System.Net;
using EncDotNet.S100.Collections.Noaa;

namespace EncDotNet.S100.Collections.Tests;

public sealed class NoaaEncCellDownloaderTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly ZipServer _server = new(File.ReadAllBytes(TestPaths.Fixture("US4OH1MK.zip")));

    public void Dispose() => _root.Dispose();

    private NoaaEncCellDownloader Create() => new(new HttpClient(_server), _root.Path);

    private static CollectionItem Cell(int edition = 1, int update = 1) => new()
    {
        Key = "US4OH1MK",
        ProductSpec = "S-57",
        Name = "US4OH1MK",
        Edition = edition,
        Update = update,
        Location = new RemoteItemLocation(new Uri("https://example.test/ENCs/US4OH1MK.zip"), 10_279),
    };

    [Fact]
    public async Task Downloads_and_extracts_a_loadable_exchange_set()
    {
        var downloader = Create();
        long lastProgress = 0;

        var cell = await downloader.DownloadAsync(Cell(), new Progress<long>(b => lastProgress = b));

        Assert.Equal("US4OH1MK", cell.Name);
        Assert.Equal(1, cell.Edition);
        var location = cell.Location;
        Assert.Equal(Path.Combine(_root.Path, "US4OH1MK", "ENC_ROOT"), location.RootPath);
        Assert.Equal("US4OH1MK/US4OH1MK.000", location.RelativePath);
        Assert.Equal(["US4OH1MK/US4OH1MK.001"], location.UpdateRelativePaths);
        Assert.Equal("CATALOG.031", location.CatalogueRelativePath);
        Assert.True(File.Exists(Path.Combine(location.RootPath, location.RelativePath)));

        // Nothing is left over from staging.
        Assert.Equal(["US4OH1MK"], Directory.EnumerateFileSystemEntries(_root.Path).Select(Path.GetFileName));
        var readBack = downloader.TryGetDownloaded("US4OH1MK")!;
        Assert.Equal(cell.Location.RootPath, readBack.Location.RootPath);
        Assert.Equal(cell.Location.UpdateRelativePaths, readBack.Location.UpdateRelativePaths);
        Assert.Equal(cell.DownloadedAt, readBack.DownloadedAt);
    }

    [Fact]
    public async Task Downloaded_cell_indexes_as_a_local_exchange_set()
    {
        var cell = await Create().DownloadAsync(Cell());

        var index = await Indexing.CollectionIndexer.CreateDefault()
            .IndexAsync(new ExchangeSetSource(Guid.NewGuid(), null, cell.Location.RootPath));

        var item = Assert.Single(index.Items);
        Assert.Equal("US4OH1MK", item.Name);
        Assert.Equal(1, item.Update);
    }

    [Fact]
    public async Task A_failed_redownload_keeps_the_previous_copy()
    {
        var downloader = Create();
        var first = await downloader.DownloadAsync(Cell());

        _server.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => downloader.DownloadAsync(Cell(update: 2)));

        var kept = downloader.TryGetDownloaded("US4OH1MK")!;
        Assert.Equal(first.Update, kept.Update);
        Assert.Equal(first.DownloadedAt, kept.DownloadedAt);
        Assert.Single(Directory.EnumerateFileSystemEntries(_root.Path));
    }

    [Fact]
    public async Task A_redownload_replaces_the_copy_and_records_the_new_update()
    {
        var downloader = Create();
        await downloader.DownloadAsync(Cell(update: 1));

        var second = await downloader.DownloadAsync(Cell(update: 2));

        Assert.Equal(2, second.Update);
        Assert.Single(Directory.EnumerateFileSystemEntries(_root.Path));
    }

    [Fact]
    public async Task Cancellation_leaves_nothing_behind()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Create().DownloadAsync(Cell(), cancellationToken: cts.Token));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_root.Path));
    }

    [Fact]
    public async Task IsOlderThan_compares_edition_then_update()
    {
        var cell = await Create().DownloadAsync(Cell(edition: 3, update: 2));

        Assert.False(cell.IsOlderThan(Cell(edition: 3, update: 2)));
        Assert.True(cell.IsOlderThan(Cell(edition: 3, update: 3)));
        Assert.True(cell.IsOlderThan(Cell(edition: 4, update: 0)));
        Assert.False(cell.IsOlderThan(Cell(edition: 2, update: 9)));
    }

    [Fact]
    public void Unknown_cells_are_not_downloaded()
    {
        Assert.Null(Create().TryGetDownloaded("US5XX00M"));
    }

    private sealed class ZipServer(byte[] zip) : HttpMessageHandler
    {
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Fail
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
        }
    }
}
