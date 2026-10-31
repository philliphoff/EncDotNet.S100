using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines.Vector;
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

    public void Initialize(IMapLayerCollection layers, IMapViewportController viewport, ViewerCommandSettings? options) { }

    /// <summary>Backs <see cref="IsInitialized"/>; settable by tests.</summary>
    public bool IsInitializedValue { get; set; } = true;
    public bool IsInitialized => IsInitializedValue;

    /// <summary>Optional override so tests can make <see cref="LoadAsync"/> throw or stall.</summary>
    public Func<DatasetEntry, CancellationToken, Task>? LoadHook { get; set; }

    /// <summary>Entries passed to <see cref="RemoveEntry"/>, in call order.</summary>
    public List<DatasetEntry> RemovedEntries { get; } = new();

    public bool SuppressAutoZoom { get; set; }
    public Task LoadAsync(DatasetEntry entry, CancellationToken cancellationToken = default)
        => LoadHook?.Invoke(entry, cancellationToken) ?? Task.CompletedTask;
    public Task ReRenderAtTimeAsync(DateTime t, CancellationToken cancellationToken) => Task.CompletedTask;
    public Task ReRenderAllAsync() => Task.CompletedTask;
    public void RemoveEntry(DatasetEntry entry)
    {
        RemovedEntries.Add(entry);
        DatasetRemoved?.Invoke(entry);
    }
    public void SetEntryOrder(IReadOnlyList<DatasetEntry> orderedEntries) { }

    public IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> Processors { get; set; } =
        new Dictionary<DatasetEntry, IDatasetProcessor>();

    public IReadOnlyDictionary<DatasetEntry, IReadOnlyList<ILayer>> EntryLayers { get; } =
        new Dictionary<DatasetEntry, IReadOnlyList<ILayer>>();

    public event Action<DatasetEntry>? DatasetLoaded;
    public event Action<DatasetEntry>? DatasetRemoved;
    public IReadOnlyList<ILayer> CurrentStackedLayers => Array.Empty<ILayer>();
    public IReadOnlyList<LayerStackEntry> CurrentStackEntries => Array.Empty<LayerStackEntry>();
    public event Action? LayerStackChanged { add { } remove { } }
    /// <summary>Dataset ids whose Active flag is off; every other id is active.</summary>
    public HashSet<string> Inactive { get; } = new(StringComparer.Ordinal);

    public bool GetActive(string datasetId) => !Inactive.Contains(datasetId);

    public void SetActive(string datasetId, bool active)
    {
        if (active ? Inactive.Remove(datasetId) : Inactive.Add(datasetId))
            ActiveChanged?.Invoke(datasetId);
    }

    /// <summary>The policy last passed to <see cref="SetHiddenFeaturesOnLoad"/>.</summary>
    public Func<DatasetEntry, IDatasetProcessor, IReadOnlySet<string>?>? HiddenFeaturesOnLoad { get; private set; }

    public void SetHiddenFeaturesOnLoad(Func<DatasetEntry, IDatasetProcessor, IReadOnlySet<string>?>? policy) =>
        HiddenFeaturesOnLoad = policy;

    /// <summary>The policy last passed to <see cref="SetPortrayalTimeOnLoad"/>.</summary>
    public Func<DatasetEntry, IDatasetProcessor, PortrayalTime?>? PortrayalTimeOnLoad { get; private set; }

    public void SetPortrayalTimeOnLoad(Func<DatasetEntry, IDatasetProcessor, PortrayalTime?>? policy) =>
        PortrayalTimeOnLoad = policy;
    public event Action<string>? ActiveChanged;

    /// <summary>The policy last passed to <see cref="SetActiveOnLoad"/>.</summary>
    public Func<DatasetEntry, IDatasetProcessor, bool?>? ActiveOnLoad { get; private set; }

    public void SetActiveOnLoad(Func<DatasetEntry, IDatasetProcessor, bool?>? policy) =>
        ActiveOnLoad = policy;

    /// <summary>The policy last passed to <see cref="SetSubLayerVisibilityOnLoad"/>.</summary>
    public Func<DatasetEntry, string, bool?>? SubLayerVisibilityOnLoad { get; private set; }

    public void SetSubLayerVisibilityOnLoad(Func<DatasetEntry, string, bool?>? policy) =>
        SubLayerVisibilityOnLoad = policy;
}
