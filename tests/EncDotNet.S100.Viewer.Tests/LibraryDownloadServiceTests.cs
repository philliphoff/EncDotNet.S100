using System.Net;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Noaa;
using EncDotNet.S100.Viewer.Library;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class LibraryDownloadServiceTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly ZipServer _server = new(File.ReadAllBytes(
        LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "US4OH1MK.zip")));

    public void Dispose() => _context.Dispose();

    private LibraryDownloadService Create() =>
        new(new NoaaEncCellDownloader(new HttpClient(_server), Path.Combine(_context.Root, "noaa-enc")));

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

    private sealed class ZipServer(byte[] zip) : HttpMessageHandler
    {
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Fail
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
    }
}
