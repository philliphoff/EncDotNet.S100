using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>One map of the window, as <c>list_maps</c> reports it (split map D28).</summary>
/// <param name="Id">The map's opaque, stable id, such as <c>map-2</c>.</param>
/// <param name="Label">Its label from the layout: "Map 1", "Map 2" in reading order.</param>
/// <param name="Name">The name the user gave it (D19), or <see langword="null"/>.</param>
/// <param name="Active">Whether it's the active map (D4).</param>
/// <param name="Folded">Whether its pane is folded to a strip (D17).</param>
/// <param name="Viewport">What it shows, or <see langword="null"/> before it's laid out.</param>
/// <param name="Datasets">What of the Datasets list it draws (split map step 2 §2.7).</param>
internal sealed record MapPaneState(
    string Id,
    string Label,
    string? Name,
    bool Active,
    bool Folded,
    MapViewportSnapshot? Viewport,
    MapDatasetsState Datasets);

/// <summary>What of the Datasets list a map draws (split map step 2 §2.7, D28).</summary>
/// <param name="Shown">How many datasets it draws.</param>
/// <param name="Total">How many datasets the Datasets list holds.</param>
/// <param name="Hidden">The ids of the datasets it doesn't draw, in Datasets-list order.</param>
internal sealed record MapDatasetsState(int Shown, int Total, IReadOnlyList<string> Hidden);

/// <summary>The window's maps (split map D28).</summary>
/// <param name="Split">Whether the map is split into more than one pane.</param>
/// <param name="Linked">Whether the panes' views are linked (D13).</param>
/// <param name="Active">The active map's id, or <see langword="null"/> before the first map opens.</param>
/// <param name="Maps">The maps, in reading order (left to right).</param>
internal sealed record MapSplitState(
    bool Split,
    bool Linked,
    string? Active,
    IReadOnlyList<MapPaneState> Maps);

/// <summary>Whether a map shows a picked feature, and why not (split map D10, D28).</summary>
/// <param name="Map">The map's id.</param>
/// <param name="Shown">Whether the map shows the feature at the pick now.</param>
/// <param name="Reason">
/// Why it doesn't: <c>dataset_hidden</c> (the map doesn't draw the feature's
/// dataset), <c>not_at_time</c> (the feature doesn't exist at the map's time;
/// step 3) or <c>out_of_view</c> (the point is outside the map's view, or the
/// pane is folded). <see langword="null"/> when shown.
/// </param>
internal sealed record MapShownIn(string Map, bool Shown, string? Reason);

/// <summary>
/// Drives the window's split for MCP (split map D28, #1735): lists the maps,
/// opens and closes the split, makes a map active, links the views and names
/// a pane, and says which maps draw a dataset (step 2 §2.7, #1773). The
/// async calls run on the UI thread through the invoker it was given; the
/// others are for callers already on it.
/// </summary>
internal sealed class MapSplitController
{
    /// <summary>The reason a map doesn't draw the feature's dataset.</summary>
    public const string DatasetHidden = "dataset_hidden";

    /// <summary>The reason the feature doesn't exist at the map's time (step 3).</summary>
    public const string NotAtTime = "not_at_time";

    /// <summary>The reason the pick is outside the map's view, or its pane is folded.</summary>
    public const string OutOfView = "out_of_view";

    private readonly MapSplitViewModel _split;
    private readonly MapViews _views;
    private readonly DatasetsViewModel _datasets;
    private readonly Func<Action, Task> _invoke;

