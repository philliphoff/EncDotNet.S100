using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.Resources;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Which maps of a split draw a picked feature (split map design note D10,
/// D11, architecture §2.4): a map draws it when its own dataset session holds
/// the feature's dataset and the dataset is active and visible there (split
/// map step 2 §2.6). A map that doesn't, while another map does, shows the
/// pick as a ghost with the reason.
/// </summary>
/// <remarks>
/// Step 2 (a dataset subset per pane) makes the sessions differ; step 3 (a
/// time per pane) adds "not at this map's time" as a reason.
/// </remarks>
internal sealed class PickPresence : IDisposable
{
    private readonly MapViews _views;
    private readonly HashSet<IMapViewCapabilities> _watched = [];

    /// <param name="views">Every map.</param>
    public PickPresence(MapViews views)
    {
        ArgumentNullException.ThrowIfNull(views);
        _views = views;
        _views.ViewAdded += OnViewAdded;
        _views.ViewRemoved += OnViewRemoved;
        foreach (var view in _views.All)
            Watch(view);
    }

    /// <summary>
    /// Raised when a map's session changes what it draws, so
    /// <see cref="Draws"/> and <see cref="GhostReason"/> may answer
    /// differently. It can be raised off the UI thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>Stops watching the maps' sessions.</summary>
    public void Dispose()
    {
        _views.ViewAdded -= OnViewAdded;
        _views.ViewRemoved -= OnViewRemoved;
        foreach (var view in _watched)
            view.DatasetSession.LayersChanged -= OnLayersChanged;
        _watched.Clear();
    }

    private void OnViewAdded(object? sender, IMapViewCapabilities view) => Watch(view);

    private void OnViewRemoved(object? sender, IMapViewCapabilities view)
    {
        if (_watched.Remove(view))
            view.DatasetSession.LayersChanged -= OnLayersChanged;
        // The ghost compares against the other maps, so it can change too.
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Watch(IMapViewCapabilities view)
    {
        if (_watched.Add(view))
            view.DatasetSession.LayersChanged += OnLayersChanged;
    }

    private void OnLayersChanged(object? sender, EventArgs e) => Changed?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Whether <paramref name="view"/> draws <paramref name="hit"/>'s feature:
    /// its session holds the dataset, active and visible. A hit with no
    /// dataset (an overlay source such as own ship) is drawn by every map.
    /// </summary>
    /// <param name="view">A map.</param>
    /// <param name="hit">The picked feature.</param>
    /// <returns>True when the map draws it.</returns>
    public static bool Draws(IMapViewCapabilities view, PickHit hit)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(hit);
        return hit.DatasetId is not { Length: > 0 } id
            || view.DatasetSession.GetDataset(new MapDatasetId(id)) is { Dataset: { IsActive: true, IsVisible: true } };
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
