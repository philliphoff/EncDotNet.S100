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

    [Fact]
    public async Task Items_report_queued_running_and_done_while_the_batch_reports_progress()
    {
        var server = new GatedServer(File.ReadAllBytes(
            LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "US4OH1MK.zip")));
        var service = new LibraryDownloadService(new EncCellDownloader(new HttpClient(server), Path.Combine(_context.Root, "noaa-enc")));
        var items = Enumerable.Range(0, LibraryDownloadService.MaxConcurrentDownloads + 1)
            .Select(i => Cell() with { Key = $"k{i}", Location = new RemoteItemLocation(new Uri($"https://example.test/{i}/US4OH1MK.zip"), 10_279) })
            .ToArray();
        // Distinct download names, so none are de-duplicated.
        items = items.Select((c, i) => c with { Name = $"US4OH1M{i}" }).ToArray();
        Assert.Null(service.Progress);

        var download = service.DownloadAsync(items);
        await server.WaitForRequestsAsync(LibraryDownloadService.MaxConcurrentDownloads);

        Assert.Equal(items.Length, service.Progress!.Total);
        Assert.Equal(LibraryDownloadItemState.Running, service.StatusOf(items[0])!.State);
        Assert.Equal(LibraryDownloadItemState.Queued, service.StatusOf(items[^1])!.State);

        server.Release();
        var result = await download;

        // The zip only holds US4OH1MK.000, so these names fail — and say so.
        Assert.Equal(items.Length, result.Failed);
        Assert.Null(service.Progress);
        var failed = service.StatusOf(items[0])!;
        Assert.Equal(LibraryDownloadItemState.Failed, failed.State);
        Assert.Contains("US4OH1M0", failed.Error);
    }

    [Fact]
    public async Task One_item_can_be_cancelled_while_the_batch_carries_on()
    {
        var server = new GatedServer(File.ReadAllBytes(
            LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "US4OH1MK.zip")));
        var service = new LibraryDownloadService(remote => new EncCellDownloader(
            new HttpClient(server), Path.Combine(_context.Root, remote.Uri.Segments[1].TrimEnd('/'))));
        var first = Cell() with { Location = new RemoteItemLocation(new Uri("https://example.test/a/US4OH1MK.zip"), 10_279) };
        var second = Cell() with { Key = "b", Location = new RemoteItemLocation(new Uri("https://example.test/b/US4OH1MK.zip"), 10_279) };

        var download = service.DownloadAsync([first, second]);
        await server.WaitForRequestsAsync(2);
        service.Cancel(first);
        server.Release();
        var result = await download;

        Assert.Equal(new LibraryDownloadResult(1, 1, false), result);
        Assert.Null(service.StatusOf(first));
        Assert.IsType<LocalItemLocation>(service.Localize(second).Location);
    }

    [Fact]
    public async Task Cancelling_all_stops_the_batch_and_clears_its_items()
    {
        var server = new GatedServer(File.ReadAllBytes(
            LibraryTestContext.RepoFile("tests", "EncDotNet.S100.Collections.Tests", "Fixtures", "US4OH1MK.zip")));
        var service = new LibraryDownloadService(new EncCellDownloader(new HttpClient(server), Path.Combine(_context.Root, "noaa-enc")));

        var download = service.DownloadAsync([Cell()]);
        await server.WaitForRequestsAsync(1);
        service.CancelAll();
        var result = await download;

        Assert.True(result.Cancelled);
        Assert.Null(service.StatusOf(Cell()));
        Assert.Null(service.Progress);
    }

    /// <summary>Holds every request until <see cref="Release"/>, so a test can observe a running batch.</summary>
    private sealed class GatedServer(byte[] zip) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requests;

        public void Release() => _gate.TrySetResult();

        public async Task WaitForRequestsAsync(int count)
        {
            for (var i = 0; i < 500 && Volatile.Read(ref _requests) < count; i++)
                await Task.Delay(10);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            await _gate.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        }
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
