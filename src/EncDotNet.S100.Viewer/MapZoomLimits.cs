using Mapsui;
using Mapsui.Limiting;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Clamps how far the map can be zoomed in and out so the user cannot
/// zoom to an unbounded, meaningless scale (e.g. hundreds of world copies
/// off the edge of a cross-antimeridian dataset, or arbitrarily deep past
/// the resolution of any chart data).
/// </summary>
/// <remarks>
/// <para>
/// The bounds are 1:N map-scale denominators. Web-mercator stretches ground
/// distance by <c>1/cos(latitude)</c>, so the EPSG:3857 resolution that shows
/// a given scale depends on latitude. The limits are therefore converted at
/// the centre latitude of each requested viewport, with the same conversion as
/// the status bar (<see cref="MapScaleFormatter.ScaleDenominatorToResolution"/>),
/// so the status bar reads exactly <see cref="MinScaleDenominator"/> or
/// <see cref="MaxScaleDenominator"/> at the clamp, whatever the latitude.
/// </para>
/// <para>
/// Mapsui's <see cref="Navigator.OverrideZoomBounds"/> is a single global
/// range, so <see cref="Apply"/> also installs a <see cref="LatitudeAwareLimiter"/>
/// that recomputes the range from each viewport it is asked to limit. Because
/// the bounds are a pure function of the requested viewport, no viewport-change
/// hook or feedback loop is involved.
/// </para>
/// </remarks>
internal static class MapZoomLimits
{
    /// <summary>
    /// Coarsest permitted scale denominator (zoom-out floor). At this scale
    /// roughly one world is visible, which is as far out as any chart view
    /// is useful.
    /// </summary>
    public const double MaxScaleDenominator = 500_000_000.0;

    /// <summary>
    /// Finest permitted scale denominator (zoom-in ceiling). 1:1&#160;000 is
    /// about the largest compilation scale used by S-100 chart data
    /// (berthing/harbour detail); zooming closer only magnifies pixels.
    /// </summary>
    public const double MinScaleDenominator = 1_000.0;

    /// <summary>
    /// Practical EPSG:3857 latitude limit (±85.05112878°); latitudes are
    /// clamped to it before the cosine correction.
    /// </summary>
    internal const double MaxMercatorLatitude = 85.05112878;

    /// <summary>
    /// Converts a 1:N map-scale denominator to an EPSG:3857 resolution
    /// (metres per pixel) at <paramref name="latitudeDegrees"/>.
    /// </summary>
    /// <param name="scaleDenominator">The scale denominator (the N in 1:N).</param>
    /// <param name="latitudeDegrees">The viewport centre latitude in decimal degrees; <c>0</c> is the equator.</param>
    /// <returns>The web-mercator resolution in metres per pixel.</returns>
    public static double ResolutionForScale(double scaleDenominator, double latitudeDegrees = 0.0) =>
        MapScaleFormatter.ScaleDenominatorToResolution(
            scaleDenominator,
            Math.Clamp(latitudeDegrees, -MaxMercatorLatitude, MaxMercatorLatitude));

    /// <summary>
    /// The zoom bounds (finest and coarsest resolution) that keep the map
    /// between <see cref="MinScaleDenominator"/> and <see cref="MaxScaleDenominator"/>
    /// at <paramref name="latitudeDegrees"/>.
    /// </summary>
    /// <param name="latitudeDegrees">The viewport centre latitude in decimal degrees.</param>
    /// <returns>The resolution range, finest first.</returns>
    public static MMinMax BoundsAt(double latitudeDegrees) =>
        // MMinMax orders its two arguments into Min (finest resolution /
        // deepest zoom-in) and Max (coarsest resolution / farthest zoom-out).
        new(ResolutionForScale(MinScaleDenominator, latitudeDegrees),
            ResolutionForScale(MaxScaleDenominator, latitudeDegrees));

    /// <summary>
    /// Applies the zoom-in and zoom-out limits to the given navigator so all
    /// zoom operations (wheel, pinch, buttons, zoom-to-box, scripted
    /// viewports) are clamped at the viewport's own latitude. Idempotent.
    /// </summary>
    /// <param name="navigator">The map navigator to constrain.</param>
    public static void Apply(Navigator navigator)
    {
        ArgumentNullException.ThrowIfNull(navigator);

        // The equatorial range keeps Navigator.ZoomBounds meaningful for
        // anything that reads it directly; the limiter replaces it per
        // viewport with the latitude-corrected range.
        navigator.OverrideZoomBounds = BoundsAt(0.0);

        if (navigator.Limiter is not LatitudeAwareLimiter)
            navigator.Limiter = new LatitudeAwareLimiter(navigator.Limiter);
    }

    /// <summary>
    /// Wraps the navigator's limiter, replacing the global zoom bounds with
    /// <see cref="BoundsAt"/> for the requested viewport's centre latitude.
    /// Pan limiting is left to the wrapped limiter.
    /// </summary>
    internal sealed class LatitudeAwareLimiter : IViewportLimiter
    {
        private readonly IViewportLimiter _inner;

        /// <summary>Creates the limiter around <paramref name="inner"/>.</summary>
        /// <param name="inner">The limiter that applies pan and zoom bounds.</param>
        public LatitudeAwareLimiter(IViewportLimiter inner)
        {
            ArgumentNullException.ThrowIfNull(inner);
            _inner = inner;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// The wrapped limiter may also move the centre (Mapsui keeps the
        /// viewport within the map's pan bounds, e.g. the loaded data extent),
        /// which changes the latitude the scale is read at. When that happens
        /// the result is limited again with the bounds for its final latitude.
        /// </remarks>
        public Viewport Limit(Viewport viewport, MRect? panBounds, MMinMax? zoomBounds)
        {
            var latitude = MapScaleFormatter.MercatorYToLatitudeDegrees(viewport.CenterY);
            if (!double.IsFinite(latitude))
                return _inner.Limit(viewport, panBounds, zoomBounds);

            var limited = _inner.Limit(viewport, panBounds, BoundsAt(latitude));
            var finalLatitude = MapScaleFormatter.MercatorYToLatitudeDegrees(limited.CenterY);
            return double.IsFinite(finalLatitude) && Math.Abs(finalLatitude - latitude) > LatitudeTolerance
                // Re-limit the original request (its resolution, at the
                // pan-limited centre): limiting the already-clamped result
                // could only coarsen it further, never restore the finer
                // bound the final latitude allows.
                ? _inner.Limit(
                    viewport with { CenterX = limited.CenterX, CenterY = limited.CenterY },
                    panBounds,
                    BoundsAt(finalLatitude))
                : limited;
        }

        /// <summary>Latitude change (degrees) below which a second pass is skipped.</summary>
        private const double LatitudeTolerance = 1e-9;
    }
}
