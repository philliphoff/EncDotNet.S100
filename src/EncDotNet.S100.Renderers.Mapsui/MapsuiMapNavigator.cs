using System.Diagnostics;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Rendering.Engine;
using EncDotNet.S100.Rendering.Engine.Prediction;
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
    // The latitude where the Web-Mercator world ends (EPSG:3857 square).
    private const double WorldMaxLatitude = 85.05112878;

    private readonly Navigator _navigator;
    private Navigator.ViewportChangedEventHandler? _observe;

    /// <summary>
    /// Creates a navigation adapter for <paramref name="map"/>.
    /// </summary>
    /// <param name="map">The existing Mapsui map to navigate.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="map"/> is <see langword="null"/>.
    /// </exception>
    public MapsuiMapNavigator(Map map)
        : this(map, planner: null)
    {
    }

    /// <summary>
    /// Creates a navigation adapter for <paramref name="map"/> that reports
    /// the map's views, and the targets of the animations and flings it
    /// starts, to <paramref name="planner"/> (map rendering slice&#160;9).
    /// </summary>
    /// <param name="map">The existing Mapsui map to navigate.</param>
    /// <param name="planner">The map's demand planner, or <see langword="null"/> to report nothing.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="map"/> is <see langword="null"/>.
    /// </exception>
    public MapsuiMapNavigator(Map map, DemandPlanner? planner)
    {
        ArgumentNullException.ThrowIfNull(map);
        _navigator = map.Navigator;
        Planner = planner;
        if (planner is not null)
        {
            // Mapsui raises viewport changes on the thread that changed the
            // view (the UI thread, or its animation callbacks); the planner
            // is thread-safe.
            _observe = (_, _) => ObserveViewport();
            _navigator.ViewportChanged += _observe;
            ObserveViewport();
        }
    }

    /// <summary>The demand planner the map's views are reported to, or <see langword="null"/>.</summary>
    public DemandPlanner? Planner { get; }

    /// <summary>
    /// Starts a fling as Mapsui's map control does on release
    /// (<c>Navigator.Fling</c>), and reports its predicted resting viewport
    /// to the <see cref="Planner"/>.
    /// </summary>
    /// <param name="velocityX">Release velocity, screen pixels per second.</param>
    /// <param name="velocityY">Release velocity, screen pixels per second.</param>
    /// <param name="maxDurationMilliseconds">The fling's longest duration.</param>
    /// <returns>The predicted resting viewport, or null when no fling starts or nothing is reported.</returns>
    public ViewState? Fling(double velocityX, double velocityY, long maxDurationMilliseconds = 1000)
    {
        var target = ForecastFling(velocityX, velocityY, maxDurationMilliseconds);
        _navigator.Fling(velocityX, velocityY, maxDurationMilliseconds);
        return target;
    }

    /// <summary>
    /// Reports to the <see cref="Planner"/> the fling Mapsui is about to start
    /// for this release velocity, without starting it: for a host whose map
    /// control starts the fling itself.
    /// </summary>
    /// <param name="velocityX">Release velocity, screen pixels per second.</param>
    /// <param name="velocityY">Release velocity, screen pixels per second.</param>
    /// <param name="maxDurationMilliseconds">The fling's longest duration.</param>
    /// <returns>The predicted resting viewport, or null when no fling would start or nothing is reported.</returns>
    public ViewState? ForecastFling(double velocityX, double velocityY, long maxDurationMilliseconds = 1000)
    {
        if (Planner is null || _navigator.PanLock
            || MapsuiFlingForecast.Travel(_navigator.Viewport, velocityX, velocityY, maxDurationMilliseconds)
                is not { } travel)
        {
            return null;
        }

        ObserveViewport();
        return Planner.BeginFling(travel.TravelX, travel.TravelY, travel.Duration, Stopwatch.GetTimestamp());
    }

    /// <summary>
    /// Zooms about the view centre to <paramref name="resolution"/>, through
    /// the navigator's limits, animating for <paramref name="durationMilliseconds"/>.
    /// </summary>
    /// <param name="resolution">The resolution, map units per pixel.</param>
    /// <param name="durationMilliseconds">Animation duration; zero or less applies it at once.</param>
    public void ZoomTo(double resolution, long durationMilliseconds)
    {
        var viewport = _navigator.Viewport;
        CenterOnAndZoomToLimited(new MPoint(viewport.CenterX, viewport.CenterY), resolution, durationMilliseconds);
    }

    /// <summary>
    /// Centres on <paramref name="center"/> at <paramref name="resolution"/>,
    /// through the navigator's limits, animating for <paramref name="durationMilliseconds"/>.
    /// </summary>
    /// <param name="center">The centre in the map's coordinate system.</param>
    /// <param name="resolution">The resolution, map units per pixel.</param>
    /// <param name="durationMilliseconds">Animation duration; zero or less applies it at once.</param>
    public void CenterOnAndZoomTo(MPoint center, double resolution, long durationMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(center);
        CenterOnAndZoomToLimited(center, resolution, durationMilliseconds);
    }

    /// <summary>Stops reporting to the planner.</summary>
    internal void Detach()
    {
        if (_observe is { } observe)
        {
            _navigator.ViewportChanged -= observe;
            _observe = null;
        }
    }

    private void ObserveViewport()
    {
        var viewport = _navigator.Viewport;
        if (Planner is not null && viewport.Width > 0 && viewport.Height > 0)
        {
            Planner.Observe(MapsuiChartLayers.ToViewState(viewport), Stopwatch.GetTimestamp());
        }
    }

    private void ReportAnimation(Viewport target, long duration)
    {
        if (Planner is not null && duration > 0 && target.Width > 0 && target.Height > 0)
        {
            ObserveViewport();
            Planner.BeginAnimation(
                MapsuiChartLayers.ToViewState(target), TimeSpan.FromMilliseconds(duration), Stopwatch.GetTimestamp());
        }
    }

    /// <summary>
    /// Frames a rendered dataset extent with ten percent padding on every
    /// side. The extent is first clipped to the Web-Mercator world
    /// (±85.05°), so a polar dataset reaching towards the pole (e.g. the NIC
    /// Arctic S-411 chart, to 89.3°N) is framed on the part that can be drawn
    /// rather than on empty space above the top of the world (issue #760).
    /// </summary>
    /// <param name="extent">The dataset extent in the map's coordinate system.</param>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="extent"/> is <see langword="null"/>.
    /// </exception>
    /// <param name="durationMilliseconds">
    /// Animation duration in milliseconds. A negative value uses Mapsui's
    /// default timing, zero applies the viewport immediately, and a positive
    /// value animates for the requested duration.
    /// </param>
    public void ZoomToExtent(MRect extent, long durationMilliseconds = -1)
    {
        ArgumentNullException.ThrowIfNull(extent);
        double worldMaxY = SphericalMercator.FromLonLat(0, WorldMaxLatitude).y;
        double minY = Math.Max(extent.MinY, -worldMaxY);
        double maxY = Math.Min(extent.MaxY, worldMaxY);
        if (maxY > minY)
            extent = new MRect(extent.MinX, minY, extent.MaxX, maxY);
        var paddingX = extent.Width * 0.1;
        var paddingY = extent.Height * 0.1;
        var paddedExtent = extent.Grow(paddingX, paddingY);
        ZoomToBoxLimited(paddedExtent, durationMilliseconds < 0 ? -1 : durationMilliseconds);
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
        ZoomToBoxLimited(extent, duration: 0);
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
        CenterOnAndZoomToLimited(center, resolution, duration: 0);
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
        ReportAnimation(_navigator.Viewport with { CenterX = x, CenterY = y }, durationMilliseconds);
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

    /// <summary>
    /// Returns the laid-out viewport resolution in map units per pixel
    /// (EPSG:3857 metres at the equator), after any zoom limits the map
    /// applies.
    /// </summary>
    /// <returns>
    /// The current resolution, or <see langword="null"/> when the viewport is
    /// not laid out or the resolution is not a positive, finite number.
    /// </returns>
    public double? TryGetViewportResolution()
    {
        var viewport = _navigator.Viewport;
        if (viewport.Width <= 0 || viewport.Height <= 0)
        {
            return null;
        }

        var resolution = viewport.Resolution;
        return double.IsFinite(resolution) && resolution > 0 ? resolution : null;
    }

    // Fits the box like Navigator.ZoomToBox (MBoxFit.Fit), through
    // CenterOnAndZoomToLimited.
    private void ZoomToBoxLimited(MRect box, long duration)
    {
        var viewport = _navigator.Viewport;
        if (!_navigator.HasExecutedPostponedCalls
            || viewport.Width <= 0 || viewport.Height <= 0
            || box.Width <= 0 || box.Height <= 0)
        {
            _navigator.ZoomToBox(box, MBoxFit.Fit, duration);
            return;
        }

        var resolution = Math.Max(box.Width / viewport.Width, box.Height / viewport.Height);
        CenterOnAndZoomToLimited(box.Centroid, resolution, duration);
    }

    // Applies the navigator's limits to the goal before handing it over.
    // When Mapsui has to clamp a requested resolution itself, it also pulls
    // the centre back toward the previous viewport's centre in proportion
    // (meant for zooming about the pointer), so a zoom-limited request would
    // not land on the requested centre (issue #749). An already-limited goal
    // passes through unchanged.
    private void CenterOnAndZoomToLimited(MPoint center, double resolution, long duration)
    {
        if (!_navigator.HasExecutedPostponedCalls)
        {
            _navigator.CenterOnAndZoomTo(center, resolution, duration);
            return;
        }

        var goal = _navigator.Viewport with { CenterX = center.X, CenterY = center.Y, Resolution = resolution };
        var limited = _navigator.Limiter.Limit(goal, _navigator.PanBounds, _navigator.ZoomBounds);
        ReportAnimation(limited, duration);
        _navigator.CenterOnAndZoomTo(
            new MPoint(limited.CenterX, limited.CenterY), limited.Resolution, duration);
    }

    private static bool IsValid(GeoPosition position) =>
        double.IsFinite(position.Latitude)
        && double.IsFinite(position.Longitude)
        && position.Latitude is >= -90.0 and <= 90.0;
}
