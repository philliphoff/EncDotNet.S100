using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Renderers.Mapsui.DynamicSources;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// An explicitly owned S-100 subsystem attached to a <see cref="Mapsui.Map"/>.
/// Composes and owns the reusable dataset-layer session, processor ownership,
/// renderer, layer bands, and navigation convenience; disposing it releases all
/// of them. Obtain one with <see cref="S100MapExtensions.AddS100"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the library's public entry point and the middle of three tiers: it
/// composes the inner <see cref="MapsuiDatasetLayerSession"/> (dataset-layer
/// lifecycle only) and is in turn composed by an application's own host (e.g.
/// the Viewer's <c>MapsuiMapHost</c>). Start here.
/// </para>
/// <para>
/// Normal pan, zoom, and rotation stay with <c>Map.Navigator</c>; this session
/// adds only S-100-specific operations plus the optional
/// <see cref="ZoomToDataset"/> convenience.
/// </para>
/// <para>
/// The rendering operations mutate <c>Map.Layers</c> and install generated
/// layers on the calling synchronization context. Call and await them from the
/// map-owning thread (e.g. a UI host's dispatcher thread), matching
/// <see cref="MapsuiDatasetLayerSession"/>'s threading contract.
/// </para>
/// <para>
/// Datasets can be added from a caller-supplied processor via
/// <see cref="AddDatasetAsync"/>, or loaded from a single file/cell path via
/// <see cref="Datasets"/>. Exchange-set folder/archive loading is a later addition.
/// </para>
/// </remarks>
public interface IS100MapSession : IDisposable, IAsyncDisposable
{
    /// <summary>The underlying reusable dataset-layer session.</summary>
    MapsuiDatasetLayerSession Session { get; }

    /// <summary>The navigation surface bound to the same map.</summary>
    MapsuiMapNavigator Navigator { get; }

    /// <summary>
    /// The host-facing basemap, overlay, and tool layer bands. A host attaches
    /// its own decoration layers here (basemaps, pick highlights, extent
    /// indicators, measure tools) so they keep their z-order relative to the
    /// session's dataset layers. The dataset band is intentionally not exposed —
    /// the session owns and drives it via <see cref="AddDatasetAsync"/>,
    /// <see cref="RemoveDataset"/>, and <see cref="SetOrder"/>.
    /// </summary>
    IS100MapLayerHost Layers { get; }

    /// <summary>Loads datasets into this session from a file path.</summary>
    IS100DatasetLoader Datasets { get; }

    /// <summary>Geographic query surface (feature / coverage picking).</summary>
    IS100MapQuery Query { get; }

    /// <summary>
    /// The dynamic-source registry: registers push-driven
    /// <see cref="EncDotNet.S100.DynamicSources.IDynamicFeatureSource"/>
    /// instances (own-ship, AIS, …) as managed overlay layers over the same
    /// map, with per-source visibility and geographic hit-testing. The session
    /// owns the host and disposes it with the session.
    /// </summary>
    IS100DynamicSourceRegistry DynamicSources { get; }

    /// <summary>Raised after the final dataset-band projection changes.</summary>
    event EventHandler? LayersChanged;

    /// <summary>Raised after the aggregate registered time range changes.</summary>
    event EventHandler? TimeRangeChanged;

    /// <summary>Raised after the global map clock changes.</summary>
    event EventHandler<MapSessionCurrentTimeEventArgs>? CurrentTimeChanged;

    /// <summary>Raised once a dataset is about to render.</summary>
    event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderStarted;

    /// <summary>Raised after a successful dataset render installs its layers.</summary>
    event EventHandler<MapSessionDatasetRenderEventArgs>? DatasetRenderCompleted;

    /// <summary>Raised when a dataset render fails during a coalesced refresh.</summary>
    event EventHandler<MapSessionDatasetRenderFailedEventArgs>? DatasetRenderFailed;

    /// <summary>
    /// Registers a caller-supplied processor, records its dataset state, and
    /// renders its generated layers.
    /// </summary>
    /// <param name="dataset">The renderer-neutral dataset state.</param>
    /// <param name="processor">
    /// The processor whose portrayal is rendered. Ownership by outcome:
    /// <list type="bullet">
    /// <item><description>
    /// Returns <see langword="true"/>: the session owns the processor and
    /// disposes it when the dataset is removed or the session is disposed.
    /// </description></item>
    /// <item><description>
    /// Returns <see langword="false"/> (identity already in use): the processor
    /// was never registered; the caller still owns it and must dispose it.
    /// </description></item>
    /// <item><description>
    /// Throws (render failure or cancellation, or the dataset was removed before
    /// its layers were installed): the session rolls back, which disposes the
    /// processor; the caller must <em>not</em> dispose it again.
    /// </description></item>
    /// </list>
    /// </param>
    /// <param name="minimumDisplayScale">Optional coarsest catalogue scale.</param>
    /// <param name="maximumDisplayScale">Optional finest catalogue scale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A task that returns <see langword="true"/> when the dataset was
    /// registered, or <see langword="false"/> when its identity is already in
    /// use (in which case the caller still owns <paramref name="processor"/>).
    /// </returns>
    Task<bool> AddDatasetAsync(
        MapDataset dataset,
        IDatasetProcessor processor,
        int? minimumDisplayScale = null,
        int? maximumDisplayScale = null,
        CancellationToken cancellationToken = default);

    /// <summary>Removes a dataset's layers, state, and processor.</summary>
    /// <param name="datasetId">The dataset identity.</param>
    /// <returns><see langword="true"/> when a dataset was removed.</returns>
    bool RemoveDataset(MapDatasetId datasetId);

    /// <summary>Gets a snapshot of every registered dataset.</summary>
    IReadOnlyList<MapsuiMapDatasetSnapshot> GetDatasets();

    /// <summary>Gets one registered dataset's snapshot, or <see langword="null"/>.</summary>
    MapsuiMapDatasetSnapshot? GetDataset(MapDatasetId datasetId);

    /// <summary>Sets a dataset's visual enabled state.</summary>
    void SetVisible(MapDatasetId datasetId, bool isVisible);

    /// <summary>Sets a dataset's cross-product composition/query participation.</summary>
    void SetActive(MapDatasetId datasetId, bool isActive);

    /// <summary>Sets a dataset's opacity in the inclusive range 0..1.</summary>
    void SetOpacity(MapDatasetId datasetId, double opacity);

    /// <summary>Sets the bottom-to-top dataset paint order.</summary>
    void SetOrder(IReadOnlyList<MapDatasetId> bottomToTopDatasetIds);

    /// <summary>Applies immutable presentation state and re-renders datasets.</summary>
    Task SetPresentationAsync(
        MapPresentationState presentation,
        CancellationToken cancellationToken = default);

    /// <summary>Sets the global map clock without rendering.</summary>
    void SetTime(DateTime time);

    /// <summary>
    /// Sets the global map clock and applies time gating using the current
    /// presentation (the last one passed to <see cref="SetPresentationAsync"/>,
    /// or the default before any).
    /// </summary>
    Task SetTimeAsync(
        DateTime time,
        CancellationToken cancellationToken = default);

    /// <summary>Gets the aggregate time state for all registered datasets.</summary>
    MapsuiMapTimeSnapshot GetTimeSnapshot();

    /// <summary>
    /// Zooms the map to a registered dataset's extent. A convenience over
    /// <c>Map.Navigator</c>; a no-op when the dataset is unknown or has no
    /// extent yet.
    /// </summary>
    /// <param name="datasetId">The dataset identity.</param>
    void ZoomToDataset(MapDatasetId datasetId);
}
