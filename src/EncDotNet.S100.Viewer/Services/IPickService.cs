using System.Collections.Generic;
using EncDotNet.S100.Viewer.ViewModels;
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
    /// <remarks>
    /// Also accepts an optional list of dynamic-source hits collected
    /// in parallel by <see cref="DynamicSources.IDynamicSourcePickService"/>.
    /// When non-empty the dynamic hits are published into the pick
    /// report's dynamic section; an empty list is equivalent to no
    /// dynamic hits. Either list (or both) being non-empty causes the
    /// panel to open.
    /// </remarks>
    void HandlePick(MapInfo? mapInfo, IReadOnlyList<DynamicPickHit>? dynamicHits = null);

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
