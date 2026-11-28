using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Resolves the optional <c>map</c> argument of a viewer tool that reads a
/// service following the active map, such as <c>get_legend</c> and
/// <c>get_depth_basis</c> (split map step 2 §2.7).
/// </summary>
internal static class ViewerMapTargets
{
    /// <summary>Resolves <paramref name="map"/> against the window's maps.</summary>
    /// <param name="views">The window's maps.</param>
    /// <param name="map">A map id from <c>list_maps</c>, or <see langword="null"/> (or blank) for the active map.</param>
    /// <param name="other">
    /// The named map when it isn't the active one, so the tool computes for
    /// it; <see langword="null"/> when the tool reads the active map's state.
    /// </param>
    /// <returns><see cref="MapNotFound"/> when no map has the id; otherwise <see langword="null"/>.</returns>
    public static ToolError? TryResolve(MapViews views, string? map, out IMapViewCapabilities? other)
    {
        ArgumentNullException.ThrowIfNull(views);
        other = null;
        if (string.IsNullOrWhiteSpace(map))
            return null;
        var id = map.Trim();
        if (views.Find(id) is not { } view)
            return new MapNotFound(id, views.MapIds());
        if (!ReferenceEquals(view, views.Active))
            other = view;
        return null;
    }
}
