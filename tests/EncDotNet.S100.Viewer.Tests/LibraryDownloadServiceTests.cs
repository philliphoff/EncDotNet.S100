using System.Net;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class LibraryDownloadServiceTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly ZipServer _server = new(File.ReadAllBytes(
        LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "US4OH1MK.zip")));

    public void Dispose() => _context.Dispose();

    private LibraryDownloadService Create() =>
        new(new EncCellDownloader(new HttpClient(_server), Path.Combine(_context.Root, "noaa-enc")));

    internal static CollectionItem Cell(int update = 1, string name = "US4OH1MK", CollectionItemStatus status = CollectionItemStatus.Active) => new()
    {
        Key = name,
        ProductSpec = "S-57",
        Name = name,
        Edition = 1,
        Update = update,
        Status = status,
        Location = new RemoteItemLocation(new Uri($"https://example.test/ENCs/{name}.zip"), 10_279),
    };

    [Fact]
    public async Task Downloaded_cells_localize_to_their_exchange_set()
    {
        var service = Create();
        var changes = 0;
        service.Changed += (_, _) => changes++;
        Assert.Same(Cell().Location.GetType(), service.Localize(Cell()).Location.GetType());

        var result = await service.DownloadAsync([Cell()]);

        Assert.Equal(new LibraryDownloadResult(1, 0, false), result);
        Assert.True(changes > 0);
        var local = Assert.IsType<LocalItemLocation>(service.Localize(Cell()).Location);
        Assert.Equal("US4OH1MK/US4OH1MK.000", local.RelativePath);
        Assert.Equal(LibraryAvailability.Local, LibraryAvailabilityResolver.Resolve(service.Localize(Cell())));
        Assert.False(service.IsOutdated(Cell()));
        Assert.True(service.IsOutdated(Cell(update: 2)));
    }

    [Fact]
    public async Task Failures_are_counted_and_cancelled_cells_skipped()
    {
        var service = Create();
        _server.Fail = true;

        var result = await service.DownloadAsync([Cell(), Cell(name: "US5GONE1", status: CollectionItemStatus.Cancelled)]);

        Assert.Equal(new LibraryDownloadResult(0, 1, false), result);
        Assert.False(service.CanDownload(Cell(status: CollectionItemStatus.Cancelled)));
        Assert.IsType<RemoteItemLocation>(service.Localize(Cell()).Location);
    }

    [Fact]
    public async Task Cancellation_stops_the_batch()
    {
        var service = Create();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await service.DownloadAsync([Cell()], cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(0, result.Downloaded);
    }

    [Fact]
    public async Task Downloads_are_routed_to_a_folder_per_provider()
    {
        var noaa = new EncCellDownloader(new HttpClient(_server), Path.Combine(_context.Root, "noaa-enc"));
        var usace = new EncCellDownloader(new HttpClient(_server), Path.Combine(_context.Root, "usace-ienc"));
        var service = new LibraryDownloadService(remote => remote.Uri.Host switch
        {
            "ienccloud.us" => usace,
            "example.test" => noaa,
            _ => null,
        });
        var usaceItem = Cell() with { Location = new RemoteItemLocation(new Uri("https://ienccloud.us/x/US4OH1MK.zip")) };
        var elsewhere = Cell() with { Location = new RemoteItemLocation(new Uri("https://unknown.test/US4OH1MK.zip")) };

        await service.DownloadAsync([usaceItem]);

        Assert.True(Directory.Exists(Path.Combine(_context.Root, "usace-ienc", "US4OH1MK")));
        Assert.False(Directory.Exists(Path.Combine(_context.Root, "noaa-enc")));
        Assert.IsType<LocalItemLocation>(service.Localize(usaceItem).Location);
        Assert.IsType<RemoteItemLocation>(service.Localize(Cell()).Location);  // same cell, other provider
        Assert.False(service.CanDownload(elsewhere));
    }

    [Fact]
    public async Task A_package_downloads_once_into_its_folder_and_localizes_its_cells()
    {
        var published = new DateTimeOffset(2024, 6, 12, 0, 0, 0, TimeSpan.Zero);
        var folders = new List<string>();
        var service = new LibraryDownloadService(remote =>
        {
            folders.Add(remote.DownloadFolder ?? "(host)");
            return new EncCellDownloader(new HttpClient(_server), Path.Combine(_context.Root, remote.DownloadFolder ?? "noaa-enc"));
        });
        CollectionItem Member(string name, DateTimeOffset? at = null) => new()
        {
            Key = "Base1/" + name,
            ProductSpec = "S-57",
            Name = name,
            Location = new RemoteItemLocation(new Uri("https://example.test/p.zip"), null, at ?? published, "community/TEST", "Base1"),
        };

        // Two cells of the same package: one download.
        var result = await service.DownloadAsync([Member("US4OH1MK"), Member("OTHER")]);

        Assert.Equal(new LibraryDownloadResult(1, 0, false), result);
        Assert.True(File.Exists(Path.Combine(_context.Root, "community", "TEST", "Base1", EncCellDownloader.RecordFileName)));
        Assert.IsType<LocalItemLocation>(service.Localize(Member("US4OH1MK")).Location);
        // A name the package does not hold (e.g. the entry before re-indexing) stays online.
        Assert.IsType<RemoteItemLocation>(service.Localize(Member("OTHER")).Location);
        Assert.False(service.IsOutdated(Member("US4OH1MK")));
        Assert.True(service.IsOutdated(Member("US4OH1MK", published.AddDays(7))));
        Assert.DoesNotContain("(host)", folders);
    }

    private sealed class ZipServer(byte[] zip) : HttpMessageHandler
    {
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Fail
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
    }
}
