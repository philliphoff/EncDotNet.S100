using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Library;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

public sealed class LibraryLoadServiceTests : IDisposable
{
    private readonly LibraryTestContext _context = new();

    public void Dispose() => _context.Dispose();

    private async Task<IReadOnlyList<CollectionItem>> IndexS57SetAsync()
    {
        var root = _context.CreateS57ExchangeSet();
        var index = await CollectionIndexer.CreateDefault()
            .IndexAsync(new ExchangeSetSource(Guid.NewGuid(), null, root));
        return index.Items;
    }

    private static (DatasetsViewModel Datasets, ExchangeSetService Service, LibraryLoadService Loader) CreateSystem()
    {
        var datasets = new DatasetsViewModel(new NoopDatasetLoader());
        var service = new ExchangeSetService(datasets, Notifications.TestNotifications.Create());
        return (datasets, service, new LibraryLoadService(service, datasets));
    }

    [Fact]
    public async Task Loads_local_items_as_one_exchange_set_and_tracks_their_state()
    {
        var items = await IndexS57SetAsync();
        var (datasets, service, loader) = CreateSystem();
        using var _ = service;
        using var __ = loader;
        var changes = 0;
        loader.Changed += (_, _) => changes++;

        var result = await loader.LoadAsync(items, defer: false);

        Assert.Equal(new LibraryLoadResult(2, 0), result);
        Assert.Equal(2, datasets.Entries.Count);
        Assert.Single(datasets.ExchangeSetHeaders);
        Assert.True(changes > 0);

        Assert.Equal(LibraryLoadState.None, loader.StateOf(items[0]));
        datasets.Entries[0].IsLoaded = true;
        Assert.Equal(LibraryLoadState.Loaded, loader.StateOf(items[0]));
    }

    [Fact]
    public async Task Skips_online_catalogue_only_and_missing_items()
    {
        var items = await IndexS57SetAsync();
        var (datasets, service, loader) = CreateSystem();
        using var _ = service;
        using var __ = loader;
        CollectionItem Clone(CollectionItem item, ItemLocation location) => item with { Key = item.Key + "x", Location = location };

        var result = await loader.LoadAsync(
        [
            items[0],
            Clone(items[1], new RemoteItemLocation(new Uri("https://example.test/cell.zip"))),
            Clone(items[1], NoItemLocation.Instance),
            Clone(items[1], ((LocalItemLocation)items[1].Location) with { RelativePath = "GONE/GONE.000" }),
        ], defer: false);

        Assert.Equal(new LibraryLoadResult(1, 3), result);
        Assert.Single(datasets.Entries);
    }

    [Fact]
    public async Task Removing_an_entry_forgets_its_item()
    {
        var items = await IndexS57SetAsync();
        var (datasets, service, loader) = CreateSystem();
        using var _ = service;
        using var __ = loader;
        await loader.LoadAsync(items, defer: false);
        datasets.Entries[0].IsLoaded = true;

        datasets.Entries.RemoveAt(0);

        Assert.Equal(LibraryLoadState.None, loader.StateOf(items[0]));
    }

    private sealed class NoopDatasetLoader : IDatasetLoaderService
    {
        public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } = new Dictionary<DatasetEntry, IDatasetProcessor>();
        public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } = new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();
        public event Action<DatasetEntry>? DatasetLoaded { add { } remove { } }
        public event Action<DatasetEntry>? DatasetRemoved { add { } remove { } }
        public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }
        public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
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
