using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The <see cref="IMapDatasetSession"/> over a map host's
/// <see cref="MapsuiDatasetLayerSession"/>.
/// </summary>
/// <param name="session">The session to drive.</param>
internal sealed class MapsuiMapDatasetSession(MapsuiDatasetLayerSession session) : IMapDatasetSession
{
    private readonly MapsuiDatasetLayerSession _session =
        session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public TimeProvider Clock
    {
        get => _session.Clock;
        set => _session.Clock = value;
    }

    /// <inheritdoc />
    public IS100MapQuery Query => _session.Query;

    /// <inheritdoc />
    public event EventHandler? LayersChanged
    {
        add => _session.LayersChanged += value;
        remove => _session.LayersChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler? TimeRangeChanged
    {
        add => _session.TimeRangeChanged += value;
        remove => _session.TimeRangeChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler<MapSessionCurrentTimeEventArgs>? CurrentTimeChanged
    {
        add => _session.CurrentTimeChanged += value;
        remove => _session.CurrentTimeChanged -= value;
    }

    /// <inheritdoc />
    public event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderStarted
    {
        add => _session.DatasetRenderStarted += value;
        remove => _session.DatasetRenderStarted -= value;
    }

    /// <inheritdoc />
    public event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderCompleted
    {
        add => _session.DatasetRenderCompleted += value;
        remove => _session.DatasetRenderCompleted -= value;
    }

    /// <inheritdoc />
    public event EventHandler<MapSessionDatasetRenderFailedEventArgs>? DatasetRenderFailed
    {
        add => _session.DatasetRenderFailed += value;
        remove => _session.DatasetRenderFailed -= value;
    }

    /// <inheritdoc />
    public MapsuiMapDatasetSnapshot? GetDataset(MapDatasetId datasetId) => _session.GetDataset(datasetId);

    /// <inheritdoc />
    public IReadOnlyList<MapsuiMapDatasetSnapshot> GetDatasets() => _session.GetDatasets();

    /// <inheritdoc />
    public IReadOnlyList<LayerStackEntry> GetLayerStackEntries() => _session.GetLayerStackEntries();

    /// <inheritdoc />
    public IReadOnlyList<ILayer> GetStackedLayers() => _session.GetStackedLayers();

    /// <inheritdoc />
    public MapsuiMapTimeSnapshot GetTimeSnapshot() => _session.GetTimeSnapshot();

    /// <inheritdoc />
    public void SetDataset(MapDataset dataset, int? minimumDisplayScale, int? maximumDisplayScale) =>
        _session.SetDataset(dataset, minimumDisplayScale, maximumDisplayScale);

    /// <inheritdoc />
    public Task<bool> PublishStoredAsync(MapDatasetId datasetId, MapsuiDatasetResult result, CancellationToken cancellationToken) =>
        _session.PublishStoredAsync(datasetId, result, cancellationToken);

    /// <inheritdoc />
    public Task<MapsuiDatasetResult?> RenderAsync(MapDatasetId datasetId, MapPresentationState presentation, CancellationToken cancellationToken) =>
        _session.RenderAsync(datasetId, presentation, cancellationToken);

    /// <inheritdoc />
    public void ClearLayers(MapDatasetId datasetId) => _session.ClearLayers(datasetId);

    /// <inheritdoc />
    public Task<bool> RefreshAsync(MapPresentationState presentation, MRect? viewport, CancellationToken cancellationToken) =>
        _session.RefreshAsync(presentation, viewport, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RefreshDepthAdjustedAsync(MapPresentationState presentation, MRect? viewport, CancellationToken cancellationToken) =>
        _session.RefreshDepthAdjustedAsync(presentation, viewport, cancellationToken);

    /// <inheritdoc />
    public Task<bool> RefreshRevealedAsync(MRect viewport, CancellationToken cancellationToken) =>
        _session.RefreshRevealedAsync(viewport, cancellationToken);

    /// <inheritdoc />
    public Task RefreshTimeAsync(MapPresentationState presentation, CancellationToken cancellationToken) =>
        _session.RefreshTimeAsync(presentation, cancellationToken);

    /// <inheritdoc />
    public void SetCurrentTime(DateTime time) => _session.SetCurrentTime(time);

    /// <inheritdoc />
    public void SetMarinerSettings(MarinerSettings mariner) => _session.SetMarinerSettings(mariner);

    /// <inheritdoc />
    public void SetOrder(IReadOnlyList<MapDatasetId> datasetIds) => _session.SetOrder(datasetIds);

    /// <inheritdoc />
    public int RemoveDatasets(IReadOnlyCollection<MapDatasetId> datasetIds, bool preserveState, bool removeProcessor) =>
        _session.RemoveDatasets(datasetIds, preserveState, removeProcessor);
}
