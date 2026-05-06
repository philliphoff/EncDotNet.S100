using Mapsui;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Dispatches Mapsui pick hits (single-tap, modifier-click, long-press)
/// to the pick report view-model. Lifts the find-owning-entry +
/// <see cref="EncDotNet.S100.Datasets.Pipelines.IDatasetProcessor.GetFeatureInfo"/>
/// logic out of <see cref="MainWindow"/>; the window keeps the gesture
/// detection and routes the resolved <see cref="MapInfo"/> here.
/// </summary>
internal interface IPickService
{
    /// <summary>
    /// Resolves the supplied hit (or null for an empty-map tap) into a
    /// pick-report update and a status-text message. Safe to call with
    /// null or with hits that don't carry a feature reference.
    /// </summary>
    void HandlePick(MapInfo? mapInfo);

    /// <summary>
    /// Resolves an xlink-style reference from the currently selected hit
    /// to its target feature within the same dataset and re-opens the
    /// Object Info panel on it. Returns <c>true</c> when navigation
    /// succeeded, <c>false</c> when the target could not be found (the
    /// caller is responsible for surfacing
    /// <see cref="EncDotNet.S100.Viewer.Resources.Strings.Status_FeatureRefNotFound"/>
    /// or equivalent).
    /// </summary>
    bool NavigateToReference(EncDotNet.S100.Datasets.Pipelines.FeatureReference reference);
}
