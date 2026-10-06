using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>The Library read MCP tools (#715 slice 2).</summary>
public sealed class LibraryToolsTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly StubLoader _loader = new();
    private readonly StubDownloader _downloader = new();
    private readonly List<KnownCatalogueSource> _userCatalogues = [];

    public LibraryToolsTests()
    {
        _library = _context.CreateService();
        _library.Initialize();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private static Task Immediate(Action action)
    {
        action();
        return Task.CompletedTask;
    }

    private async Task<(LibraryPanelViewModel Panel, ViewerLibraryController Controller)> ChartsAsync()
    {
        _library.AddCollection("Charts", [new ExchangeSetSource(Guid.NewGuid(), null, _context.CreateS57ExchangeSet("Charts"))]);
        await _library.WhenIdle();
        var panel = new LibraryPanelViewModel(_library, new StubImporter(), _loader, _downloader, action => action());
        return (panel, new ViewerLibraryController(panel, () => _userCatalogues, Immediate));
    }

    private static QueryLibraryItemsRequest Query(
        string? sourceId = null, string[]? states = null, string? text = null,
        double? lat = null, double? lon = null, int? page = null, int? pageSize = null) =>
        new(sourceId, states, null, text, null, null, null, null, lat, lon, page, pageSize);

    [Fact]
    public async Task Sources_report_kind_counts_and_state_tallies()
    {
        var (panel, controller) = await ChartsAsync();
        using var _ = panel;

        Assert.True((await new ListLibrarySourcesTool(controller).InvokeAsync(counts: true, ct: TestContext.Current.CancellationToken)).TryGetValue(out var result));

        var collection = Assert.Single(result!.Collections);
        Assert.Equal("Charts", collection.Name);
        Assert.Equal(2, collection.ItemCount);
        var source = Assert.Single(collection.Sources);
        Assert.Equal("ready", source.IndexState);
        Assert.NotNull(source.IndexedAt);
        Assert.Equal(new Dictionary<string, int> { ["local"] = 2 }, source.Counts);
    }

    [Fact]
    public async Task Items_filter_by_state_text_and_page()
    {
        var (panel, controller) = await ChartsAsync();
        using var _ = panel;
        var tool = new QueryLibraryItemsTool(controller);

        Assert.True((await tool.InvokeAsync(Query(states: ["local"]), TestContext.Current.CancellationToken)).TryGetValue(out var local));
        Assert.Equal(["US5WA51M", "US5WA52M"], local!.Items.Select(i => i.Name));
        Assert.All(local.Items, i => Assert.NotNull(i.LocalPath));

        Assert.True((await tool.InvokeAsync(Query(text: "52m"), TestContext.Current.CancellationToken)).TryGetValue(out var text));
        Assert.Equal("US5WA52M", Assert.Single(text!.Items).Name);

        Assert.True((await tool.InvokeAsync(Query(pageSize: 1), TestContext.Current.CancellationToken)).TryGetValue(out var first));
        Assert.Equal((2, true), (first!.Total, first.HasMore));
        Assert.True((await tool.InvokeAsync(Query(page: 1, pageSize: 1), TestContext.Current.CancellationToken)).TryGetValue(out var second));
        Assert.False(second!.HasMore);

        _loader.State = LibraryLoadState.Loaded;
        Assert.True((await tool.InvokeAsync(Query(states: ["loaded"]), TestContext.Current.CancellationToken)).TryGetValue(out var loaded));
        Assert.Equal(2, loaded!.Total);
        Assert.All(loaded.Items, i => Assert.Equal("loaded", i.State));
    }

    [Fact]
    public async Task A_point_query_returns_what_covers_it()
    {
        var (panel, controller) = await ChartsAsync();
        using var _ = panel;
        var tool = new QueryLibraryItemsTool(controller);
        Assert.True((await tool.InvokeAsync(Query(text: "51m"), TestContext.Current.CancellationToken)).TryGetValue(out var target));
        var bounds = Assert.Single(target!.Items).Bounds!;

        Assert.True((await tool.InvokeAsync(Query(
            lat: (bounds.South + bounds.North) / 2, lon: (bounds.West + bounds.East) / 2), TestContext.Current.CancellationToken)).TryGetValue(out var hits));

        Assert.Contains(hits!.Items, i => i.Name == "US5WA51M");
    }

    [Fact]
    public async Task Bad_queries_are_rejected()
    {
        var (panel, controller) = await ChartsAsync();
        using var _ = panel;
        var tool = new QueryLibraryItemsTool(controller);

        Assert.True((await tool.InvokeAsync(Query(states: ["stale"]), TestContext.Current.CancellationToken)).TryGetError(out var state));
        Assert.Equal("states", Assert.IsType<InvalidArgument>(state).Parameter);
        Assert.True((await tool.InvokeAsync(Query(sourceId: Guid.NewGuid().ToString()), TestContext.Current.CancellationToken)).TryGetError(out var source));
        Assert.Equal("library_source_not_found", source!.Code);
        Assert.True((await tool.InvokeAsync(Query(lat: 47), TestContext.Current.CancellationToken)).TryGetError(out var point));
        Assert.Equal("lat", Assert.IsType<InvalidArgument>(point).Parameter);
        Assert.True((await tool.InvokeAsync(Query(pageSize: 501), TestContext.Current.CancellationToken)).TryGetError(out var size));
        Assert.Equal("pageSize", Assert.IsType<InvalidArgument>(size).Parameter);
    }

    [Fact]
    public async Task An_item_is_described_as_its_details_pane_shows_it()
    {
        var (panel, controller) = await ChartsAsync();
        using var _ = panel;
        Assert.True((await new QueryLibraryItemsTool(controller).InvokeAsync(Query(text: "51m"), TestContext.Current.CancellationToken)).TryGetValue(out var page));
        var id = Assert.Single(page!.Items).Id;
        var describe = new DescribeLibraryItemTool(controller);

        Assert.True((await describe.InvokeAsync(id, TestContext.Current.CancellationToken)).TryGetValue(out var detail));
        Assert.Equal("US5WA51M", detail!.Item.Name);
        Assert.NotEmpty(detail.Details);
        Assert.All(detail.Details, group => Assert.NotEmpty(group.Fields));

        Assert.True((await describe.InvokeAsync($"{Guid.NewGuid()}:US5WA51M", TestContext.Current.CancellationToken)).TryGetError(out var missing));
        Assert.Equal("library_item_not_found", missing!.Code);
    }

    [Fact]
    public async Task Known_sources_list_the_curated_entries_then_the_users_with_tokens_masked()
    {
        var (panel, controller) = await ChartsAsync();
        using var _ = panel;
        _userCatalogues.Add(KnownCatalogueSources.FromUrl(
            new Uri("http://bridge-pc:8100/secret-token-3f9a/feed.json"), KnownCatalogueFormat.S100Feed, "Bridge"));

        Assert.True((await new ListKnownSourcesTool(controller).InvokeAsync(TestContext.Current.CancellationToken)).TryGetValue(out var result));

        var s111 = Assert.Single(result!.Sources, s => s.Id == "noaa-s111");
        Assert.False(s111.UserAdded);
        Assert.Contains(s111.Models, m => m.Id == "cbofs");
        var user = result.Sources[^1];
        Assert.True(user.UserAdded);
        Assert.DoesNotContain("secret-token", user.Url, StringComparison.Ordinal);
        Assert.EndsWith("3f9a/feed.json", user.Url, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("5f0c6f3e-6b5e-4f0a-9d1e-2a7c1d3b4e5f:US5WA51M", true)]
    [InlineData("5f0c6f3e-6b5e-4f0a-9d1e-2a7c1d3b4e5f:a:b", true)]
    [InlineData("US5WA51M", false)]
    [InlineData("not-a-guid:US5WA51M", false)]
    public void Item_ids_parse(string id, bool valid) =>
        Assert.Equal(valid, LibraryItemState.TryParseId(id, out _, out _));

    [Fact]
    public async Task Every_adapter_builds_its_tool()
    {
        var (panel, controller) = await ChartsAsync();
        using var _ = panel;

        var tools = new[]
        {
            LibraryMcpAdapters.Create(new ListLibrarySourcesTool(controller)),
            LibraryMcpAdapters.Create(new QueryLibraryItemsTool(controller)),
            LibraryMcpAdapters.Create(new DescribeLibraryItemTool(controller)),
            LibraryMcpAdapters.Create(new ListKnownSourcesTool(controller)),
        };

        Assert.Equal(
            ["list_library_sources", "query_library_items", "describe_library_item", "list_known_sources"],
            tools.Select(tool => tool.ProtocolTool.Name));
    }

    private sealed class StubLoader : ILibraryLoader
    {
        public LibraryLoadState State { get; set; }

        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) => State;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryLoadResult(items.Count, 0));
    }

    private sealed class StubDownloader : ILibraryDownloader
    {
        public LibraryDownloadProgress? Progress => null;

        public event EventHandler? ProgressChanged { add { } remove { } }

        public event EventHandler? Changed { add { } remove { } }

        public void CancelAll() { }

        public CollectionItem Localize(CollectionItem item) => item;

        public bool IsOutdated(CollectionItem item) => false;

        public bool CanDownload(CollectionItem item) => item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
    }

    private sealed class StubImporter : ILibraryImporter
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
