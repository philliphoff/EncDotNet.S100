using EncDotNet.S100.DataModel;
using Mapsui;
using Mapsui.Projections;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Provides the focused S-100 map navigation conveniences used by interactive
/// Mapsui hosts.
/// </summary>
/// <remarks>
/// <para>
/// This adapter mutates the supplied map's <see cref="Map.Navigator"/> directly.
/// It does not own the map, dispatch to a UI thread, invalidate a control, or
/// automatically frame datasets after loading. UI-framework hosts remain
/// responsible for thread affinity and redraw behavior; automatic framing
/// remains application policy.
/// </para>
/// <para>
/// Normal pan, zoom, rotation, and gesture behavior remains available through
/// Mapsui's navigator. This type intentionally exposes only the conveniences
/// already needed by S-100 hosts rather than duplicating the full Mapsui API.
/// </para>
/// </remarks>
public sealed class MapsuiMapNavigator
{
    private readonly Navigator _navigator;

    /// <summary>
    /// Creates a navigation adapter for <paramref name="map"/>.
    /// </summary>
    /// <param name="map">The existing Mapsui map to navigate.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="map"/> is <see langword="null"/>.
    /// </exception>
    public MapsuiMapNavigator(Map map)
    {
        ArgumentNullException.ThrowIfNull(map);
        _navigator = map.Navigator;
    }

    /// <summary>
    /// Frames a rendered dataset extent with ten percent padding on every
    /// side.
    /// </summary>
    /// <param name="extent">The dataset extent in the map's coordinate system.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="extent"/> is <see langword="null"/>.
    /// </exception>
    /// <param name="durationMilliseconds">
    /// Animation duration in milliseconds. A non-positive value applies the
    /// viewport immediately.
    /// </param>
    public void ZoomToExtent(MRect extent, long durationMilliseconds = -1)
    {
        ArgumentNullException.ThrowIfNull(extent);
        var paddingX = extent.Width * 0.1;
        var paddingY = extent.Height * 0.1;
        _navigator.ZoomToBox(
            extent.Grow(paddingX, paddingY),
            duration: durationMilliseconds);
    }

    /// <summary>
    /// Sets an exact map-coordinate extent without animation.
    /// </summary>
    /// <param name="extent">The extent in the map's coordinate system.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="extent"/> is <see langword="null"/>.
    /// </exception>
    public void SetViewportToExtent(MRect extent)
    {
        ArgumentNullException.ThrowIfNull(extent);
        _navigator.ZoomToBox(extent, duration: 0);
    }

    /// <summary>
    /// Sets an exact map-coordinate center and resolution without animation.
    /// </summary>
    /// <param name="center">The center in the map's coordinate system.</param>
    /// <param name="resolution">The map resolution in map units per pixel.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="center"/> is <see langword="null"/>.
    /// </exception>
    public void SetViewportToCenterAndResolution(MPoint center, double resolution)
    {
        ArgumentNullException.ThrowIfNull(center);
        _navigator.CenterOnAndZoomTo(center, resolution, duration: 0);
    }

    /// <summary>
    /// Sets clockwise viewport rotation without animation.
    /// </summary>
    /// <param name="degrees">Clockwise rotation in degrees.</param>
    public void SetRotation(double degrees)
    {
        _navigator.RotateTo(degrees, duration: 0);
    }

    /// <summary>
    /// Centers the viewport on a WGS-84 position while preserving its current
    /// resolution.
    /// </summary>
    /// <param name="position">The WGS-84 position to center.</param>
    /// <param name="durationMilliseconds">Animation duration in milliseconds.</param>
    /// <remarks>
    /// Invalid or non-finite coordinates are ignored so a transient dynamic
    /// vessel update cannot corrupt the live viewport.
    /// </remarks>
    public void CenterOn(GeoPosition position, long durationMilliseconds = 300)
    {
        if (!IsValid(position))
        {
            return;
        }

        var (x, y) = SphericalMercator.FromLonLat(position.Longitude, position.Latitude);
        _navigator.CenterOn(x, y, durationMilliseconds);
    }

    /// <summary>
    /// Returns the laid-out viewport center in WGS-84 coordinates.
    /// </summary>
    /// <returns>
    /// The current center, or <see langword="null"/> when the viewport is not
    /// laid out or cannot be projected to a valid WGS-84 position.
    /// </returns>
    public GeoPosition? TryGetViewportCenterWgs84()
    {
        var viewport = _navigator.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return null;
        }

        var (longitude, latitude) = SphericalMercator.ToLonLat(
            viewport.CenterX,
            viewport.CenterY);
        var position = new GeoPosition(latitude, longitude);
        return IsValid(position) ? position : null;
    }

    private static bool IsValid(GeoPosition position) =>
        double.IsFinite(position.Latitude)
        && double.IsFinite(position.Longitude)
        && position.Latitude is >= -90.0 and <= 90.0;
}
