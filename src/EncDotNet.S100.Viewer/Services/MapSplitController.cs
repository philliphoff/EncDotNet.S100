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
internal sealed record MapPaneState(
    string Id,
    string Label,
    string? Name,
    bool Active,
    bool Folded,
    MapViewportSnapshot? Viewport);

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
/// a pane. Every call runs on the UI thread through the invoker it was given.
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
    private readonly Func<Action, Task> _invoke;

    /// <summary>Creates the controller.</summary>
    /// <param name="split">The window's split.</param>
    /// <param name="views">The window's maps.</param>
    /// <param name="invoke">Runs an action on the UI thread.</param>
    public MapSplitController(MapSplitViewModel split, MapViews views, Func<Action, Task> invoke)
    {
        ArgumentNullException.ThrowIfNull(split);
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(invoke);
        _split = split;
        _views = views;
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

    private static bool Draws(IMapViewCapabilities view, string? datasetId) =>
        datasetId is not { Length: > 0 } id || view.DatasetSession.GetDataset(new MapDatasetId(id)) is not null;

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

    private MapNotFound NotFound(string map) => new(map, [.. _split.Panes.Select(p => p.Id.Value)]);

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
                p.View.Viewport.Current)),
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
