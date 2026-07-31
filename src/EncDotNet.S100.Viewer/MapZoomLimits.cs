using Mapsui;

namespace EncDotNet.S100.Viewer;

/// <summary>
/// Clamps how far the map can be zoomed in and out so the user cannot
/// zoom to an unbounded, meaningless scale (e.g. hundreds of world copies
/// off the edge of a cross-antimeridian dataset, or arbitrarily deep past
/// the resolution of any chart data).
/// </summary>
/// <remarks>
/// The bounds are expressed as representative 1:N map-scale denominators
/// and converted to EPSG:3857 (web-mercator) resolution — metres per
/// pixel — using the same OGC standardized rendering pixel size
/// (0.28&#160;mm) as <see cref="MapScaleFormatter"/>. The conversion uses
/// the equatorial approximation (no latitude correction) because Mapsui's
/// zoom bounds are latitude-independent; the on-screen scale readout will
/// therefore differ slightly from these nominal denominators at high
/// latitude, which is acceptable for a coarse zoom guard.
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
    /// Converts a 1:N map-scale denominator to an EPSG:3857 resolution
    /// (metres per pixel) at the equator.
    /// </summary>
    /// <param name="scaleDenominator">The scale denominator (the N in 1:N).</param>
    /// <returns>The equatorial web-mercator resolution in metres per pixel.</returns>
    public static double ResolutionForScale(double scaleDenominator) =>
        scaleDenominator * MapScaleFormatter.PixelSizeMeters;

    /// <summary>
    /// Applies the zoom-in and zoom-out limits to the given navigator so all
    /// zoom operations (wheel, pinch, buttons, zoom-to-box) are clamped.
    /// </summary>
    /// <param name="navigator">The map navigator to constrain.</param>
    public static void Apply(Navigator navigator)
    {
        ArgumentNullException.ThrowIfNull(navigator);

        var minResolution = ResolutionForScale(MinScaleDenominator);
        var maxResolution = ResolutionForScale(MaxScaleDenominator);

        // MMinMax orders its two arguments into Min (finest resolution /
        // deepest zoom-in) and Max (coarsest resolution / farthest zoom-out).
        navigator.OverrideZoomBounds = new MMinMax(minResolution, maxResolution);
    }
}
