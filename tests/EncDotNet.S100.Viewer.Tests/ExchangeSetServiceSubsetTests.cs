using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Tests for <see cref="ExchangeSetService.OpenSubsetAsync"/> — opening the
/// datasets chosen in the Library panel rather than a whole exchange set
/// (issue #655).
/// </summary>
public sealed class ExchangeSetServiceSubsetTests
{
    private static string Fixture(string name) => LibraryTestContext.Datasets("ExchangeSets", name);

    private static (DatasetsViewModel Datasets, ExchangeSetService Service, CountingLoader Loader) CreateSystem()
    {
        var loader = new CountingLoader();
        var datasets = new DatasetsViewModel(loader);
        return (datasets, new ExchangeSetService(datasets, Notifications.TestNotifications.Create()), loader);
    }

    private static ExchangeSetSubsetItem Cell(string name) =>
        new($"{name}/{name}.000", [], "S-57", name);

    [Fact]
    public async Task Opens_only_the_requested_items_under_one_header()
    {
        var (datasets, service, loader) = CreateSystem();
        using var _ = service;
        var root = Fixture("Synthetic-S57-Framed");

        var entries = await service.OpenSubsetAsync(
            new ExchangeSetSubsetRequest(root, "CATALOG.031", [Cell("US5WA52M")]), defer: false);

        var entry = Assert.Single(entries);
        Assert.Equal("US5WA52M", entry.DisplayName);
        Assert.Equal(Path.Combine("US5WA52M", "US5WA52M.000"), entry.RelativePath);
        Assert.Equal(5, entry.UsageBand);
        Assert.False(entry.IsDeferred);  // loaded now, not left to the lazy loader
        Assert.Single(datasets.Entries);
        Assert.Equal(1, loader.Loads);
        var header = Assert.Single(datasets.ExchangeSetHeaders);
        Assert.Equal(1, header.LoadedCount);
    }

    [Fact]
    public async Task A_second_subset_of_the_same_set_reuses_its_header_and_entries()
    {
        var (datasets, service, _) = CreateSystem();
        using var __ = service;
        var root = Fixture("Synthetic-S57-Framed");

        var first = await service.OpenSubsetAsync(
            new ExchangeSetSubsetRequest(root, "CATALOG.031", [Cell("US5WA51M")]), defer: false);
        var second = await service.OpenSubsetAsync(
            new ExchangeSetSubsetRequest(root, "CATALOG.031", [Cell("US5WA51M"), Cell("US5WA52M")]), defer: false);

        Assert.Same(first[0], second[0]);
        Assert.Equal(2, datasets.Entries.Count);
        Assert.Equal(2, Assert.Single(datasets.ExchangeSetHeaders).LoadedCount);
    }

    [Fact]
    public async Task A_set_already_opened_from_the_File_menu_is_reused()
    {
        var (datasets, service, _) = CreateSystem();
        using var __ = service;
        var root = Fixture("Synthetic-S57-Framed");
        await service.OpenAsync(root);

        var entries = await service.OpenSubsetAsync(
            new ExchangeSetSubsetRequest(root + Path.DirectorySeparatorChar, "CATALOG.031", [Cell("US5WA52M")]), defer: false);

        Assert.Contains(entries[0], datasets.Entries);
        Assert.Equal(2, datasets.Entries.Count);
        Assert.Single(datasets.ExchangeSetHeaders);
    }

    [Fact]
    public async Task S100_subset_carries_updates_and_display_scales()
    {
        var (datasets, service, _) = CreateSystem();
        using var __ = service;

        var entries = await service.OpenSubsetAsync(
            new ExchangeSetSubsetRequest(
                Fixture("Synthetic-S101Updates"),
                "CATALOG.XML",
                [new ExchangeSetSubsetItem("S-101/SYNTH101.000", ["S-101/SYNTH101.001", "S-101/SYNTH101.002"], "S-101", "SYNTH101",
                    MinimumDisplayScale: 90_000, MaximumDisplayScale: 12_000)]),
            defer: false);

        var entry = Assert.Single(entries);
        Assert.Equal(2, entry.UpdateRelativePaths.Count);
        Assert.Equal(90_000, entry.MinimumDisplayScale);
        Assert.Null(entry.UsageBand);  // S-101 names carry no S-57 band
        Assert.Single(datasets.ExchangeSetHeaders);
    }

    [Fact]
    public async Task Closing_the_header_releases_the_subset()
    {
        var (datasets, service, _) = CreateSystem();
        using var __ = service;
        await service.OpenSubsetAsync(
            new ExchangeSetSubsetRequest(Fixture("Synthetic-S57-Framed"), "CATALOG.031", [Cell("US5WA51M")]), defer: false);

        Assert.Single(datasets.ExchangeSetHeaders).CloseCommand.Execute(null);

        Assert.Empty(datasets.Entries);
        Assert.Empty(datasets.ExchangeSetHeaders);
    }

    [Fact]
    public async Task Deferred_open_keeps_its_header_through_the_batch_insert()
    {
        // Regression: the batch registration raises a collection Reset before
        // the tracked set's entries are recorded; the set must not be released.
        var loader = new CountingLoader();
        var datasets = new DatasetsViewModel(loader);
        using var coordinator = new Services.LazyLoading.ExchangeSetLazyLoadCoordinator(
            new SilentNotifier(), (_, _) => Task.CompletedTask, _ => { },
            new Services.LazyLoading.LazyLoadOptions { CellThreshold = 1 });
        using var service = new ExchangeSetService(datasets, Notifications.TestNotifications.Create(), coordinator);

        await service.OpenAsync(Fixture("Synthetic-S57-Framed"));
        var subset = await service.OpenSubsetAsync(
            new ExchangeSetSubsetRequest(Fixture("Synthetic-S101Updates"), "CATALOG.XML",
                [new ExchangeSetSubsetItem("S-101/SYNTH101.000", [], "S-101", "SYNTH101")]),
            defer: true);

        Assert.Equal(3, datasets.Entries.Count);
        Assert.Equal(2, datasets.ExchangeSetHeaders.Count);
        Assert.True(subset[0].IsDeferred);
        Assert.Equal(0, loader.Loads);
    }

    private sealed class SilentNotifier : IMapViewportNotifier
    {
        public MapViewportSnapshot? Current => null;
        public event EventHandler<MapViewportSnapshot>? ViewportChanged { add { } remove { } }
    }

    private sealed class CountingLoader : IDatasetLoaderService
    {
        public int Loads { get; private set; }
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default)
        {
            Loads++;
            return Task.CompletedTask;
        }
        public Task ReRenderAtTimeAsync(DateTime t, CancellationToken ct) => Task.CompletedTask;
        public Task ReRenderAllAsync() => Task.CompletedTask;
        public void RemoveEntry(DatasetEntry entry) { }
        public void SetEntryOrder(IReadOnlyList<DatasetEntry> ordered) { }
        public IReadOnlyList<ILayer> CurrentStackedLayers => [];
        public IReadOnlyList<LayerStackEntry> CurrentStackEntries => [];
        public event Action? LayerStackChanged { add { } remove { } }
        public bool GetActive(string datasetId) => true;
        public void SetActive(string datasetId, bool active) { }
        public event Action<string>? ActiveChanged { add { } remove { } }
    }
}
