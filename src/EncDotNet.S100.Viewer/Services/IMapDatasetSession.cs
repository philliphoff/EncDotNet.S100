using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Renderers.Mapsui;
using Mapsui;
using Mapsui.Layers;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// What the viewer drives on one map's dataset session: the part of
/// <see cref="MapsuiDatasetLayerSession"/> that <see cref="DatasetLoaderService"/>
/// and <see cref="GlobalTimeService"/> use (split map, #1731).
/// </summary>
/// <remarks>
/// Each map view has its own session over the one shared
/// <see cref="DatasetProcessorOwner"/>. <see cref="MapSessionSet"/> fans the
/// loader's calls out to every view's session through this seam.
/// </remarks>
internal interface IMapDatasetSession
{
    /// <summary>The clock the session takes ages and live times at.</summary>
    TimeProvider Clock { get; set; }

    /// <summary>Picks features on this session's map.</summary>
    IS100MapQuery Query { get; }

    /// <summary>Raised after the session's dataset layers change.</summary>
    event EventHandler? LayersChanged;

    /// <summary>Raised when the session's aggregate time range changes.</summary>
    event EventHandler? TimeRangeChanged;

    /// <summary>Raised when the session's clock changes.</summary>
    event EventHandler<MapSessionCurrentTimeEventArgs>? CurrentTimeChanged;

    /// <summary>Raised when a dataset render starts.</summary>
    event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderStarted;

    /// <summary>Raised when a dataset render completes.</summary>
    event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderCompleted;

    /// <summary>Raised when a dataset render fails.</summary>
    event EventHandler<MapSessionDatasetRenderFailedEventArgs>? DatasetRenderFailed;

    /// <summary>Gets one dataset's state, or <see langword="null"/> when the session doesn't hold it.</summary>
    /// <param name="datasetId">The dataset.</param>
    /// <returns>The dataset's snapshot.</returns>
    MapsuiMapDatasetSnapshot? GetDataset(MapDatasetId datasetId);

    /// <summary>Gets every dataset's state, in paint order.</summary>
    /// <returns>The snapshots.</returns>
    IReadOnlyList<MapsuiMapDatasetSnapshot> GetDatasets();

    /// <summary>Gets the composed S-98 layer stack.</summary>
    /// <returns>The stack entries.</returns>
    IReadOnlyList<LayerStackEntry> GetLayerStackEntries();

    /// <summary>Gets the composed dataset layers.</summary>
    /// <returns>The layers.</returns>
    IReadOnlyList<ILayer> GetStackedLayers();

    /// <summary>Gets the session's aggregate time state.</summary>
    /// <returns>The time snapshot.</returns>
    MapsuiMapTimeSnapshot GetTimeSnapshot();

    /// <summary>Adds or updates a dataset's renderer-neutral state.</summary>
    /// <param name="dataset">The dataset state.</param>
    /// <param name="minimumDisplayScale">The catalogue's coarsest display scale.</param>
    /// <param name="maximumDisplayScale">The catalogue's compilation scale.</param>
    void SetDataset(MapDataset dataset, int? minimumDisplayScale, int? maximumDisplayScale);

    /// <summary>Puts a stored rendering on the map before a processor is registered.</summary>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="result">The stored rendering; its layers belong to this session from now on.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> once the rendering is on the map.</returns>
    Task<bool> PublishStoredAsync(MapDatasetId datasetId, MapsuiDatasetResult result, CancellationToken cancellationToken);

    /// <summary>Renders a registered dataset and replaces its layers.</summary>
    /// <param name="datasetId">The dataset.</param>
    /// <param name="presentation">The presentation to portray with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The installed result, or <see langword="null"/> when nothing was installed.</returns>
    Task<MapsuiDatasetResult?> RenderAsync(MapDatasetId datasetId, MapPresentationState presentation, CancellationToken cancellationToken);

    /// <summary>Takes a dataset's layers off the map, keeping its state.</summary>
    /// <param name="datasetId">The dataset.</param>
    void ClearLayers(MapDatasetId datasetId);

    /// <summary>Re-portrays the datasets for a new presentation.</summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="viewport">The map's viewport in EPSG:3857; <see langword="null"/> refreshes everything.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the refresh ran to completion.</returns>
    Task<bool> RefreshAsync(MapPresentationState presentation, MRect? viewport, CancellationToken cancellationToken);

    /// <summary>Re-portrays the depth-adjusted datasets.</summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="viewport">The map's viewport in EPSG:3857; <see langword="null"/> refreshes everything.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when the refresh ran to completion.</returns>
    Task<bool> RefreshDepthAdjustedAsync(MapPresentationState presentation, MRect? viewport, CancellationToken cancellationToken);

    /// <summary>Re-portrays deferred datasets that <paramref name="viewport"/> now reveals.</summary>
    /// <param name="viewport">The map's viewport in EPSG:3857.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><see langword="true"/> when anything was re-portrayed.</returns>
    Task<bool> RefreshRevealedAsync(MRect viewport, CancellationToken cancellationToken);

    /// <summary>Re-renders the time-dependent datasets at the session's clock.</summary>
    /// <param name="presentation">The presentation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes with the refresh.</returns>
    Task RefreshTimeAsync(MapPresentationState presentation, CancellationToken cancellationToken);

    /// <summary>Sets the session's clock.</summary>
    /// <param name="time">The time.</param>
    void SetCurrentTime(DateTime time);

    /// <summary>Sets the mariner settings the session portrays with.</summary>
    /// <param name="mariner">The settings.</param>
    void SetMarinerSettings(MarinerSettings mariner);

    /// <summary>Sets the datasets' paint order, bottom to top.</summary>
    /// <param name="datasetIds">The datasets.</param>
    void SetOrder(IReadOnlyList<MapDatasetId> datasetIds);

    /// <summary>Removes datasets from the session.</summary>
    /// <param name="datasetIds">The datasets; unknown ones are skipped.</param>
    /// <param name="preserveState">Whether to keep their state and place for a later reload.</param>
    /// <param name="removeProcessor">Whether to retire their processors from the shared owner.</param>
    /// <returns>The number of datasets removed.</returns>
    int RemoveDatasets(IReadOnlyCollection<MapDatasetId> datasetIds, bool preserveState, bool removeProcessor);
}
