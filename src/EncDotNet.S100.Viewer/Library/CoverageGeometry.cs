using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Library;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Services.LazyLoading;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Geometry helpers for drawing library coverage (issue #655): projection to
/// Web Mercator with antimeridian handling, and scale gating. Point
/// hit-testing is <see cref="CoverageHitTest"/>.
/// </summary>
/// <remarks>
/// <para>
/// Coverage rings arrive in two antimeridian conventions: NOAA writes
/// <em>continuous</em> longitudes (e.g. −219.5 for 140.5°E), while S-100
/// catalogues keep −180..180 and let a ring jump across the seam. Both are
/// first <see cref="CoverageHitTest.Unwrap">unwrapped</see> into a continuous ring. A ring lying wholly
/// outside −180..180 is then shifted back into range, and one straddling ±180
/// is emitted twice (shifted by ±360) so each half draws on its side of the
/// seam; the off-world remainder of each copy is simply outside the map.
/// </para>
/// </remarks>
internal static class CoverageGeometry
{
    /// <summary>Web Mercator's latitude limit.</summary>
    private const double MaxLatitude = 85.05112878;

    /// <summary>
    /// The item's outline rings in Web Mercator metres: its coverage
    /// polygons (exteriors and holes) or, lacking coverage, its bounds as a
    /// rectangle. Empty when the item has no geometry.
    /// </summary>
    public static IReadOnlyList<(double X, double Y)[]> ToMercatorRings(CollectionItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        var rings = new List<(double X, double Y)[]>();
        if (item.Coverage is { } coverage)
        {
            foreach (var polygon in coverage.Polygons)
            {
                AddRing(rings, polygon.Exterior);
                foreach (var hole in polygon.Holes)
                    AddRing(rings, hole);
            }
        }
        else if (item.Bounds is { } b)
        {
            var east = b.CrossesAntimeridian ? b.East + 360 : b.East;
            AddRing(rings,
            [
                new GeoPosition(b.South, b.West),
                new GeoPosition(b.North, b.West),
                new GeoPosition(b.North, east),
                new GeoPosition(b.South, east),
                new GeoPosition(b.South, b.West),
            ]);
        }

        return rings;
    }

    /// <summary>
    /// The longitude shifts (multiples of 360°) at which an unwrapped ring
    /// spanning <paramref name="minLon"/>..<paramref name="maxLon"/> overlaps
    /// the −180..180 world.
    /// </summary>
    public static IReadOnlyList<double> WorldShifts(double minLon, double maxLon)
    {
        var shifts = new List<double>(2);
        for (var k = -2; k <= 2; k++)
        {
            var shift = k * 360.0;
            if (maxLon + shift > -180 && minLon + shift < 180)
                shifts.Add(shift);
        }

        return shifts;
    }

    /// <summary>
    /// True when the item should be outlined at <paramref name="scaleDenominator"/>.
    /// ENC cells show in a two-band window: the finest usage band suited to
    /// the scale (the band lazy loading would load) plus the next finer band,
    /// previewing what zooming in reveals while coarser, overlapping bands
    /// drop away. Other items (S-101 and the like, gated by display scale)
    /// show at every zoomed-out scale, so a collection's extents never vanish
    /// when zoomed out, and drop away only when zoomed in well past their
    /// finest display scale, as coarser bands do; anything without either
    /// always shows.
    /// </summary>
    public static bool IsVisibleAtScale(CollectionItem item, double scaleDenominator)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.UsageBand is { } band)
        {
            var finest = FinestEligibleBand(scaleDenominator);
            return band == finest || band == finest + 1;
        }

        if (item.MaximumDisplayScale is > 0 and var finestScale && !double.IsNaN(scaleDenominator))
            return scaleDenominator >= finestScale / DisplayScaleZoomInAllowance;

        return true;
    }

    /// <summary>How far past its finest display scale a band-less item stays outlined when zooming in.</summary>
    public const double DisplayScaleZoomInAllowance = 4;

    /// <summary>The finest ENC usage band (1–6) suited to <paramref name="scaleDenominator"/>.</summary>
    public static int FinestEligibleBand(double scaleDenominator)
    {
        for (var band = CellUsageBand.MaxBand; band > CellUsageBand.MinBand; band--)
        {
            if (LazyCellGate.IsBandEligible(band, scaleDenominator))
                return band;
        }

        return CellUsageBand.MinBand;
    }

    private static void AddRing(List<(double X, double Y)[]> rings, IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 2)
            return;

        var unwrapped = CoverageHitTest.Unwrap(ring);
        var minLon = unwrapped.Min(p => p.Longitude);
        var maxLon = unwrapped.Max(p => p.Longitude);

        foreach (var shift in WorldShifts(minLon, maxLon))
        {
            var coordinates = new (double X, double Y)[unwrapped.Length];
            for (var i = 0; i < unwrapped.Length; i++)
            {
                var lat = Math.Clamp(unwrapped[i].Latitude, -MaxLatitude, MaxLatitude);
                var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(unwrapped[i].Longitude + shift, lat);
                coordinates[i] = (x, y);
            }

            rings.Add(coordinates);
        }
    }
}
