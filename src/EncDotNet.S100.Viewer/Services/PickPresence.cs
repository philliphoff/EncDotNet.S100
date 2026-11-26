using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Which maps of a split draw a picked feature (split map design note D10,
/// D11, architecture §2.4): a map draws it when its own dataset session holds
/// the feature's dataset. A map that doesn't, while another map does, shows
/// the pick as a ghost with the reason.
/// </summary>
/// <remarks>
/// In step 1 every map holds the same datasets, so no map shows a ghost.
/// Step 2 (a dataset subset per pane) and step 3 (a time per pane) make the
/// sessions differ; step 3 adds "not at this map's time" as a reason.
/// </remarks>
internal sealed class PickPresence
{
    private readonly MapViews _views;

    /// <param name="views">Every map.</param>
    public PickPresence(MapViews views)
    {
        ArgumentNullException.ThrowIfNull(views);
        _views = views;
    }

    /// <summary>
    /// Whether <paramref name="view"/> draws <paramref name="hit"/>'s feature.
    /// A hit with no dataset (an overlay source such as own ship) is drawn
    /// by every map.
    /// </summary>
    /// <param name="view">A map.</param>
    /// <param name="hit">The picked feature.</param>
    /// <returns>True when the map draws it.</returns>
    public static bool Draws(IMapViewCapabilities view, PickHit hit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(hit);
        return hit.DatasetId is not { Length: > 0 } id
            || view.DatasetSession.GetDataset(new MapDatasetId(id)) is not null;
    }

    /// <summary>
    /// The reason <paramref name="view"/> shows <paramref name="hit"/> as a
    /// ghost, or <see langword="null"/> when it shows the pick as usual:
    /// it draws the feature, or no map does (so there is nothing to compare).
    /// </summary>
    /// <param name="view">A map.</param>
    /// <param name="hit">The picked feature.</param>
    /// <returns>The ghost's tag text, or <see langword="null"/>.</returns>
    public string? GhostReason(IMapViewCapabilities view, PickHit hit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(hit);
        if (Draws(view, hit) || !_views.All.Any(other => !ReferenceEquals(other, view) && Draws(other, hit)))
            return null;
        var what = hit.ProductSpec is { Length: > 0 } spec ? spec : hit.DatasetFileName ?? hit.DatasetId;
        return string.Format(CultureInfo.CurrentCulture, Strings.SplitMap_GhostHiddenFormat, what);
    }
}
