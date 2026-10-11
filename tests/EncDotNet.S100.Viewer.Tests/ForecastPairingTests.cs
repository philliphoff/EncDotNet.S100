using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.Collections.RemoteCatalogues;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// #685 slice 5 (handoff A4, B7, B8, E1): S-111 model domains on the map, the
/// S-104 pilot, and pairing S-111 tiles with S-102 bathymetry on the shared grid.
/// </summary>
public sealed class ForecastPairingTests : IDisposable
{
    private static readonly Uri ModelsUri = new("https://noaa-s111-pds.s3.amazonaws.com/ed1.0.1/model_forecast_guidance/");

    private static readonly Uri S102Uri = new("https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/S100_ROOT/CATALOG.XML");

    private static readonly ForecastModel Cbofs = new("cbofs", "Chesapeake Bay", 6, 48);

    private static readonly DateTimeOffset Run = new(2026, 9, 30, 18, 0, 0, TimeSpan.Zero);

    private readonly LibraryTestContext _context = new();
    private readonly FakeTimeProvider _time = new(Run.AddHours(1));
    private readonly Downloader _downloader;
    private readonly RecordingImporter _importer = new();
    private readonly CollectionLibrary _library;

    public ForecastPairingTests()
    {
        _downloader = new Downloader(Path.Combine(_context.Root, "downloads"));
        _library = _context.CreateService(new CollectionIndexer([new Indexer()]));
    }

    public void Dispose()
    {
        _library.Dispose();
        _context.Dispose();
    }

    private static CollectionItem CurrentTile(string cell, double south, double west) => new()
    {
        Key = "cbofs/111US00_CBOFS_" + cell,
        ProductSpec = "S-111",
        Name = "111US00_CBOFS_" + cell,
        Title = "Chesapeake Bay",
        Bounds = new GeoBounds(south, west, south + 0.3, west + 0.3),
        Location = new RemoteItemLocation(new Uri(ModelsUri, $"cbofs/x/{cell}.h5"), 500_000, Run),
        Properties = new Dictionary<string, string>
        {
            [S100ForecastFeedIndexer.ModelProperty] = "cbofs",
            [S100ForecastFeedIndexer.RunProperty] = Run.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            [S100ForecastFeedIndexer.ValidToProperty] = Run.AddHours(48).ToString("yyyy-MM-ddTHH:mm:ssZ"),
        },
    };

    private static CollectionItem BathymetryTile(string name, double south, double west, string folder = "Mid_Atlantic/Chesapeake_Bay") => new()
    {
        Key = folder + "/" + name,
        ProductSpec = "S-102",
        Name = name,
        Edition = 3,
        Update = 0,
        Bounds = new GeoBounds(south, west, south + 0.3, west + 0.3),
        Location = new RemoteItemLocation(new Uri(S102Uri, $"../{folder}/{name}262247.h5"), 3_100_000),
        Properties = new Dictionary<string, string> { [RemoteS100Catalogue.FolderProperty] = folder },
    };

    private async Task<LibraryPanelViewModel> PanelAsync(bool withBathymetry = true)
    {
        _library.Initialize();
        _library.AddCollection("Currents", [new S100ForecastFeedSource(Guid.NewGuid(), null, ModelsUri, [Cbofs])]);
        if (withBathymetry)
        {
            _library.AddCollection("Bathymetry", [new S100CatalogueFeedSource(Guid.NewGuid(), null, S102Uri, S100CatalogueFilter.All)]);
        }

        await _library.WhenIdle();
        var panel = new LibraryPanelViewModel(_library, _importer, new NullLoader(), _downloader, action => action(), time: _time);
        panel.Sync();
        return panel;
    }

    private static LibraryNodeViewModel SourceOf(LibraryPanelViewModel panel, string collection) =>
        panel.Nodes.Single(n => n.Name == collection).Children.Single();

