using System.Net;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Headless load and download (issue #792 chunk 2): items group by exchange
/// set, downloads report plain progress, and every operation keeps the
/// Library busy until the datasets it opens are open (#790).
/// </summary>
public sealed class LibraryOperationsTests : IDisposable
{
    private readonly LibraryContext _context = new();
    private readonly GatedServer _server = new(File.ReadAllBytes(TestPaths.Fixture("US4OH1MK.zip")));

    public void Dispose() => _context.Dispose();

    private static CollectionItem Cell(string name = "US4OH1MK") => new()
    {
        Key = name,
        ProductSpec = "S-57",
        Name = name,
        Edition = 1,
        Update = 1,
        Location = new RemoteItemLocation(new Uri($"https://example.test/ENCs/{name}.zip"), 10_279),
    };

    private static LibrarySource SourceOf(CollectionSource definition) =>
        new(definition, null, LibrarySourceState.Ready);

    private (CollectionLibrary Library, LibraryOperations Operations, FakeOpener Opener) Create()
    {
        var library = _context.CreateLibrary();
        library.Initialize();
        var opener = new FakeOpener();
        var downloads = new LibraryDownloads(new EncCellDownloader(new HttpClient(_server), Path.Combine(_context.Root, "downloads")));
        return (library, new LibraryOperations(library, downloads, new LibraryLoader(opener)), opener);
    }

    private async Task<IReadOnlyList<CollectionItem>> IndexS57SetAsync()
    {
        var root = _context.CreateS57ExchangeSet();
        var index = await CollectionIndexer.CreateDefault()
            .IndexAsync(new ExchangeSetSource(Guid.NewGuid(), null, root), cancellationToken: TestContext.Current.CancellationToken);
        return index.Items;
    }

    [Fact]
    public async Task Plan_labels_each_group_with_its_library_source()
    {
        var items = await IndexS57SetAsync();
        var label = new LibrarySourceLabel(Guid.NewGuid(), "Charts");

        var (groups, _) = LibraryLoader.Plan(items, _ => label);
        var (unlabelled, _) = LibraryLoader.Plan(items);

        Assert.Same(label, Assert.Single(groups).Source);
        Assert.Null(Assert.Single(unlabelled).Source);
    }

    [Fact]
    public async Task Plan_groups_local_items_by_exchange_set_and_skips_the_rest()
    {
        var items = await IndexS57SetAsync();

        var (groups, skipped) = LibraryLoader.Plan(
        [
            .. items,
            Cell(),
            items[0] with { Key = "x", Location = NoItemLocation.Instance },
            items[0] with { Key = "y", Location = ((LocalItemLocation)items[0].Location) with { RelativePath = "GONE/GONE.000" } },
            items[0] with { Key = "z", ProductSpec = "Unknown" },
        ]);

        var group = Assert.Single(groups);
        Assert.Equal(4, skipped);
        Assert.Equal("CATALOG.031", group.CatalogueRelativePath);
        Assert.Equal(items.Select(i => i.Key), group.Items.Select(i => i.Key));
    }

    [Fact]
    public async Task A_load_keeps_the_library_busy_until_its_datasets_are_open()
    {
        var items = await IndexS57SetAsync();
        var (library, operations, opener) = Create();
        using var _ = library;
        await library.WhenIdle();
        opener.Gate = new TaskCompletionSource();

        var load = operations.LoadAsync(items, cancellationToken: TestContext.Current.CancellationToken);

        var busy = operations.GetActivity();
        Assert.False(busy.IsIdle);
        Assert.Equal(1, busy.ActiveOperations);
        Assert.Equal(2, busy.PendingDatasets);
        Assert.True((await operations.AwaitIdleAsync(TimeSpan.Zero, TestContext.Current.CancellationToken)).TimedOut);

        opener.Gate.SetResult();
        var result = await load;

        Assert.Equal(new LibraryLoadResult(2, 0), result);
        Assert.True(operations.GetActivity().IsIdle);
        Assert.All(items, item => Assert.Equal(LibraryLoadState.Loaded, operations.Loader.StateOf(item)));
        Assert.True(operations.StateOf(items[0], SourceOf(new ExchangeSetSource(Guid.NewGuid(), null, "x"))).IsLoadedNow);
    }

    [Fact]
    public async Task An_unreadable_exchange_set_is_skipped_with_its_reason()
    {
        var items = await IndexS57SetAsync();
        var (library, operations, opener) = Create();
        using var _ = library;
        opener.Failure = new InvalidDataException("The catalogue is unreadable.");

        var result = await operations.LoadAsync(items, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(0, result.Opened);
        Assert.Equal(2, result.Skipped);
        Assert.Equal(["The catalogue is unreadable."], result.Problems);
        Assert.Equal(0, operations.Activity.Active);
    }

    [Fact]
    public async Task A_download_reports_progress_and_stays_busy_until_its_datasets_open()
    {
        var (library, operations, opener) = Create();
        using var _ = library;
        await library.WhenIdle();
        var reports = new List<LibraryDownloadProgress>();
        var progress = new SyncProgress(reports.Add);
        var source = SourceOf(new LocalFolderSource(Guid.NewGuid(), null, _context.Root));

        // Not awaited, as library_action starts a download: the operation is
        // already tracked when the call returns.
        var download = operations.DownloadAsync([(Cell(), source)], load: true, progress, TestContext.Current.CancellationToken);

        var busy = operations.GetActivity();
        Assert.False(busy.IsIdle);
        Assert.Equal(1, busy.PendingDatasets);
        await _server.WaitForRequestAsync();
        Assert.Equal(LibraryDownloadItemState.Running, operations.Downloads.StatusOf(Cell())!.State);
        Assert.Equal(1, operations.Downloads.Progress!.Total);

        _server.Release();
        var outcome = await download;

        Assert.Equal(new LibraryDownloadResult(1, 0, false), outcome.Download);
        Assert.Empty(outcome.ReindexedSources);
        Assert.Equal(new LibraryLoadResult(1, 0), outcome.Load);
        var group = Assert.Single(opener.Groups);
        Assert.StartsWith(Path.Combine(_context.Root, "downloads", "US4OH1MK"), group.RootPath, StringComparison.Ordinal);
        Assert.Equal(LibraryLoadState.Loaded, operations.Loader.StateOf(operations.Downloads.Localize(Cell())));

        Assert.Equal(new LibraryDownloadProgress(0, 0, 1, 0, 10_279), reports[0]);
        Assert.Equal(1, reports[^1].Completed);
        Assert.Equal(1.0, reports[^1].Fraction);
        Assert.Null(operations.Downloads.Progress);
        Assert.Null(operations.Downloads.StatusOf(Cell()));
        var idle = await operations.AwaitIdleAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.False(idle.TimedOut);
        Assert.True(idle.Activity.IsIdle);
    }

    [Fact]
    public async Task A_download_only_opens_nothing_and_a_failure_is_counted()
    {
        var (library, operations, opener) = Create();
        using var _ = library;
        var source = SourceOf(new LocalFolderSource(Guid.NewGuid(), null, _context.Root));
        _server.Release();

        var outcome = await operations.DownloadAsync([(Cell(), source)], load: false, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Download.Downloaded);
        Assert.Null(outcome.Load);
        Assert.Empty(opener.Groups);
        Assert.IsType<LocalItemLocation>(operations.Downloads.Localize(Cell()).Location);

        _server.Fail = true;
        var failed = await operations.DownloadAsync([(Cell("US5FAIL1"), source)], load: true, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new LibraryDownloadResult(0, 1, false), failed.Download);
        Assert.Equal(LibraryDownloadItemState.Failed, operations.Downloads.StatusOf(Cell("US5FAIL1"))!.State);
        Assert.True(operations.GetActivity().IsIdle);
    }

    [Fact]
    public async Task Cancelling_a_batch_ends_it_and_leaves_the_library_idle()
    {
        var (library, operations, _) = Create();
        using var __ = library;
        await library.WhenIdle();
        var source = SourceOf(new LocalFolderSource(Guid.NewGuid(), null, _context.Root));

        var download = operations.DownloadAsync([(Cell(), source)], load: true, cancellationToken: TestContext.Current.CancellationToken);
        await _server.WaitForRequestAsync();
        operations.Downloads.CancelAll();
        var outcome = await download;

        Assert.True(outcome.Download.Cancelled);
        Assert.Null(outcome.Load);
        Assert.True(operations.GetActivity().IsIdle);
    }

    [Fact]
    public void Managed_folders_route_by_item_folder_then_provider()
    {
        var select = LibraryDownloads.ManagedFolders(new HttpClient(_server), _context.Root);

        Assert.Equal(Path.Combine(_context.Root, "noaa-enc"), select(new RemoteItemLocation(new Uri("https://charts.noaa.gov/x.zip")))!.Root);
        Assert.Equal(Path.Combine(_context.Root, "usace-ienc"), select(new RemoteItemLocation(new Uri("https://ienccloud.us/x.zip")))!.Root);
        var folder = select(new RemoteItemLocation(new Uri("https://example.test/x.h5"), DownloadFolder: "noaa-s111"))!;
        Assert.Equal(Path.Combine(_context.Root, "noaa-s111"), folder.Root);
        Assert.Same(folder, select(new RemoteItemLocation(new Uri("https://example.test/y.h5"), DownloadFolder: "noaa-s111")));
    }

    [Fact]
    public async Task A_downloaded_secom_object_shows_its_signature_without_a_manual_refresh()
    {
        using var signer = SecomTests.Signer.Create();
        var data = System.Text.Encoding.UTF8.GetBytes("<S124:Dataset/>");
        var server = new SecomTests.FakeSecomServer(SecomTests.Summaries(2));
        server.Objects["ref-0001"] = (data, signer.Sign(data));
        var http = new HttpClient(server);
        var downloadsRoot = Path.Combine(_context.Root, "downloads");
        var library = _context.CreateLibrary(CollectionIndexer.CreateDefault(
            feeds: [new SecomSourceIndexer(http, downloadsRoot: downloadsRoot)]));
        using var _ = library;
        library.Initialize();
        var definition = new SecomSource(Guid.NewGuid(), null, new Uri("https://secom.test/api/secom"), SecomFilter.All);
        library.AddCollection("Warnings", [definition]);
        await library.WhenIdle().WaitAsync(TestContext.Current.CancellationToken);
        LibrarySource Source() => library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == definition.Id);
        var item = Source().Index!.Items.Single(i => i.Key == "ref-0001");
        Assert.False(item.Properties.ContainsKey("signature"));

        var operations = new LibraryOperations(
            library, new LibraryDownloads(LibraryDownloads.ManagedFolders(http, downloadsRoot)), new LibraryLoader(new FakeOpener()));
        var outcome = await operations.DownloadAsync([(item, Source())], load: false, cancellationToken: TestContext.Current.CancellationToken);
        await library.WhenIdle().WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, outcome.Download.Downloaded);
        Assert.Equal("valid · signer not trusted", Source().Index!.Items.Single(i => i.Key == "ref-0001").Properties["signature"]);
    }

    /// <summary>Opens every group (optionally after a gate) and remembers what it opened.</summary>
    private sealed class FakeOpener : ILibraryDatasetOpener
    {
        private readonly HashSet<string> _open = new(StringComparer.Ordinal);

        public TaskCompletionSource? Gate { get; set; }

        public Exception? Failure { get; set; }

        public List<LibraryOpenGroup> Groups { get; } = [];

        public event EventHandler? Changed;

        public LibraryLoadState StateOf(LocalItemLocation location) =>
            _open.Contains(location.RootPath + "|" + location.RelativePath) ? LibraryLoadState.Loaded : LibraryLoadState.None;

        public async Task<LibraryOpenOutcome> OpenAsync(LibraryOpenGroup group, bool defer, CancellationToken cancellationToken)
        {
            if (Gate is { } gate)
                await gate.Task.WaitAsync(cancellationToken);
            if (Failure is { } failure)
                throw failure;
            Groups.Add(group);
            foreach (var item in group.Items)
            {
                var location = LibraryOpenGroup.LocationOf(item);
                _open.Add(location.RootPath + "|" + location.RelativePath);
            }
            Changed?.Invoke(this, EventArgs.Empty);
            return new LibraryOpenOutcome(group.Items.Count, []);
        }
    }

    private sealed class SyncProgress(Action<LibraryDownloadProgress> report) : IProgress<LibraryDownloadProgress>
    {
        public void Report(LibraryDownloadProgress value)
        {
            lock (this)
                report(value);
        }
    }

    /// <summary>Serves the cell zip once released (or fails), so a test can look while a download is in flight.</summary>
    private sealed class GatedServer(byte[] zip) : HttpMessageHandler
    {
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _requested = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Fail { get; set; }

        public void Release() => _release.TrySetResult();

        public Task WaitForRequestAsync() => _requested.Task.WaitAsync(TimeSpan.FromSeconds(10));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _requested.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return Fail
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) };
        }
    }
}
