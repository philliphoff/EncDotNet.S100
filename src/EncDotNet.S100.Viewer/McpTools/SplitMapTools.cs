using System.ComponentModel;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>A map's view, as <c>list_maps</c> reports it.</summary>
[Description("What a map shows: its WGS-84 bounds, centre and scale.")]
internal sealed record MapViewportDto(
    [property: Description("South edge, decimal degrees.")] double South,
    [property: Description("West edge, decimal degrees, in the continuous frame the map shows (past ±180 on a world copy).")] double West,
    [property: Description("North edge, decimal degrees.")] double North,
    [property: Description("East edge, decimal degrees, in the continuous frame the map shows.")] double East,
    [property: Description("Centre latitude, decimal degrees.")] double CenterLatitude,
    [property: Description("Centre longitude, decimal degrees, in the continuous frame the map shows.")] double CenterLongitude,
    [property: Description("The 1:N scale the status bar shows for this map, or null when the map isn't laid out.")] double? ScaleDenominator);

/// <summary>One map, as <c>list_maps</c> reports it.</summary>
[Description("One map of the window.")]
internal sealed record MapDto(
    [property: Description("The map's opaque, stable id (such as 'map-2'): pass it as the map argument of the map tools.")] string Id,
    [property: Description("Its label from the layout, 'Map 1', 'Map 2' in reading order (left to right). It changes when the panes are swapped; the id doesn't.")] string Label,
    [property: Description("The name the user (or set_map_name) gave the pane, shown in its header, or null.")] string? Name,
    [property: Description("Whether it's the active map: the one the map tools act on when given no map, and the one the status bar, Legend and keyboard follow.")] bool Active,
    [property: Description("Whether the panes' views are linked (set_map_link).")] bool Linked,
    [property: Description("Whether the pane is folded to a strip because the window is too narrow or the user folded it.")] bool Folded,
    [property: Description("What the map shows, or null before it's laid out.")] MapViewportDto? Viewport);

/// <summary>The window's maps (<c>list_maps</c> and the split tools' result).</summary>
[Description("The window's maps.")]
internal sealed record MapListDto(
    [property: Description("Whether the map is split into more than one pane.")] bool Split,
    [property: Description("Whether the panes' views are linked: moving one moves the others, each keeping its scale ratio.")] bool Linked,
    [property: Description("The active map's id.")] string? Active,
    [property: Description("The maps, in reading order (left to right).")] IReadOnlyList<MapDto> Maps);

/// <summary>
/// The split-map tools (dual-map design D28, #1735): <c>list_maps</c>,
/// <c>set_active_map</c>, <c>set_split_map</c>, <c>set_map_link</c> and
/// <c>set_map_name</c>, over the window's <see cref="MapSplitController"/>.
/// </summary>
internal sealed class SplitMapTools
{
    /// <summary><c>list_maps</c>.</summary>
    public const string ListMapsName = "list_maps";

    /// <summary><c>set_active_map</c>.</summary>
    public const string SetActiveMapName = "set_active_map";

    /// <summary><c>set_split_map</c>.</summary>
    public const string SetSplitMapName = "set_split_map";

    /// <summary><c>set_map_link</c>.</summary>
    public const string SetMapLinkName = "set_map_link";

    /// <summary><c>set_map_name</c>.</summary>
    public const string SetMapNameName = "set_map_name";

    /// <summary>The longest pane name, in characters.</summary>
    internal const int MaxNameLength = 40;

    private readonly ICapabilityAccessor<MapSplitController> _controller;

    /// <summary>Creates the tools.</summary>
    /// <param name="controller">The window's split, once it's built.</param>
    public SplitMapTools(ICapabilityAccessor<MapSplitController> controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        _controller = controller;
    }

    /// <summary>Lists the maps.</summary>
    /// <returns>The maps.</returns>
    public async Task<ToolResult<MapListDto>> ListMapsAsync()
    {
        if (_controller.Current is not { } controller)
            return NotReady();
        return ToolResult<MapListDto>.Ok(ToDto(await controller.GetStateAsync().ConfigureAwait(false)));
    }

