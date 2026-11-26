using Avalonia;
using EncDotNet.S100.DataModel;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// One map's input services for <see cref="IMapTool"/> implementations: its
/// control and the projection between its pointer positions and lat/lon.
/// Hides the host window/view-model from tools so they remain testable and
/// don't accumulate cross-cutting dependencies.
/// </summary>
/// <remarks>
/// A tool's state is geographic, so it is shared by every map of a split;
/// each map draws it with its own overlay (<see cref="Services.ToolOverlayController"/>).
/// The context only says which map the input comes from, and moves to the
/// map under the pointer (split map design note D12).
/// </remarks>
internal sealed class MapToolContext
{
    private readonly Action<string?> _setStatusSummary;
    private readonly Func<Point, GeoPosition?> _screenToLatLon;
    private readonly Func<GeoPosition, Point?> _latLonToScreen;

    public MapToolContext(
        MapControl mapControl,
        Action<string?> setStatusSummary,
        Func<Point, GeoPosition?> screenToLatLon,
        Func<GeoPosition, Point?> latLonToScreen)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        ArgumentNullException.ThrowIfNull(setStatusSummary);
        ArgumentNullException.ThrowIfNull(screenToLatLon);
        ArgumentNullException.ThrowIfNull(latLonToScreen);

        MapControl = mapControl;
        _setStatusSummary = setStatusSummary;
        _screenToLatLon = screenToLatLon;
        _latLonToScreen = latLonToScreen;
    }

    /// <summary>
    /// The Mapsui control the tool is operating on. Tools should avoid
    /// calling pan/zoom on it directly — those gestures still belong to
    /// Mapsui — but may read viewport state for hit testing.
    /// </summary>
    public MapControl MapControl { get; }

    /// <summary>
    /// Sets a tool-owned summary string (e.g. measure-leg / total) on the
    /// status bar, or <c>null</c> to clear it.
    /// </summary>
    public void SetStatusSummary(string? text) => _setStatusSummary(text);

    /// <summary>
    /// Converts a pointer position (in <see cref="MapControl"/> client
    /// coordinates) to a WGS-84 lat/lon, or <c>null</c> when the pointer
    /// is outside the map or projects to an invalid location (e.g. above
    /// the Mercator pole limit).
    /// </summary>
    public GeoPosition? ScreenToLatLon(Point screen) => _screenToLatLon(screen);

    /// <summary>
    /// Converts a WGS-84 lat/lon to a pointer position in
    /// <see cref="MapControl"/> client coordinates, or <c>null</c> when no
    /// viewport is available. Used by editing tools for hit-testing screen
    /// gestures against world-space features (e.g. grabbing a waypoint).
    /// </summary>
    public Point? LatLonToScreen(GeoPosition world) => _latLonToScreen(world);
}
