using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Resolves a viewer MCP tool's optional <c>map</c> argument to one of the
/// window's maps (split map D28, #1735): no map is the active map, and the
/// call never changes which map is active (D4).
/// </summary>
internal static class McpMapTargets
{
    /// <summary>Resolves <paramref name="map"/> among <paramref name="views"/>.</summary>
    /// <param name="views">The window's maps.</param>
    /// <param name="map">A map id, such as <c>map-2</c>, or <see langword="null"/> (or blank) for the active map.</param>
    /// <param name="view">The map.</param>
    /// <returns>
    /// <see cref="MapNotFound"/> when no map has the id, <see cref="MapNotReady"/>
    /// when there's no map yet; otherwise <see langword="null"/>.
    /// </returns>
    public static ToolError? Resolve(MapViews views, string? map, out IMapViewCapabilities? view)
    {
        ArgumentNullException.ThrowIfNull(views);
        if (string.IsNullOrWhiteSpace(map))
        {
            view = views.Active;
            return view is null ? new MapNotReady("the viewer's map control has not been initialised yet") : null;
        }

        view = views.Find(map);
        return view is null ? new MapNotFound(map, views.MapIds()) : null;
    }

    /// <summary>
    /// The render-activity monitor a diagnostics tool reads: every map's
    /// (<paramref name="all"/>) when <paramref name="map"/> is omitted, else
    /// that map's own (#1735).
    /// </summary>
    /// <param name="views">The window's maps.</param>
    /// <param name="all">The window's monitor, which hears every map.</param>
    /// <param name="map">A map id, or <see langword="null"/> (or blank) for every map.</param>
    /// <param name="monitor">The monitor.</param>
    /// <returns><see cref="MapNotFound"/> or <see cref="MapNotReady"/>; otherwise <see langword="null"/>.</returns>
    public static ToolError? ResolveRenderActivity(
        MapViews views,
        IRenderActivityMonitor all,
        string? map,
        out IRenderActivityMonitor monitor)
    {
        ArgumentNullException.ThrowIfNull(views);
        ArgumentNullException.ThrowIfNull(all);
        monitor = all;
        if (string.IsNullOrWhiteSpace(map))
            return null;
        if (Resolve(views, map, out var view) is { } error)
            return error;
        if (view is not IMapViewDiagnostics { RenderActivity: { } own })
            return new MapNotReady($"map '{map}' isn't attached yet");
        monitor = own;
        return null;
    }
}
