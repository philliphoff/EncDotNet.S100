using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The Library write MCP tools (#715 slice 3).</summary>
public sealed class LibraryEditToolsTests : IDisposable
{
    private static readonly DateTimeOffset Run = new(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);

    private readonly LibraryTestContext _context = new();
    private readonly LibraryService _library;
    private readonly RecordingLoader _loader = new();
    private readonly RecordingDownloader _downloader = new();
    private readonly LibraryPanelViewModel _panel;
    private readonly ViewerLibraryEditor _editor;

    public LibraryEditToolsTests()
    {
        _library = _context.CreateService(CollectionIndexer.CreateDefault(feeds: [new RemoteIndexer()]));
        _library.Initialize();
        _panel = new LibraryPanelViewModel(_library, new NoImporter(), _loader, _downloader, action => action());
        var reader = new ViewerLibraryController(_panel, null, action => { action(); return Task.CompletedTask; });
        _editor = new ViewerLibraryEditor(_panel, _library, reader, Dialog, dispatch: work => work());
    }

    public void Dispose()
    {
        _panel.Dispose();
        _library.Dispose();
        _context.Dispose();
    }

    private AddToLibraryDialogViewModel Dialog() => new(
        _library,
        null,
        loadForecastModels: (_, models, _) => Task.FromResult<IReadOnlyList<ForecastModelSummary>>(
            [.. models.Select(m => new ForecastModelSummary(m, Run, 4, 2_000_000, 1_000_000, new GeoBounds(36.5, -77.5, 39.7, -75.5)))]));

    private static AddSourceRequest Add(
        string? known = null, string? path = null, string[]? choices = null, Guid? collectionId = null, bool preview = false) =>
        new(known, path, null, null, choices, null, collectionId, null, null, null, preview);

    private static LibraryActionToolRequest Act(
        string action, string[]? ids = null, string? sourceId = null, string[]? states = null,
        bool? dryRun = null, long? maxBytes = null, bool? all = null) =>
        new(action, ids, new QueryLibraryItemsRequest(sourceId, states, null, null, null, null, null, null, null, null, null, null), all, dryRun, maxBytes);

    private async Task<Guid> ChartsAsync()
    {
        var collection = _library.AddCollection("Charts", [new ExchangeSetSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet("Charts"))]);
        await _library.WhenIdle();
        return collection.Id;
    }

    private async Task<Guid> OnlineAsync()
    {
        var collection = _library.AddCollection("Online", [new ChartCatalogsFeedSource(
            Guid.NewGuid(), null, new Uri("https://example.test/TEST_Catalog.xml"), ChartCatalogsFilter.All)]);
        await _library.WhenIdle();
        return collection.Id;
    }

    // ── add_library_source ─────────────────────────────────────────────

    [Fact]
    public async Task A_preview_lists_the_choices_and_adds_nothing()
    {
        var result = await new AddLibrarySourceTool(_editor).InvokeAsync(Add(known: "noaa-s111", preview: true), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var preview));
        Assert.False(preview!.Added);
        Assert.Equal("S100Forecast", preview.Kind);
        Assert.Contains(Assert.Single(preview.Choices).Options, o => o.Value == "cbofs");
        Assert.Contains(preview.Shapes, s => s.Selected);
        Assert.Empty(_library.Collections);
    }

    [Fact]
    public async Task Choosing_models_adds_a_forecast_source_in_a_new_collection()
    {
        var result = await new AddLibrarySourceTool(_editor).InvokeAsync(Add(known: "noaa-s111", choices: ["cbofs", "Delaware Bay"]), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var added));
        Assert.True(added!.Added);
        var collection = Assert.Single(_library.Collections);
        Assert.Equal(added.CollectionId, collection.Id);
        var source = Assert.IsType<S100ForecastFeedSource>(Assert.Single(collection.Sources).Definition);
        Assert.Equal(added.SourceId, source.Id);
        Assert.Equal(["cbofs", "dbofs"], source.Models.Select(m => m.Id).Order());
    }

    [Fact]
    public async Task A_local_exchange_set_joins_an_existing_collection()
    {
        var charts = await ChartsAsync();
        var path = _context.CreateS57ExchangeSet("More");

        var result = await new AddLibrarySourceTool(_editor).InvokeAsync(Add(path: path, collectionId: charts), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetValue(out var added));
        Assert.Equal(charts, added!.CollectionId);
        Assert.Equal(2, Assert.Single(_library.Collections).Sources.Count);
    }

    [Theory]
    [InlineData("noaa-s111", null, "choices")]
    [InlineData("no-such-source", null, "knownSourceId")]
    [InlineData(null, null, "knownSourceId")]
    [InlineData(null, "/no/such/path", "path")]
    public async Task Bad_adds_are_rejected(string? known, string? path, string parameter)
    {
        var result = await new AddLibrarySourceTool(_editor).InvokeAsync(Add(known: known, path: path, choices: known == "noaa-s111" ? ["atlantis"] : null), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(parameter, Assert.IsType<InvalidArgument>(error).Parameter);
        Assert.Empty(_library.Collections);
    }

    // ── library_action ─────────────────────────────────────────────────

    [Fact]
    public async Task A_dry_run_counts_bytes_and_skips_what_the_action_does_not_apply_to()
    {
        await ChartsAsync();
        await OnlineAsync();

        Assert.True((await new LibraryActionTool(_editor).InvokeAsync(Act("download", states: ["online", "local"], dryRun: true), TestContext.Current.CancellationToken)).TryGetValue(out var plan));

        Assert.Equal((2, 1_000L, 1), (plan!.Eligible, plan.Bytes, plan.UnknownSizes));
        Assert.Equal(new Dictionary<string, int> { ["local"] = 2 }, plan.Skipped);
        Assert.Equal(0, _downloader.Downloads);
    }

    [Fact]
    public async Task Downloads_start_in_the_background_and_respect_maxBytes()
    {
        var online = await OnlineAsync();
        var tool = new LibraryActionTool(_editor);

        Assert.True((await tool.InvokeAsync(Act("download_only", sourceId: online.ToString(), maxBytes: 500), TestContext.Current.CancellationToken)).TryGetError(out var tooBig));
        Assert.Equal("library_change_rejected", tooBig!.Code);
        Assert.Equal(0, _downloader.Downloads);

        Assert.True((await tool.InvokeAsync(Act("download", sourceId: online.ToString(), maxBytes: 5_000), TestContext.Current.CancellationToken)).TryGetValue(out var started));
        Assert.True(started!.Started);
        Assert.Equal(2, _downloader.Downloads);
        Assert.Equal(2, _loader.Loaded);
    }

    [Fact]
    public async Task Load_opens_local_items_by_id()
    {
        await ChartsAsync();
        Assert.True((await new QueryLibraryItemsTool(new ViewerLibraryController(_panel, null, a => { a(); return Task.CompletedTask; }))
            .InvokeAsync(new QueryLibraryItemsRequest(null, null, null, "51m", null, null, null, null, null, null, null, null), TestContext.Current.CancellationToken))
            .TryGetValue(out var page));

        Assert.True((await new LibraryActionTool(_editor).InvokeAsync(Act("load", ids: [page!.Items[0].Id]), TestContext.Current.CancellationToken)).TryGetValue(out var loaded));

        Assert.Equal(1, loaded!.Opened);
        Assert.Equal(1, _loader.Loaded);
    }

    [Theory]
    [InlineData("explode", true, "action")]
    [InlineData("load", false, "itemIds")]
    public async Task Bad_actions_are_rejected(string action, bool withFilter, string parameter)
    {
        await ChartsAsync();
        var result = await new LibraryActionTool(_editor).InvokeAsync(Act(action, states: withFilter ? ["local"] : null), TestContext.Current.CancellationToken);

        Assert.True(result.TryGetError(out var error));
        Assert.Equal(parameter, Assert.IsType<InvalidArgument>(error).Parameter);
    }

    [Fact]
    public async Task Cancel_all_cancels_every_download()
    {
        Assert.True((await new LibraryActionTool(_editor).InvokeAsync(Act("cancel", all: true), TestContext.Current.CancellationToken)).TryGetValue(out _));
        Assert.True(_downloader.CancelledAll);
    }

    // ── refresh / remove / idle ────────────────────────────────────────

    [Fact]
    public async Task Refresh_reports_counts_after_reindexing()
    {
        var charts = await ChartsAsync();

        Assert.True((await new RefreshLibrarySourceTool(_editor).InvokeAsync(charts.ToString(), 10_000, TestContext.Current.CancellationToken)).TryGetValue(out var refreshed));

        Assert.True(refreshed!.Waited);
        Assert.False(refreshed.TimedOut);
        Assert.Equal((2, 0, 0), (refreshed.Total, refreshed.Added, refreshed.Removed));
        Assert.Equal(new Dictionary<string, int> { ["local"] = 2 }, refreshed.Counts);
    }

    [Fact]
    public async Task Remove_needs_confirmation()
    {
        var charts = await ChartsAsync();
        var tool = new RemoveLibrarySourceTool(_editor);

        Assert.True((await tool.InvokeAsync(charts.ToString(), confirm: null, ct: TestContext.Current.CancellationToken)).TryGetError(out var unconfirmed));
        Assert.Equal("confirm", Assert.IsType<InvalidArgument>(unconfirmed).Parameter);
        Assert.Single(_library.Collections);

        Assert.True((await tool.InvokeAsync(charts.ToString(), confirm: true, ct: TestContext.Current.CancellationToken)).TryGetValue(out var removed));
        Assert.Equal(("Charts", true, 2), (removed!.Name, removed.WasCollection, removed.ItemCount));
        Assert.Empty(_library.Collections);
    }

    [Fact]
    public async Task Idle_reports_at_once_when_nothing_runs()
    {
        await ChartsAsync();

        Assert.True((await new AwaitLibraryIdleTool(_editor).InvokeAsync(1_000, TestContext.Current.CancellationToken)).TryGetValue(out var idle));

        Assert.True(idle!.Idle);
        Assert.Null(idle.Downloads);
    }

    [Fact]
    public void Every_adapter_builds_its_tool()
    {
        var tools = new[]
        {
            LibraryEditMcpAdapters.Create(new AddLibrarySourceTool(_editor)),
            LibraryEditMcpAdapters.Create(new RefreshLibrarySourceTool(_editor)),
            LibraryEditMcpAdapters.Create(new LibraryActionTool(_editor)),
            LibraryEditMcpAdapters.Create(new RemoveLibrarySourceTool(_editor)),
            LibraryEditMcpAdapters.Create(new AwaitLibraryIdleTool(_editor)),
        };

        Assert.Equal(
            ["add_library_source", "refresh_library_source", "library_action", "remove_library_source", "await_library_idle"],
            tools.Select(tool => tool.ProtocolTool.Name));
    }

    /// <summary>Lists two online cells for a community list: one 1 000 bytes, one of unknown size.</summary>
    private sealed class RemoteIndexer : ICollectionSourceIndexer
    {
        public bool CanIndex(CollectionSource source) => source is ChartCatalogsFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
        {
            CollectionItem Cell(string name, long? size) => new()
            {
                Key = name,
                ProductSpec = "S-57",
                Name = name,
                Location = new RemoteItemLocation(new Uri($"https://example.test/{name}.zip"), size, null, "community/TEST", null, new PackageLayout($"{name}.000", [])),
            };
            return ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [Cell("RO1", 1_000), Cell("RO2", null)], []));
        }
    }

    private sealed class RecordingLoader : ILibraryLoader
    {
        public int Loaded { get; private set; }

        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) => LibraryLoadState.None;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default)
        {
            Loaded += items.Count;
            return Task.FromResult(new LibraryLoadResult(items.Count, 0));
        }
    }

    private sealed class RecordingDownloader : ILibraryDownloader
    {
        public int Downloads { get; private set; }

        public bool CancelledAll { get; private set; }

        public event EventHandler? Changed { add { } remove { } }

        public void CancelAll() => CancelledAll = true;

        public CollectionItem Localize(CollectionItem item) => item;

        public bool IsOutdated(CollectionItem item) => false;

        public bool CanDownload(CollectionItem item) => item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
        {
            Downloads += items.Count;
            return Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
        }
    }

    private sealed class NoImporter : ILibraryImporter
    {
        public Task AddFolderAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddExchangeSetZipAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddOnlineCatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddSharedFeedAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddKnownCatalogueAsync(KnownCatalogueSource source, Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddS128CatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddCollectionManifestAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source) => Task.CompletedTask;

        public Task AddPathAsync(string path, Guid? targetCollectionId) => Task.CompletedTask;

        public bool IsInLibrary(string path) => false;
    }
}