    /// <summary>Makes a map active.</summary>
    /// <param name="map">The map's id.</param>
    /// <returns>The maps afterwards.</returns>
    public async Task<ToolResult<MapListDto>> SetActiveMapAsync(string? map)
    {
        if (string.IsNullOrWhiteSpace(map))
            return ToolResult<MapListDto>.Err(new InvalidArgument("map", "a map id from list_maps is required"));
        if (_controller.Current is not { } controller)
            return NotReady();
        return Map(await controller.SetActiveAsync(map).ConfigureAwait(false));
    }

    /// <summary>Opens or closes the split.</summary>
    /// <param name="open">True to open, false to close.</param>
    /// <returns>The maps afterwards.</returns>
    public async Task<ToolResult<MapListDto>> SetSplitMapAsync(bool open)
    {
        if (_controller.Current is not { } controller)
            return NotReady();
        return ToolResult<MapListDto>.Ok(ToDto(await controller.SetSplitAsync(open).ConfigureAwait(false)));
    }

    /// <summary>Links or unlinks the views.</summary>
    /// <param name="linked">True to link.</param>
    /// <returns>The maps afterwards.</returns>
    public async Task<ToolResult<MapListDto>> SetMapLinkAsync(bool linked)
    {
        if (_controller.Current is not { } controller)
            return NotReady();
        return Map(await controller.SetLinkedAsync(linked).ConfigureAwait(false));
    }

    /// <summary>Names a pane.</summary>
    /// <param name="map">The map's id.</param>
    /// <param name="name">The name; null or blank for none.</param>
    /// <returns>The maps afterwards.</returns>
    public async Task<ToolResult<MapListDto>> SetMapNameAsync(string? map, string? name)
    {
        if (string.IsNullOrWhiteSpace(map))
            return ToolResult<MapListDto>.Err(new InvalidArgument("map", "a map id from list_maps is required"));
        if (name is { } text && text.Trim().Length > MaxNameLength)
            return ToolResult<MapListDto>.Err(new InvalidArgument("name", $"at most {MaxNameLength} characters"));
        if (_controller.Current is not { } controller)
            return NotReady();
        return Map(await controller.SetNameAsync(map, name).ConfigureAwait(false));
    }

    internal static MapListDto ToDto(MapSplitState state) => new(
        state.Split,
        state.Linked,
        state.Active,
        [.. state.Maps.Select(m => new MapDto(m.Id, m.Label, m.Name, m.Active, state.Linked, m.Folded, ToDto(m.Viewport)))]);

    private static MapViewportDto? ToDto(MapViewportSnapshot? viewport)
    {
        if (viewport is null)
            return null;
        var latitude = (viewport.MinLatitude + viewport.MaxLatitude) / 2.0;
        return new MapViewportDto(
            viewport.MinLatitude,
            viewport.MinLongitude,
            viewport.MaxLatitude,
            viewport.MaxLongitude,
            latitude,
            (viewport.MinLongitude + viewport.MaxLongitude) / 2.0,
            viewport.MercatorResolution > 0
                ? MapScaleFormatter.ResolutionToScaleDenominator(viewport.MercatorResolution, latitude)
                : null);
    }

    private static ToolResult<MapListDto> Map(ToolResult<MapSplitState> result)
    {
        if (result.TryGetValue(out var state))
            return ToolResult<MapListDto>.Ok(ToDto(state!));
        result.TryGetError(out var error);
        return ToolResult<MapListDto>.Err(error!);
    }

    private static ToolResult<MapListDto> NotReady() =>
        ToolResult<MapListDto>.Err(new MapNotReady("the viewer's window has not built its map panes yet"));
}

/// <summary>Wraps <see cref="SplitMapTools"/> as MCP server tools.</summary>
internal static class SplitMapMcpAdapters
{
    private const string MapListNote =
        " Returns what list_maps returns: split, linked, active and each map's id, label, name, active, linked, folded and viewport.";

