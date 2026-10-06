using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Library;

/// <summary>
/// Point hit-testing of library item coverage (issue #655): whether a
/// position lies within an item's coverage polygons (or bounds), across the
/// antimeridian.
/// </summary>
/// <remarks>
/// Coverage rings arrive in two antimeridian conventions: NOAA writes
/// <em>continuous</em> longitudes (e.g. −219.5 for 140.5°E), while S-100
/// catalogues keep −180..180 and let a ring jump across the seam. Rings are
/// <see cref="Unwrap">unwrapped</see> into continuous longitudes and tested
/// at the point and its ±360° copies.
/// </remarks>
public static class CoverageHitTest
{
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
