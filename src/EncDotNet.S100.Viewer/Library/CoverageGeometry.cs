using EncDotNet.S100.Collections;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Viewer.Services.LazyLoading;

namespace EncDotNet.S100.Viewer.Library;

/// <summary>
/// Geometry helpers for drawing and hit-testing library coverage
/// (issue #655): projection to Web Mercator with antimeridian handling,
/// scale gating, and point containment.
/// </summary>
/// <remarks>
/// <para>
/// Coverage rings arrive in two antimeridian conventions: NOAA writes
/// <em>continuous</em> longitudes (e.g. −219.5 for 140.5°E), while S-100
/// catalogues keep −180..180 and let a ring jump across the seam. Both are
/// first <em>unwrapped</em> into a continuous ring. A ring lying wholly
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
    /// Makes a ring's longitudes continuous: each vertex is moved by a
    /// multiple of 360° so it lies within 180° of its predecessor.
    /// </summary>
    public static GeoPosition[] Unwrap(IReadOnlyList<GeoPosition> ring)
    {
        ArgumentNullException.ThrowIfNull(ring);

        var result = new GeoPosition[ring.Count];
        for (var i = 0; i < ring.Count; i++)
        {
            var lon = ring[i].Longitude;
            if (i > 0)
            {
                var previous = result[i - 1].Longitude;
                while (lon - previous > 180)
                    lon -= 360;
                while (lon - previous < -180)
                    lon += 360;
            }

            result[i] = new GeoPosition(ring[i].Latitude, lon);
        }

        return result;
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
    /// drop away. Other items show down to a few times their coarsest display
    /// scale; anything without either always shows.
    /// </summary>
    public static bool IsVisibleAtScale(CollectionItem item, double scaleDenominator)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.UsageBand is { } band)
        {
            var finest = FinestEligibleBand(scaleDenominator);
            return band == finest || band == finest + 1;
        }

        if (item.MinimumDisplayScale is { } coarsest && !double.IsNaN(scaleDenominator))
            return scaleDenominator <= coarsest * 4;  // allow some zoom-out before hiding

        return true;
    }

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

    /// <summary>True when <paramref name="position"/> lies within the item's coverage (or bounds).</summary>
    public static bool Contains(CollectionItem item, GeoPosition position)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (item.Bounds is not { } bounds || !bounds.Contains(Normalize(position)))
            return false;

        if (item.Coverage is not { } coverage)
            return true;

        foreach (var polygon in coverage.Polygons)
        {
            var exterior = Unwrap(polygon.Exterior);
            if (!ContainsAnyShift(exterior, position))
                continue;
            if (polygon.Holes.Any(h => ContainsAnyShift(Unwrap(h), position)))
                continue;
            return true;
        }

        return false;
    }

    /// <summary>
    /// A size used to prefer the most detailed of several overlapping items:
    /// the bounds' area in square degrees (smaller is more detailed).
    /// </summary>
    public static double Area(CollectionItem item) =>
        item.Bounds is { } b ? b.LongitudeSpan * (b.North - b.South) : double.MaxValue;

    private static void AddRing(List<(double X, double Y)[]> rings, IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 2)
            return;

        var unwrapped = Unwrap(ring);
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

    private static bool ContainsAnyShift(GeoPosition[] ring, GeoPosition point)
    {
        for (var k = -1; k <= 1; k++)
        {
            if (RingContains(ring, point.Latitude, point.Longitude + k * 360.0))
                return true;
        }

        return false;
    }

    /// <summary>Even-odd ray casting.</summary>
    private static bool RingContains(GeoPosition[] ring, double lat, double lon)
    {
        var inside = false;
        for (int i = 0, j = ring.Length - 1; i < ring.Length; j = i++)
        {
            var (latI, lonI) = ring[i];
            var (latJ, lonJ) = ring[j];
            if ((latI > lat) != (latJ > lat)
                && lon < (lonJ - lonI) * (lat - latI) / (latJ - latI) + lonI)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static GeoPosition Normalize(GeoPosition position) =>
        new(position.Latitude, GeoBounds.NormalizeLongitude(position.Longitude));
}
