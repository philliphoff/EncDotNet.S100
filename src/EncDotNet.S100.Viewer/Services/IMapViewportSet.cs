namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// The viewports of every map in the window (split map, #1730). The
/// consumers that serve all maps at once read this rather than the active
/// map's <see cref="IMapViewportNotifier"/>, so a map the user isn't working
/// in still gets its cells loaded and its sources fed.
/// </summary>
/// <remarks>
/// The set holds each map's own viewport, not one box around them all: two
/// maps can show places far apart at different scales, and a single box
/// would take in everything between them.
/// </remarks>
internal interface IMapViewportSet
{
    /// <summary>
    /// The viewport of each map that has one, in the order the maps were
    /// added. Empty before any map has been laid out.
    /// </summary>
    IReadOnlyList<MapViewViewport> Current { get; }

    /// <summary>
    /// Raised with the new <see cref="Current"/> after any map's viewport
    /// changes or a map with a viewport is added or removed. Raised on the
    /// thread that changed it (usually the UI thread).
    /// </summary>
    event EventHandler<IReadOnlyList<MapViewViewport>>? ViewportsChanged;
}

/// <summary>One map's viewport in an <see cref="IMapViewportSet"/>.</summary>
/// <param name="View">The map.</param>
/// <param name="Snapshot">Its viewport.</param>
internal readonly record struct MapViewViewport(MapViewId View, MapViewportSnapshot Snapshot);