    [Fact]
    public async Task A_forecast_model_is_one_domain_outline_at_every_scale()
    {
        using var panel = await PanelAsync(withBathymetry: false);
        panel.SelectedNode = SourceOf(panel, "Currents");

        foreach (var scale in new[] { 50_000d, 5_000_000d })
        {
            var domain = Assert.Single(LibraryCoverageOverlayController.Areas(panel.Items, scale));
            Assert.Equal("cbofs", domain.Area.Model);
            Assert.Equal(2, domain.Items.Count);
            Assert.Empty(LibraryCoverageOverlayController.Candidates(panel.Items, scale));
        }

        // The two adjacent tiles make one shape.
        Assert.Single(LibraryCoverageOverlayController.Areas(panel.Items, 50_000)[0].Area.Rings);
    }

    [Fact]
    public async Task An_expired_runs_domain_is_red()
    {
        _downloader.Have("111US00_CBOFS_US4VA1DD", Run);
        _downloader.Have("111US00_CBOFS_US4VA1DE", Run);
        _time.Advance(TimeSpan.FromHours(50));
        using var panel = await PanelAsync(withBathymetry: false);
        panel.SelectedNode = SourceOf(panel, "Currents");

        var domain = Assert.Single(LibraryCoverageOverlayController.Areas(panel.Items, 50_000));

        Assert.Equal(LibraryPrimaryAvailability.Expired, LibraryCoverageOverlayController.AreaState(domain.Items));
    }

    [Fact]
    public async Task Tapping_a_domain_lists_what_is_there_and_selects_its_model()
    {
        using var panel = await PanelAsync(withBathymetry: false);
        var source = SourceOf(panel, "Currents");
        panel.SelectedNode = source;

        panel.ListModelsAt(new GeoPosition(37.6, -76.6), [(source.Id, "cbofs")]);

        Assert.True(panel.HasLocation);
        Assert.True(panel.SelectedItem!.IsModelHeader);
        Assert.Equal("cbofs", panel.SelectedItem.Name);
    }

    [Fact]
    public async Task An_S111_tile_offers_its_S102_twin_in_the_same_grid_cell()
    {
        using var panel = await PanelAsync();
        panel.SelectedNode = SourceOf(panel, "Currents");
        panel.Items.Single(i => i.IsModelHeader).ToggleCommand!.Execute(null);

        panel.SelectedItem = panel.Items.Single(i => i.Name == "111US00_CBOFS_US4VA1DD");

        var pairing = Assert.IsType<LibraryPairing>(panel.Pairing);
        Assert.True(panel.HasPairing);
        Assert.Equal("Bathymetry for this tile", pairing.Title);
        Assert.Equal("S-102 102US004VA1DD · same grid cell · Online · " + LibraryItemViewModel.FormatBytes(3_100_000), pairing.Detail);
        Assert.True(pairing.CanGet);

        pairing.GetCommand.Execute(null);
        Assert.Equal(["102US004VA1DD"], _downloader.Requested);

        // A tile whose cell has no S-102 tile listed offers nothing.
        panel.SelectedItem = panel.Items.Single(i => i.Name == "111US00_CBOFS_US4VA1DE");
        Assert.Null(panel.Pairing);
    }

