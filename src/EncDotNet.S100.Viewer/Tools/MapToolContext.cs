using System;
using Avalonia;
using Mapsui.Layers;
using Mapsui.UI.Avalonia;

namespace EncDotNet.S100.Viewer.Tools;

/// <summary>
/// Services made available to <see cref="IMapTool"/> implementations while
/// they are active. Hides the host window/view-model from tools so they
/// remain testable and don't accumulate cross-cutting dependencies.
/// </summary>
internal sealed class MapToolContext
{
    private readonly Action<ILayer> _addLayer;
    private readonly Action<ILayer> _removeLayer;
    private readonly Action<string?> _setStatusSummary;
    private readonly Action _refreshGraphics;
    private readonly Func<Point, (double Lat, double Lon)?> _screenToLatLon;

    public MapToolContext(
        MapControl mapControl,
        Action<ILayer> addLayer,
        Action<ILayer> removeLayer,
        Action<string?> setStatusSummary,
        Action refreshGraphics,
        Func<Point, (double Lat, double Lon)?> screenToLatLon)
    {
        ArgumentNullException.ThrowIfNull(mapControl);
        ArgumentNullException.ThrowIfNull(addLayer);
        ArgumentNullException.ThrowIfNull(removeLayer);
        ArgumentNullException.ThrowIfNull(setStatusSummary);
        ArgumentNullException.ThrowIfNull(refreshGraphics);
        ArgumentNullException.ThrowIfNull(screenToLatLon);

        MapControl = mapControl;
        _addLayer = addLayer;
        _removeLayer = removeLayer;
        _setStatusSummary = setStatusSummary;
        _refreshGraphics = refreshGraphics;
        _screenToLatLon = screenToLatLon;
    }

    /// <summary>
    /// The Mapsui control the tool is operating on. Tools should avoid
    /// calling pan/zoom on it directly — those gestures still belong to
    /// Mapsui — but may read viewport state for their own rendering.
    /// </summary>
    public MapControl MapControl { get; }

    /// <summary>Adds an overlay layer to the map.</summary>
    public void AddLayer(ILayer layer) => _addLayer(layer);

    /// <summary>Removes an overlay layer from the map.</summary>
    public void RemoveLayer(ILayer layer) => _removeLayer(layer);

    /// <summary>
    /// Sets a tool-owned summary string (e.g. measure-leg / total) on the
    /// status bar, or <c>null</c> to clear it.
    /// </summary>
    public void SetStatusSummary(string? text) => _setStatusSummary(text);

    /// <summary>
    /// Asks the host to schedule a redraw of map graphics. Tools call this
    /// after mutating their overlay layer's feature list.
    /// </summary>
    public void RefreshGraphics() => _refreshGraphics();

    /// <summary>
    /// Converts a pointer position (in <see cref="MapControl"/> client
    /// coordinates) to a WGS-84 lat/lon, or <c>null</c> when the pointer
    /// is outside the map or projects to an invalid location (e.g. above
    /// the Mercator pole limit).
    /// </summary>
    public (double Lat, double Lon)? ScreenToLatLon(Point screen) => _screenToLatLon(screen);
}
