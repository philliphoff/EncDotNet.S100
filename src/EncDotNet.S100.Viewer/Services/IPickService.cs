using EncDotNet.S100.Viewer.ViewModels;
using Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Resolves map picks (single-tap, modifier-click, long-press) and publishes
/// them as <see cref="PickResult"/>s, which Object Information shows. Lifts the find-owning-entry +
/// <see cref="EncDotNet.S100.Datasets.Pipelines.IDatasetProcessor.GetFeatureInfo"/>
/// logic out of <see cref="MainWindow"/>; the window keeps the gesture
/// detection and routes the clicked world position here.
/// </summary>
internal interface IPickService
{
    /// <summary>
    /// Raised with every pick result: a map pick, a geographic pick, a feature
    /// opened by search or a followed reference. An empty result clears
    /// Object Information. Raised on the UI thread.
    /// </summary>
    event EventHandler<PickResult>? Picked;

    /// <summary>
    /// Picks the drawn features at <paramref name="worldPosition"/> (EPSG:3857;
    /// null for a tap off the map) through the map session's engine pick and
    /// publishes them to the pick report, with a coverage sample when no
    /// feature is hit. Must be called on the UI thread.
    /// </summary>
    /// <param name="worldPosition">The clicked point in EPSG:3857 metres, or null.</param>
    /// <param name="resolution">The map resolution, metres per DIP, which sizes the click tolerance.</param>
    /// <param name="dynamicHits">
    /// Dynamic-source hits collected in parallel by
    /// <see cref="DynamicSources.IDynamicSourcePickService"/>. When non-empty
    /// they are published into the pick report's dynamic section; an empty
    /// list is equivalent to none. Either list (or both) being non-empty
    /// opens the panel.
    /// </param>
    /// <returns>A task that completes once the pick report is updated.</returns>
    Task HandlePickAsync(MPoint? worldPosition, double resolution, IReadOnlyList<DynamicPickHit>? dynamicHits = null);

    /// <summary>
    /// Publishes a pick at a geographic location whose features have already
    /// been resolved elsewhere (the MCP <c>pick_features</c> path), updating
    /// the pick report exactly as a user click would so the Object
    /// Information panel and the pick highlight reflect it. Each entry is
    /// re-queried against its owning dataset's processor so the panel shows
    /// full FC-decoded attributes. Entries that no longer resolve are
    /// skipped; when none resolve the pick is cleared. Must be called on the
    /// UI thread.
    /// </summary>
    /// <param name="latitude">Pick latitude in WGS-84 decimal degrees.</param>
    /// <param name="longitude">Pick longitude in WGS-84 decimal degrees.</param>
    /// <param name="features">Resolved features under the pick, most-specific first.</param>
    void PresentGeographicPick(
        double latitude,
        double longitude,
        IReadOnlyList<GeographicPickFeature> features);

    /// <summary>
    /// As <see cref="PresentGeographicPick(double, double, IReadOnlyList{GeographicPickFeature})"/>,
    /// with the dynamic-source features at the point too (a predicted tide
    /// station, an AIS target), as a click shows them. Must be called on the
    /// UI thread.
    /// </summary>
    /// <param name="latitude">Pick latitude in WGS-84 decimal degrees.</param>
    /// <param name="longitude">Pick longitude in WGS-84 decimal degrees.</param>
    /// <param name="features">Resolved features under the pick, most-specific first.</param>
    /// <param name="dynamicHits">The dynamic-source features at the point.</param>
    void PresentGeographicPick(
        double latitude,
        double longitude,
        IReadOnlyList<GeographicPickFeature> features,
        IReadOnlyList<ViewModels.DynamicPickHit> dynamicHits) =>
        PresentGeographicPick(latitude, longitude, features);

    /// <summary>
    /// Resolves an xlink-style reference from the currently selected hit
    /// to its target feature within the same dataset and re-opens the
    /// Object Info panel on it. Returns <c>true</c> when navigation
    /// succeeded, <c>false</c> when the target could not be found, after
    /// showing a toast
    /// (<see cref="EncDotNet.S100.Viewer.Resources.Strings.Status_FeatureRefNotFound"/>)
    /// so the user knows the click registered.
    /// </summary>
    bool NavigateToReference(EncDotNet.S100.Datasets.Pipelines.FeatureReference reference);

    /// <summary>
    /// Opens the Object Information panel on a feature owned by the
    /// supplied processor. Used by feature search to re-display an
    /// arbitrary feature picked from any loaded dataset (the
    /// xlink-navigation contract is restricted to the currently selected
    /// hit's processor; this method generalises that for cross-dataset
    /// jumps). Returns <c>true</c> when the feature was resolved,
    /// <c>false</c> when the processor returned <c>null</c>.
    /// </summary>
    bool OpenFeature(
        EncDotNet.S100.Datasets.Pipelines.IDatasetProcessor processor,
        string featureRef,
        string datasetFileName);

    /// <summary>
    /// Opens the Object Information panel on a feature identified by
    /// its enumeration ordinal within the processor (see
    /// <see cref="EncDotNet.S100.Datasets.Pipelines.FeatureSummary.Ordinal"/>).
    /// Used by feature search so duplicate <c>gml:id</c>s — a real
    /// producer bug seen in S-122 datasets — still resolve to the
    /// correct feature instead of always returning the first match.
    /// </summary>
    bool OpenFeatureAt(
        EncDotNet.S100.Datasets.Pipelines.IDatasetProcessor processor,
        int ordinal,
        string datasetFileName);
}