    [Fact]
    public async Task A_local_twin_is_shown_without_Get()
    {
        _downloader.Have("102US004VA1DD", Run);
        using (var panel = await PanelAsync())
        {
            panel.SelectedNode = SourceOf(panel, "Currents");
            panel.Items.Single(i => i.IsModelHeader).ToggleCommand!.Execute(null);
            panel.SelectedItem = panel.Items.Single(i => i.Name == "111US00_CBOFS_US4VA1DD");

            Assert.False(panel.Pairing!.CanGet);
            Assert.Contains("· Local", panel.Pairing.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Without_an_S102_collection_a_tile_offers_no_bathymetry()
    {
        using var panel = await PanelAsync(withBathymetry: false);
        panel.SelectedNode = SourceOf(panel, "Currents");
        panel.Items.Single(i => i.IsModelHeader).ToggleCommand!.Execute(null);

        panel.SelectedItem = panel.Items.Single(i => i.Name == "111US00_CBOFS_US4VA1DD");

        Assert.False(panel.HasPairing);
    }

    [Fact]
    public async Task An_S102_area_offers_currents_for_it()
    {
        using var panel = await PanelAsync();
        var bathymetry = SourceOf(panel, "Bathymetry");
        panel.SelectedNode = bathymetry;
        Assert.False(panel.CanAddCurrentsForArea);  // the source, not an area
        panel.SelectedNode = SourceOf(panel, "Currents");
        Assert.False(panel.CanAddCurrentsForArea);

        panel.SelectedNode = bathymetry.Children.Single(c => c.Name == "Chesapeake Bay");
        Assert.True(panel.CanAddCurrentsForArea);
        panel.AddCurrentsForAreaCommand.Execute(null);

        Assert.Equal([new GeoBounds(37.5, -76.8, 37.8, -76.5)], _importer.AreasForCurrents);
    }

    [Fact]
    public async Task Currents_for_an_area_tick_the_models_reaching_it_but_not_regional_ones_covering_them()
    {
        var known = KnownCatalogueSources.Find("noaa-s111")!;
        var area = new GeoBounds(37.5, -76.8, 38.1, -76.2);
        IReadOnlyList<ForecastModelSummary> Summaries(IReadOnlyList<ForecastModel> models) => models.Select(m => m.Id switch
        {
            "cbofs" => new ForecastModelSummary(m, Run, 2, 1, 1, null) { TileBounds = [new GeoBounds(37.5, -76.8, 37.8, -76.5)] },
            "rtofs_east" => new ForecastModelSummary(m, Run, 1, 1, 1, null) { TileBounds = [new GeoBounds(33.6, -81.6, 43.2, -72)] },
            _ => new ForecastModelSummary(m, Run, 1, 1, 1, null) { TileBounds = [new GeoBounds(10, 10, 11, 11)] },
        }).ToArray();
        var vm = new AddToLibraryDialogViewModel(_library, new LibraryCatalogueReaders { ForecastModels = (_, models, _) => Task.FromResult(Summaries(models)) });
        vm.Initialize(known, targetCollectionId: null);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        vm.PreselectModelsCovering(area);

        Assert.Equal(["cbofs"], vm.Choices().Where(o => o.IsSelected).Select(o => o.Value));
        Assert.False(vm.IncludeAll);
    }

    [Fact]
    public async Task The_S104_pilot_is_one_model_with_an_ended_forecast_and_a_pilot_chip()
    {
        var known = KnownCatalogueSources.Find("noaa-s104")!;
        var pilotRun = new DateTimeOffset(2025, 12, 17, 12, 0, 0, TimeSpan.Zero);
        var vm = new AddToLibraryDialogViewModel(_library, new LibraryCatalogueReaders
        {
            ForecastModels = (_, models, _) => Task.FromResult<IReadOnlyList<ForecastModelSummary>>(
                [new ForecastModelSummary(models[0], pilotRun, 4, 5_349_334, null, null)]),
        }, _time);

        vm.Initialize(known, targetCollectionId: null);
        await vm.LoadCatalogAsync(TestContext.Current.CancellationToken);

        Assert.True(vm.IsSingleEntry);
        Assert.Equal("Charleston Harbor", vm.SingleEntry!.Label);
        Assert.Equal("4 tiles · " + LibraryItemViewModel.FormatBytes(5_349_334), vm.SingleEntry.Detail);
        Assert.False(vm.HasForecastShapes);
        Assert.True(vm.HasForecastEndedNote);
        Assert.StartsWith("Forecast ended " + new DateTime(2025, 12, 25).ToString("d"), vm.ForecastEndedNote, StringComparison.Ordinal);
        Assert.True(vm.CanContinueFromScope);

        var entry = new CatalogueEntryViewModel(known, openUrl: null);
        Assert.True(entry.IsPilot);
        Assert.Equal("S-104 · AWS", entry.FormatText);
    }

    [Theory]
    [InlineData("111US00_CBOFS_US4VA1DD", "102US004VA1DD")]
    [InlineData("111US00_RTOFS_EAST_US2GOMBD", "102US002GOMBD")]
    [InlineData("111US00_CBOFS", null)]
    public void S111_tiles_name_their_S102_twin(string tile, string? twin)
    {
        Assert.Equal(twin, ForecastRuns.BathymetryTwinOf(tile));
    }

    private sealed class Indexer : ICollectionSourceIndexer
    {
        public bool CanIndex(CollectionSource source) => source is S100ForecastFeedSource or S100CatalogueFeedSource;

        public ValueTask<string?> GetFingerprintAsync(CollectionSource source, CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask<SourceIndex> IndexAsync(CollectionSource source, IProgress<IndexProgress>? progress, CancellationToken cancellationToken) =>
            ValueTask.FromResult(source is S100ForecastFeedSource
                ? new SourceIndex(source.Id, Run, null, [CurrentTile("US4VA1DD", 37.5, -76.8), CurrentTile("US4VA1DE", 37.5, -76.5)], [])
                : new SourceIndex(source.Id, Run, null,
                [
                    BathymetryTile("102US004VA1DD", 37.5, -76.8),
                    BathymetryTile("102US005MA1RF", 42.3, -71.0, "Northeast/Boston"),
                ], [])
                {
                    Groups =
                    [
                        new SourceIndexGroup("Mid_Atlantic/Chesapeake_Bay", "Chesapeake Bay"),
                        new SourceIndexGroup("Northeast/Boston", "Boston"),
                    ],
                });
    }

    private sealed class Downloader(string root) : ILibraryDownloader
    {
        private readonly Dictionary<string, DateTimeOffset> _runs = [];

        public List<string> Requested { get; } = [];

        public event EventHandler? Changed { add { } remove { } }

        public void Have(string name, DateTimeOffset run)
        {
            Directory.CreateDirectory(Path.Combine(root, name));
            File.WriteAllBytes(Path.Combine(root, name, name + ".h5"), [1]);
            _runs[name] = run;
        }

        public CollectionItem Localize(CollectionItem item) => _runs.ContainsKey(item.Name)
            ? item with { Location = new LocalItemLocation(Path.Combine(root, item.Name), item.Name + ".h5", []) }
            : item;

        public bool IsOutdated(CollectionItem item) => false;

        public DateTimeOffset? LocalPublishedAtOf(CollectionItem item) => _runs.TryGetValue(item.Name, out var run) ? run : null;

        public bool CanDownload(CollectionItem item) => item.Location is RemoteItemLocation;

        public Task<LibraryDownloadResult> DownloadAsync(IReadOnlyList<CollectionItem> items, CancellationToken cancellationToken = default)
        {
            Requested.AddRange(items.Select(i => i.Name));
            return Task.FromResult(new LibraryDownloadResult(items.Count, 0, false));
        }
    }

    private sealed class NullLoader : ILibraryLoader
    {
        public event EventHandler? Changed { add { } remove { } }

        public LibraryLoadState StateOf(CollectionItem item) => default;

        public Task<LibraryLoadResult> LoadAsync(IReadOnlyList<CollectionItem> items, bool defer, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LibraryLoadResult(0, 0));
    }

    private sealed class RecordingImporter : ILibraryImporter
    {
        public List<GeoBounds> AreasForCurrents { get; } = [];

        public Task AddFolderAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddExchangeSetZipAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddOnlineCatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddSharedFeedAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddKnownCatalogueAsync(KnownCatalogueSource source, Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddS128CatalogueAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task AddCollectionManifestAsync(Guid? targetCollectionId) => Task.CompletedTask;

        public Task ChooseManifestGroupsAsync(Guid collectionId, LocalManifestSource source) => Task.CompletedTask;

        public Task AddCurrentsForAreaAsync(GeoBounds area, Guid? targetCollectionId)
        {
            AreasForCurrents.Add(area);
            return Task.CompletedTask;
        }

        public Task AddPathAsync(string path, Guid? targetCollectionId) => Task.CompletedTask;

        public bool IsInLibrary(string path) => false;
    }
}