    /// <summary>Creates the five tools.</summary>
    /// <param name="inner">The tools.</param>
    /// <returns>The MCP tools.</returns>
    public static IEnumerable<McpServerTool> Create(SplitMapTools inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        yield return Tool(
            (CancellationToken ct = default) => McpAdapterShared.DispatchAsync(inner.ListMapsAsync),
            SplitMapTools.ListMapsName,
            "Lists the live viewer's maps. With the map split (set_split_map), each pane is a map with its own view; "
            + "in this release every map shows the same datasets at the same time. Each map has an opaque, stable 'id' "
            + "(such as 'map-2') to pass as the 'map' argument of the map tools (set_viewport, fling, pick_features, "
            + "render_to_image, identify_features, await_settled, await_render_idle, get_render_stats); its 'label' "
            + "('Map 1', 'Map 2' in reading order); its 'name' (set_map_name); whether it's 'active' (the map those "
            + "tools act on when given no map, which the status bar and Legend follow); 'linked'; 'folded' (its pane "
            + "folded to a strip); and its 'viewport' (south, west, north, east, centerLatitude, centerLongitude, "
            + "scaleDenominator). Also returns 'split', 'linked' and the 'active' id. Read-only; viewer-injected tool.");

        yield return Tool(
            ([Description("The map to make active: an id from list_maps, such as 'map-2'.")] string map,
             CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.SetActiveMapAsync(map)),
            SplitMapTools.SetActiveMapName,
            "Makes a map active, as a click in its pane does: the status bar, Legend, keyboard and the map tools called "
            + "without a map follow it. Activating a folded pane unfolds it. The map tools never change the active map "
            + "themselves. Fails with map_not_found for an unknown id." + MapListNote + " Mutating; viewer-injected tool.");

        yield return Tool(
            ([Description("True opens the split: a second pane at the active map's view, which becomes active. False closes every pane but the active one.")] bool open,
             CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.SetSplitMapAsync(open)),
            SplitMapTools.SetSplitMapName,
            "Splits the live viewer's map into two side-by-side panes, or closes the split, as the title bar's Split "
            + "Map button and View › Split Map do. Opening copies the active map's view to the new pane, which becomes "
            + "active; closing keeps the active map and its view, and turns the link off. Opening a split map, or closing "
            + "one that isn't, changes nothing. Each pane then has its own view; in this release every pane shows the "
            + "same datasets at the same time. A command-line (--mcp) session doesn't save the split." + MapListNote
            + " Mutating; viewer-injected tool.");

        yield return Tool(
            ([Description("True links the panes' views; false unlinks them.")] bool linked,
             CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.SetMapLinkAsync(linked)),
            SplitMapTools.SetMapLinkName,
            "Links or unlinks the split panes' views, as a pane header's chain does. Linking moves the other panes to "
            + "the active map's centre and keeps each pane's scale, so their scale ratio is fixed from then on; after "
            + "that, moving any pane (set_viewport, fling, the user) moves the others with it. Rotation isn't linked. "
            + "Closing the split unlinks, and the link isn't saved. Linking one map fails with invalid_argument."
            + MapListNote + " Mutating; viewer-injected tool.");

        yield return Tool(
            ([Description("The map to name: an id from list_maps.")] string map,
             [Description("The name its header shows, such as 'S-57' or 'Low water', up to 40 characters. Null or blank goes back to its number ('Map 2').")] string? name = null,
             CancellationToken ct = default) => McpAdapterShared.DispatchAsync(() => inner.SetMapNameAsync(map, name)),
            SplitMapTools.SetMapNameName,
            "Names a map pane, as renaming it in its header does. The header shows the name; chips and the status bar "
            + "keep the short label ('Map 2'). The name is saved with the split. Fails with map_not_found for an "
            + "unknown id." + MapListNote + " Mutating; viewer-injected tool.");
    }

    private static McpServerTool Tool(Delegate del, string name, string description) =>
        McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = name,
            Description = description,
            SerializerOptions = McpAdapterShared.Options,
        });
}