    /// <summary>Creates the controller.</summary>
    /// <param name="split">The window's split.</param>
    /// <param name="views">The window's maps.</param>
    /// <param name="datasets">The Datasets list, whose rows say which maps draw them.</param>
    /// <param name="invoke">Runs an action on the UI thread.</param>
    public MapSplitController(MapSplitViewModel split, MapViews views, DatasetsViewModel datasets, Func<Action, Task> invoke)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(invoke);
        _split = split;
        _views = views;
        _datasets = datasets;
        _invoke = invoke;
    }

    /// <summary>Reads the maps.</summary>
    /// <returns>The window's maps.</returns>
    public Task<MapSplitState> GetStateAsync() => RunAsync(State);

    /// <summary>
    /// Opens the split (a new pane at the active map's view, which becomes
    /// active; D2) or closes it (keeping the active map; D3). Opening an
    /// already split map, or closing one that isn't, changes nothing.
    /// </summary>
    /// <param name="open">True to open the split, false to close it.</param>
    /// <returns>The window's maps afterwards.</returns>
    public Task<MapSplitState> SetSplitAsync(bool open) => RunAsync(() =>
    {
        if (open && !_split.IsSplit)
            _split.OpenSplit();
        else if (!open && _split.IsSplit)
            _split.CloseSplit();
        return State();
    });

    /// <summary>Makes a map active, as a click in it does (D4).</summary>
    /// <param name="map">The map's id.</param>
    /// <returns>The window's maps afterwards, or <see cref="MapNotFound"/>.</returns>
    public Task<ToolResult<MapSplitState>> SetActiveAsync(string map) => RunAsync(() =>
    {
        if (FindPane(map) is not { } pane)
            return ToolResult<MapSplitState>.Err(NotFound(map));
        _split.Activate(pane.Id);
        return ToolResult<MapSplitState>.Ok(State());
    });

    /// <summary>Links or unlinks the panes' views (D13, D14). Linking needs a split.</summary>
    /// <param name="linked">True to link.</param>
    /// <returns>The window's maps afterwards, or <see cref="InvalidArgument"/> when linking one map.</returns>
    public Task<ToolResult<MapSplitState>> SetLinkedAsync(bool linked) => RunAsync(() =>
    {
        if (linked && !_split.IsSplit)
        {
            return ToolResult<MapSplitState>.Err(new InvalidArgument(
                "linked", "there is one map, so nothing to link; open the split with set_split_map first"));
        }

        _split.IsLinked = linked;
        return ToolResult<MapSplitState>.Ok(State());
    });

    /// <summary>Names a pane (D19); a blank name goes back to its number.</summary>
    /// <param name="map">The map's id.</param>
    /// <param name="name">The name, or <see langword="null"/> or blank for none.</param>
    /// <returns>The window's maps afterwards, or <see cref="MapNotFound"/>.</returns>
    public Task<ToolResult<MapSplitState>> SetNameAsync(string map, string? name) => RunAsync(() =>
    {
        if (FindPane(map) is not { } pane)
            return ToolResult<MapSplitState>.Err(NotFound(map));
        _split.SetName(pane.Id, name);
        return ToolResult<MapSplitState>.Ok(State());
    });

    /// <summary>
    /// Which maps show a picked feature at a point (D10, D28): a map shows it
    /// when it draws the feature's dataset and the point is in its view.
    /// </summary>
    /// <param name="datasetId">The feature's dataset, or <see langword="null"/> for an overlay feature every map draws.</param>
    /// <param name="latitude">The pick's latitude.</param>
    /// <param name="longitude">The pick's longitude, in any world copy.</param>
    /// <returns>One entry per map, in reading order.</returns>
    public Task<IReadOnlyList<MapShownIn>> ShownInAsync(string? datasetId, double latitude, double longitude) =>
        RunAsync<IReadOnlyList<MapShownIn>>(() =>
        [
            .. _split.Panes.Select(pane =>
            {
                var reason = !Draws(pane.View, datasetId) ? DatasetHidden
                    : pane.IsFolded || !InView(pane.View.Viewport.Current, latitude, longitude) ? OutOfView
                    : null;
                return new MapShownIn(pane.Id.Value, reason is null, reason);
            }),
        ]);

    /// <summary>
    /// The ids of the maps that draw <paramref name="entry"/>, in reading
    /// order (split map step 2 §2.7): it's visible in the map and the display
    /// settings don't hide it. Call on the UI thread.
    /// </summary>
    /// <param name="entry">A row of the Datasets list.</param>
    /// <returns>The map ids; empty when no map draws it.</returns>
    public IReadOnlyList<string> ShownIn(DatasetEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return [.. _split.Panes.Where(pane => Draws(entry, pane.Id)).Select(pane => pane.Id.Value)];
    }

    /// <summary>
    /// The maps that draw each dataset, by the ids <c>list_datasets</c>
    /// reports (split map step 2 §2.7).
    /// </summary>
    /// <param name="datasetIds">Dataset ids.</param>
    /// <returns>
    /// One entry per id, in the same order: the ids of the maps that draw it,
    /// in reading order, or <see langword="null"/> for an id the Datasets
    /// list doesn't hold.
    /// </returns>
    public Task<IReadOnlyList<IReadOnlyList<string>?>> ShownInAsync(IReadOnlyList<string> datasetIds)
    {
        ArgumentNullException.ThrowIfNull(datasetIds);
        return RunAsync<IReadOnlyList<IReadOnlyList<string>?>>(() =>
        {
            var byId = new Dictionary<string, DatasetEntry>(StringComparer.Ordinal);
            foreach (var entry in _datasets.Entries)
                byId.TryAdd(entry.DisplayName, entry);
            return [.. datasetIds.Select(id => byId.TryGetValue(id, out var entry) ? ShownIn(entry) : null)];
        });
    }

    /// <summary>The map with id <paramref name="map"/>, or <see langword="null"/>. Call on the UI thread.</summary>
    /// <param name="map">A map id from <c>list_maps</c>.</param>
    /// <returns>The map's id, or <see langword="null"/> when there's none.</returns>
    public MapViewId? FindMap(string map) => FindPane(map)?.Id;

    /// <summary>The error for an unknown map id, listing the maps there are. Call on the UI thread.</summary>
    /// <param name="map">The unknown id.</param>
    /// <returns>The error.</returns>
    public MapNotFound NotFound(string map) => new(map, [.. _split.Panes.Select(p => p.Id.Value)]);

    /// <summary>Whether the map draws the dataset: visible there, and not hidden by the display settings.</summary>
    private static bool Draws(DatasetEntry entry, MapViewId map) => entry.IsVisibleIn(map) && !entry.IsHiddenByDisplay;

    /// <summary>
    /// Whether the map's session draws the dataset (D10): it holds it, active
    /// and visible there, as <see cref="PickPresence.Draws"/> asks.
    /// </summary>
    private static bool Draws(IMapViewCapabilities view, string? datasetId) =>
        datasetId is not { Length: > 0 } id
        || view.DatasetSession.GetDataset(new MapDatasetId(id)) is { Dataset: { IsActive: true, IsVisible: true } };

    private MapDatasetsState DatasetsOf(MapViewId map)
    {
        var hidden = new List<string>();
        var total = 0;
        foreach (var entry in _datasets.Entries)
        {
            total++;
            if (!Draws(entry, map))
                hidden.Add(entry.DisplayName);
        }

        return new MapDatasetsState(total - hidden.Count, total, hidden);
    }

    private static bool InView(MapViewportSnapshot? viewport, double latitude, double longitude)
    {
        if (viewport is null || latitude < viewport.MinLatitude || latitude > viewport.MaxLatitude)
            return false;

        // The view may be on a world copy (#773): try the point one world either side.
        for (var shift = -360.0; shift <= 360.0; shift += 360.0)
        {
            var lon = longitude + shift;
            if (lon >= viewport.MinLongitude && lon <= viewport.MaxLongitude)
                return true;
        }

        return false;
    }

    private MapPaneViewModel? FindPane(string map) =>
        _split.Panes.FirstOrDefault(p => string.Equals(p.Id.Value, map, StringComparison.Ordinal));

    private MapSplitState State() => new(
        _split.IsSplit,
        _split.IsLinked,
        _views.Active?.Id.Value,
        [
            .. _split.Panes.Select(p => new MapPaneState(
                p.Id.Value,
                p.Label,
                p.Name,
                p.IsActive,
                p.IsFolded,
                p.View.Viewport.Current,
                DatasetsOf(p.Id))),
        ]);

    private async Task<T> RunAsync<T>(Func<T> work)
    {
        T result = default!;
        await _invoke(() => result = work()).ConfigureAwait(false);
        return result;
    }
}

/// <summary>
/// The window's <see cref="MapSplitController"/> for the MCP tools, set once
/// the window builds its split and cleared when it closes.
/// </summary>
internal sealed class MapSplitControllerAccessor : ICapabilityAccessor<MapSplitController>
{
    /// <inheritdoc />
    public MapSplitController? Current { get; set; }
}
