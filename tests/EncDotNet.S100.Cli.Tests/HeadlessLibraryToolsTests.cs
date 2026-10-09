using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Mcp.Tools.Library;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Issue #792 chunk 3b: the shared Library tools over <see cref="HeadlessLibrary"/>,
/// with the CLI's headless dataset catalog — the same tools and result shapes
/// the viewer registers, with no view models.
/// </summary>
public sealed class HeadlessLibraryToolsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "headless-tools-" + Guid.NewGuid().ToString("N"));
    private readonly HeadlessMutableCatalog _catalog = new();
    private readonly CollectionLibrary _library;
    private readonly LibraryOperations _operations;
    private readonly HeadlessLibrary _host;

    public HeadlessLibraryToolsTests()
    {
        Directory.CreateDirectory(_root);
        _library = new CollectionLibrary(
            CollectionIndexer.CreateDefault(),
            new CollectionLibraryOptions(Path.Combine(_root, "collections.json"), Path.Combine(_root, "index-cache")));
        _library.Initialize();
        var operations = _operations = new LibraryOperations(
            _library,
            new LibraryDownloads(LibraryDownloads.ManagedFolders(new HttpClient(), Path.Combine(_root, "downloads"))),
            new LibraryLoader(new CatalogLibraryOpener(_catalog)));
        _host = new HeadlessLibrary(operations);
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

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string TestData(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    /// <summary>A collection "Charts" with one folder source holding one S-57 cell.</summary>
    private async Task<LibrarySource> AddChartFolderAsync()
    {
        var folder = Path.Combine(_root, "harbour");
        Directory.CreateDirectory(folder);
        File.Copy(TestData("US5MA1BO.000"), Path.Combine(folder, "US5MA1BO.000"));
        var definition = new LocalFolderSource(Guid.NewGuid(), null, folder);
        _library.AddCollection("Charts", [definition]);
        await _library.WhenIdle().WaitAsync(TimeSpan.FromSeconds(30), Ct);
        return _library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == definition.Id);
    }

    private static T Value<T>(Datasets.Pipelines.Query.ToolResult<T> result)
    {
        Assert.True(result.TryGetValue(out var value), result.TryGetError(out var error) ? error!.Message : "no value");
        return value!;
    }

    [Fact]
    public async Task Sources_are_listed_with_their_names_kinds_and_counts()
    {
        var source = await AddChartFolderAsync();

        var listed = Value(await new ListLibrarySourcesTool(_host).InvokeAsync(counts: true, Ct));

        var collection = Assert.Single(listed.Collections);
        Assert.Equal("Charts", collection.Name);
        Assert.Equal("DIR", collection.Kind);
        Assert.Equal(1, collection.ItemCount);
        var info = Assert.Single(collection.Sources);
        Assert.Equal(source.Id, info.Id);
        Assert.Equal("harbour", info.Name);
        Assert.Equal("ready", info.IndexState);
        Assert.Null(info.Url);
        Assert.Equal(1, info.Counts!["local"]);
    }

    [Fact]
    public async Task Items_are_queried_described_and_loaded_in_the_viewers_words()
    {
        var source = await AddChartFolderAsync();

        var page = Value(await new QueryLibraryItemsTool(_host).InvokeAsync(
            new QueryLibraryItemsRequest(source.Id.ToString(), null, "S-57", null, null, null, null, null, null, null, null, null), Ct));
        var item = Assert.Single(page.Items);
        Assert.Equal("US5MA1BO", item.Name);
        Assert.Equal("local", item.State);
        Assert.Empty(item.Tags);
        Assert.NotNull(item.LocalPath);

        var detail = Value(await new DescribeLibraryItemTool(_host).InvokeAsync(item.Id, Ct));
        var expected = LibraryItemText.Details(new LibraryItemTextInput(
            source.Index!.Items[0], source, LibraryAvailability.Local, source.Index.Items[0])
        { CollectionName = "Charts" });
        Assert.Equal(expected.Select(g => g.Title), detail.Details.Select(g => g.Title));
        Assert.Equal(
            expected.SelectMany(g => g.Fields).Select(f => (f.Label, f.Value)),
            detail.Details.SelectMany(g => g.Fields).Select(f => (f.Label, f.Value)));

        var loaded = Value(await new LibraryActionTool(_host).InvokeAsync(new LibraryActionToolRequest(
            "load", [item.Id], new QueryLibraryItemsRequest(null, null, null, null, null, null, null, null, null, null, null, null), null, null, null), Ct));
        Assert.Equal(1, loaded.Opened);
        Assert.Single(_catalog.Datasets);

        var after = Assert.Single(Value(await new QueryLibraryItemsTool(_host).InvokeAsync(
            new QueryLibraryItemsRequest(null, ["loaded"], null, null, null, null, null, null, null, null, null, null), Ct)).Items);
        Assert.Equal("loaded", after.State);
        Assert.Equal([LibraryText.Find("Library_Availability_Loaded")!], after.Tags);
        Assert.True(Value(await new AwaitLibraryIdleTool(_host).InvokeAsync(0, Ct)).Idle);
    }

    [Fact]
    public async Task A_download_of_local_items_is_a_dry_run_that_skips_them()
    {
        var source = await AddChartFolderAsync();

        var result = Value(await new LibraryActionTool(_host).InvokeAsync(new LibraryActionToolRequest(
            "download", null, new QueryLibraryItemsRequest(source.Id.ToString(), null, null, null, null, null, null, null, null, null, null, null),
            null, DryRun: true, null), Ct));

        Assert.True(result.DryRun);
        Assert.Equal(0, result.Eligible);
        Assert.Equal(1, result.Skipped["local"]);
    }

    [Fact]
    public async Task Options_are_set_and_sources_removed()
    {
        var source = await AddChartFolderAsync();

        var options = Value(await new SetLibrarySourceOptionsTool(_host).InvokeAsync(source.Id.ToString(), sync: null, showOnMap: true, Ct));
        var set = Assert.Single(options.Sources);
        Assert.True(set.ShowOnMap);
        Assert.False(set.CanSync);
        Assert.True(set.Changed);
        Assert.False((await new SetLibrarySourceOptionsTool(_host).InvokeAsync(source.Id.ToString(), sync: true, showOnMap: null, Ct)).TryGetValue(out _));

        var collectionId = _library.Collections.Single().Id;
        var removed = Value(await new RemoveLibrarySourceTool(_host).InvokeAsync(collectionId.ToString(), confirm: true, Ct));
        Assert.True(removed.WasCollection);
        Assert.DoesNotContain(_library.Collections, c => !c.IsSession);
    }

    [Fact]
    public async Task A_folder_is_previewed_then_added_by_path()
    {
        var folder = Path.Combine(_root, "harbour");
        Directory.CreateDirectory(folder);
        File.Copy(TestData("US5MA1BO.000"), Path.Combine(folder, "US5MA1BO.000"));
        var tool = new AddLibrarySourceTool(_host);

        var preview = Value(await tool.InvokeAsync(
            new AddSourceRequest(null, folder, null, null, null, null, null, null, null, null, Preview: true), Ct));
        Assert.False(preview.Added);
        Assert.Equal("Folder", preview.Kind);
        Assert.Null(preview.CatalogueDetail);
        Assert.DoesNotContain(_library.Collections, c => !c.IsSession);

        var added = Value(await tool.InvokeAsync(
            new AddSourceRequest(null, folder, null, null, null, null, null, "Harbour charts", null, null, Preview: false), Ct));
        Assert.True(added.Added);
        Assert.NotNull(added.SourceId);
        await _library.WhenIdle().WaitAsync(TimeSpan.FromSeconds(30), Ct);

        var collection = Assert.Single(_library.Collections, c => !c.IsSession);
        Assert.Equal("Harbour charts", collection.Definition.Name);
        Assert.Equal(added.CollectionId, collection.Id);
        Assert.Equal(1, collection.ItemCount);
    }

    [Fact]
    public async Task Catalogues_without_a_reader_are_refused()
    {
        var tool = new AddLibrarySourceTool(_host);

        // This host was given no catalogue readers.
        Assert.True((await tool.InvokeAsync(
            new AddSourceRequest("noaa-s111", null, null, null, null, null, null, null, null, null, Preview: true), Ct)).TryGetError(out var forecast));
        Assert.Equal("library_change_rejected", forecast!.Code);
        Assert.True((await tool.InvokeAsync(
            new AddSourceRequest("noaa-enc", null, null, null, null, null, null, null, null, null, Preview: true), Ct)).TryGetError(out var noaa));
        Assert.Equal("library_change_rejected", noaa!.Code);
    }

    [Fact]
    public async Task A_forecast_feed_and_a_secom_service_are_added_with_their_shape_and_sync()
    {
        var run = DateTimeOffset.UtcNow.AddHours(-1);
        var readers = new LibraryCatalogueReaders
        {
            ForecastModels = (_, models, _) => Task.FromResult<IReadOnlyList<ForecastModelSummary>>(
                [.. models.Select(m => new ForecastModelSummary(m, run, 4, 4_000, 9_000, null))]),
            Secom = (_, _, _) => Task.FromResult(new SecomServiceDescription(
                [new CatalogFacetValue("S-124", 5, 5_120)], 5, 5, Truncated: false, Collections.Secom.SecomApiVersion.V2)),
        };
        var host = new HeadlessLibrary(_operations, readers: readers);
        var tool = new AddLibrarySourceTool(host);

        var forecast = Value(await tool.InvokeAsync(
            new AddSourceRequest("noaa-s111", null, null, null, ["cbofs"], null, null, null, Shape: "regional", null, Preview: false), Ct));
        Assert.True(forecast.Added);
        Assert.Equal("regional", Assert.Single(forecast.Shapes, o => o.Selected).Value);
        Assert.Null(forecast.Note);
        var feed = Assert.IsType<S100ForecastFeedSource>(_library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == forecast.SourceId).Definition);
        Assert.Equal(ForecastShape.Regional, feed.Shape);

        // No map view here; a small service syncs by default.
        Assert.True((await tool.InvokeAsync(
            new AddSourceRequest("ccg-s124-secom", null, null, null, null, null, null, null, null, null, Preview: true, InMapView: true), Ct))
            .TryGetError(out var noView));
        Assert.Equal("library_change_rejected", noView!.Code);
        var secom = Value(await tool.InvokeAsync(
            new AddSourceRequest("ccg-s124-secom", null, null, null, null, null, null, null, null, null, Preview: false), Ct));
        Assert.True(secom.Sync);
        Assert.StartsWith("Downloads ", secom.SyncNote, StringComparison.Ordinal);
        var service = Assert.IsType<SecomSource>(_library.Collections.SelectMany(c => c.Sources).Single(s => s.Id == secom.SourceId).Definition);
        Assert.True(service.Sync);
    }

    [Fact]
    public async Task Known_sources_list_the_curated_directory()
    {
        var known = Value(await new ListKnownSourcesTool(_host).InvokeAsync(Ct));

        Assert.Contains(known.Sources, s => s.Id == "noaa-enc" && !s.UserAdded);
        Assert.Equal(KnownCatalogueSources.All.Count, known.Sources.Count);
    }
}
