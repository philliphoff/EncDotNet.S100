using System.Net;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Mcp.Tools.Library;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #792 chunk 2: Library items open into the CLI's headless dataset
/// catalog with no view models — local folders, collection manifests, and
/// downloaded online cells.
/// </summary>
public sealed class HeadlessLibraryLoadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "headless-library-" + Guid.NewGuid().ToString("N"));
    private readonly HeadlessMutableCatalog _catalog = new();
    private readonly CollectionLibrary _library;
    private readonly LibraryOperations _operations;

    public HeadlessLibraryLoadTests()
    {
        Directory.CreateDirectory(_root);
        _library = new CollectionLibrary(
            CollectionIndexer.CreateDefault(),
            new CollectionLibraryOptions(Path.Combine(_root, "collections.json"), Path.Combine(_root, "index-cache")));
        _library.Initialize();
        var http = new HttpClient(new ZipServer(File.ReadAllBytes(TestData("US4OH1MK.zip"))));
        _operations = new LibraryOperations(
            _library,
            new LibraryDownloads(LibraryDownloads.ManagedFolders(http, Path.Combine(_root, "downloads"))),
            new LibraryLoader(new CatalogLibraryOpener(_catalog)));
    }

    public void Dispose()
    {
        _library.Dispose();
        _catalog.Dispose();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string TestData(params string[] parts) =>
        Path.Combine([AppContext.BaseDirectory, "TestData", .. parts]);

    private static void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
    }

    /// <summary>Adds <paramref name="source"/> in a new collection and returns its indexed items.</summary>
    private async Task<(LibrarySource Source, IReadOnlyList<CollectionItem> Items)> AddAsync(CollectionSource source)
    {
        _library.AddCollection("Test", [source]);
        await _library.WhenIdle().WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        var indexed = _library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == source.Id);
        Assert.Equal(LibrarySourceState.Ready, indexed.State);
        return (indexed, indexed.Index!.Items);
    }

    [Fact]
    public async Task A_local_folder_item_opens_into_the_catalog_and_reports_loaded()
    {
        var folder = Path.Combine(_root, "charts");
        Directory.CreateDirectory(folder);
        File.Copy(TestData("US5MA1BO.000"), Path.Combine(folder, "US5MA1BO.000"));
        var (source, items) = await AddAsync(new LocalFolderSource(Guid.NewGuid(), null, folder));
        var item = Assert.Single(items);
        Assert.False(_operations.StateOf(item, source).IsLoadedNow);

        var result = await _operations.LoadAsync(items, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(new LibraryLoadResult(1, 0), result);
        var dataset = Assert.Single(_catalog.Datasets);
        Assert.Equal("S-57", dataset.Spec.Name);
        Assert.Equal(LibraryLoadState.Loaded, _operations.Loader.StateOf(item));
        Assert.Equal(LibraryAvailability.Loaded, _operations.StateOf(item, source).Availability);

        // Opening it again does not load it twice; closing it shows it as not loaded.
        Assert.Equal(new LibraryLoadResult(1, 0), await _operations.LoadAsync(items, cancellationToken: TestContext.Current.CancellationToken));
        Assert.Single(_catalog.Datasets);
        _catalog.RemoveAll();
        Assert.Equal(LibraryLoadState.None, _operations.Loader.StateOf(item));
        Assert.True(_operations.GetActivity().IsIdle);
    }

    [Fact]
    public async Task Manifest_items_open_by_exchange_set_and_unsupported_ones_are_skipped()
    {
        CopyTree(TestData("ExchangeSet"), Path.Combine(_root, "AU", "set"));
        var manifest = Path.Combine(_root, "test.s100collection.json");
        File.WriteAllText(manifest, """
            { "format": "encdotnet-s100-collection", "version": 1, "title": "Test",
              "groups": [ { "id": "AU", "name": "Australia", "paths": ["AU"] } ] }
            """);
        var (_, items) = await AddAsync(new LocalManifestSource(Guid.NewGuid(), null, manifest, new LocalManifestFilter { Groups = ["AU"] }));
        var openable = LibraryLoader.Plan(items);
        var group = Assert.Single(openable.Groups);
        Assert.Equal(Path.Combine(_root, "AU", "set"), group.RootPath);

        var result = await _operations.LoadAsync(items, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.Opened > 0, string.Join("; ", result.Problems ?? []));
        Assert.Equal(items.Count, result.Opened + result.Skipped);
        Assert.Equal(result.Opened, _catalog.Datasets.Count);
        Assert.Equal(result.Opened, items.Count(i => _operations.Loader.StateOf(i) == LibraryLoadState.Loaded));
    }

    [Fact]
    public async Task A_downloaded_cell_opens_into_the_catalog_and_the_library_goes_idle()
    {
        var feed = new LocalFolderSource(Guid.NewGuid(), null, _root);
        var source = new LibrarySource(feed, null, LibrarySourceState.Ready);
        var cell = new CollectionItem
        {
            Key = "US4OH1MK",
            ProductSpec = "S-57",
            Name = "US4OH1MK",
            Edition = 1,
            Update = 1,
            Location = new RemoteItemLocation(new Uri("https://charts.noaa.gov/ENCs/US4OH1MK.zip"), 10_279),
        };

        // Started as library_action download does: not awaited, yet already tracked.
        var download = _operations.DownloadAsync([(cell, source)], load: true, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(_operations.GetActivity().IsIdle);
        var idle = await _operations.AwaitIdleAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        var outcome = await download;

        Assert.False(idle.TimedOut);
        Assert.Equal(0, idle.Activity.PendingDatasets);
        Assert.Equal(new LibraryDownloadResult(1, 0, false), outcome.Download);
        Assert.Equal(1, outcome.Load!.Opened);
        Assert.True(Directory.Exists(Path.Combine(_root, "downloads", "noaa-enc", "US4OH1MK")));
        Assert.Contains(_catalog.Datasets, d => d.Id.Value.StartsWith("US4OH1MK", StringComparison.Ordinal));
        Assert.Equal(LibraryAvailability.Loaded, _operations.StateOf(cell, source).Availability);
    }

    private sealed class ZipServer(byte[] zip) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(zip) });
    }
}
