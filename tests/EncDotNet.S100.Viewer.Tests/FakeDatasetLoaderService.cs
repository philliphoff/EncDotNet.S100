using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Viewer.Services;
using EncDotNet.S100.Viewer.ViewModels;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Minimal <see cref="IDatasetLoaderService"/> stand-in that lets tests
/// drive <see cref="ViewerDatasetCatalog"/> by firing the
/// <see cref="IDatasetLoaderService.DatasetLoaded"/> and
/// <see cref="IDatasetLoaderService.DatasetRemoved"/> events directly.
/// </summary>
internal sealed class FakeDatasetLoaderService : IDatasetLoaderService
{
    public void RaiseLoaded(DatasetEntry entry) => DatasetLoaded?.Invoke(entry);
    public void RaiseRemoved(DatasetEntry entry) => DatasetRemoved?.Invoke(entry);

    public void Initialize(IMapHost host, ViewerCommandSettings? options) { }

    public bool SuppressAutoZoom { get; set; }
    public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ReRenderAtTimeAsync(DateTime t, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReRenderAllAsync() => Task.CompletedTask;
    public void RemoveEntry(DatasetEntry entry) => DatasetRemoved?.Invoke(entry);
    public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries) { }

    public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; } =
        new Dictionary<DatasetEntry, IDatasetProcessor>();

    public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } =
        new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();

    public event Action<DatasetEntry>? DatasetLoaded;
    public event Action<string?>? StatusChanged;
    public event Action<DatasetEntry>? DatasetRemoved;
    public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
    public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
    public event Action? LayerStackChanged { add { } remove { } }
    public bool GetActive(string datasetId) => true;
    public void SetActive(string datasetId, bool active) { }
    public event Action<string>? ActiveChanged { add { } remove { } }
}
