using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The Library panel's details pane and map taps on their own (#1396), without a panel.
/// </summary>
public sealed class LibraryDetailsAndTapTests : IDisposable
{
    private readonly LibraryTestContext _context = new();
    private readonly CollectionLibrary _library;
    private readonly FakeLibraryLoader _loader = new();
    private readonly FakeLibraryDownloader _downloader = new();

    public LibraryDetailsAndTapTests()
    {
        _library = _context.CreateService(CollectionIndexer.CreateDefault(feeds: [new RemoteIndexer()]));
        _library.Initialize();
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private async Task<(TestLibrary Library, LibraryItemViewModel[] Rows)> OnlineRowsAsync()
    {
        _library.AddCollection("Online", [new ChartCatalogsFeedSource(
            Guid.NewGuid(), null, new Uri("https://example.test/TEST_Catalog.xml"), ChartCatalogsFilter.All)]);
        await _library.WhenIdle();
        var source = _library.Collections.Single(c => !c.IsSession).Sources.Single();
        var library = new TestLibrary(_library, _loader, _downloader);
        return (library, [.. source.Index!.Items.Select(item => library.Rows.Create(item, source))]);
    }

    [Fact]
    public async Task The_details_act_on_the_dataset_they_are_given()
    {
        var (library, rows) = await OnlineRowsAsync();
        var details = new LibraryDetailsViewModel(
            _library, _downloader, library.Open, library.Download, library.OpenAsRoute, library.LoadLatest, library.PinRun, viewTime: null);
        Assert.False(details.LoadOrDownloadCommand.CanExecute(null));
        Assert.False(details.DownloadOnlyCommand.CanExecute(null));

        details.Item = rows[0];

        // An online cell downloads; it can't load until it has.
        Assert.True(details.DownloadOnlyCommand.CanExecute(null));
        Assert.False(details.LoadCommand.CanExecute(null));
        Assert.False(details.CanOpenAsRoute);
        Assert.False(details.HasValidWindow);

        await ((CommunityToolkit.Mvvm.Input.IAsyncRelayCommand)details.DownloadOnlyCommand).ExecuteAsync(null);
        Assert.Equal(1, _downloader.Downloads);
        Assert.Empty(_loader.Calls);
    }

    [Fact]
    public async Task A_tap_selects_in_its_list_and_List_these_lists_the_point()
    {
        var (_, rows) = await OnlineRowsAsync();
        var list = new FakeList(rows);
        var tap = new LibraryTapViewModel(list);
        var point = new GeoPosition(50, -1);

        tap.SelectTapHits(point, [rows[1], rows[0]], sameSpot: false);

        Assert.Same(rows[1], list.SelectedItem);
        Assert.True(tap.HasTap);
        Assert.Equal("· 1 of 2", tap.TapPositionText);

        // Picking a row that isn't a hit ends the tap.
        list.SelectedItem = null;
        tap.OnSelectionChanged();
        Assert.False(tap.HasTap);

        tap.SelectTapHits(point, [rows[1], rows[0]], sameSpot: false);
        tap.ListTapHitsCommand.Execute(null);
        Assert.Equal([(GeoPosition?)point], list.ListedAt);
        Assert.True(tap.HasLocation);
        Assert.False(tap.HasTap);

        tap.ClearLocationCommand.Execute(null);
        Assert.Equal([point, null], list.ListedAt);
        Assert.False(tap.HasLocation);
    }

    [Fact]
    public async Task The_panel_hands_the_list_selection_to_the_details()
    {
        var (library, _) = await OnlineRowsAsync();
        using var panel = library.CreatePanel(new RecordingLibraryImporter(), action => action(), coalesce: action => action(),
            time: TimeProvider.System, feedHealth: null, viewTime: null, timeFormat: null, sync: null);
        panel.Tree.SelectedNode = panel.Tree.Nodes.Single(n => n.Source is null && n.Children.Count > 0).Children.Single();
        Assert.True(panel.ShowsItemPlaceholder);

        panel.List.SelectedItem = panel.List.Items[1];

        Assert.Same(panel.List.Items[1], panel.Details.Item);
        Assert.False(panel.ShowsItemPlaceholder);
        Assert.Same(panel.List.Tap, panel.Tap);

        panel.List.SelectedItem = null;
        Assert.Null(panel.Details.Item);
        Assert.True(panel.ShowsItemPlaceholder);
    }

    /// <summary>A list of fixed rows that records what the taps ask it to list.</summary>
    private sealed class FakeList(IReadOnlyList<LibraryItemViewModel> items) : ILibraryTapList
    {
        public IReadOnlyList<LibraryItemViewModel> Items => items;

        public int ListedCount => items.Count;

        public LibraryItemViewModel? SelectedItem { get; set; }

        public List<GeoPosition?> ListedAt { get; } = [];

        public void ListAt(GeoPosition? position) => ListedAt.Add(position);
    }

    /// <summary>Indexes two online cells with a stated layout.</summary>
    private sealed class RemoteIndexer : ICollectionSourceIndexer
    {
        public bool CanIndex(CollectionSource source) => source is ChartCatalogsFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken)
        {
            CollectionItem Cell(string name) => new()
            {
                Key = name,
                ProductSpec = "S-57",
                Name = name,
                Location = new RemoteItemLocation(new Uri($"https://example.test/{name}.zip"), 1_000, null, "community/TEST", null, new PackageLayout($"{name}.000", [])),
            };
            return ValueTask.FromResult(new SourceIndex(source.Id, DateTimeOffset.UtcNow, null, [Cell("RO1"), Cell("RO2")], []));
        }
    }
}
