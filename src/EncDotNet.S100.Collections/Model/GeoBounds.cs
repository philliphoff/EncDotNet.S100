using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections;

/// <summary>
/// An axis-aligned geographic bounding box in decimal degrees (EPSG:4326).
/// </summary>
/// <remarks>
/// A box that crosses the antimeridian is recorded with
/// <see cref="West"/> greater than <see cref="East"/> (for example
/// <c>West = 172</c>, <c>East = -170</c> for the western Aleutians), the same
/// convention as the S-100 exchange-catalogue <c>boundingBox</c>.
/// </remarks>
/// <param name="South">South edge, −90..+90.</param>
/// <param name="West">West edge, −180..+180.</param>
/// <param name="North">North edge, −90..+90.</param>
/// <param name="East">East edge, −180..+180.</param>
public readonly record struct GeoBounds(double South, double West, double North, double East)
{
    /// <summary>True when the box crosses the antimeridian (<see cref="West"/> &gt; <see cref="East"/>).</summary>
    public bool CrossesAntimeridian => West > East;

    /// <summary>The longitudinal width of the box in degrees, accounting for antimeridian crossing.</summary>
    public double LongitudeSpan => ArcLength(West, East);

    /// <summary>Returns true when <paramref name="position"/> lies inside the box (edges inclusive).</summary>
    public bool Contains(GeoPosition position) =>
        position.Latitude >= South && position.Latitude <= North
        && ArcContains(West, East, position.Longitude);

    /// <summary>Returns true when this box and <paramref name="other"/> overlap (edges inclusive).</summary>
    public bool Intersects(GeoBounds other) =>
        South <= other.North && other.South <= North
        && (ArcContains(West, East, other.West) || ArcContains(other.West, other.East, West));

    /// <summary>
    /// Returns the smallest box containing both this box and
    /// <paramref name="other"/>, choosing the shorter way around the globe for
    /// the longitude range.
    /// </summary>
    public GeoBounds Union(GeoBounds other)
    {
        var south = Math.Min(South, other.South);
        var north = Math.Max(North, other.North);

        if (ArcContains(West, East, other.West) && ArcContains(West, East, other.East)
            && ArcLength(West, East) >= ArcLength(other.West, other.East))
        {
            return new GeoBounds(south, West, north, East);
        }

        if (ArcContains(other.West, other.East, West) && ArcContains(other.West, other.East, East)
            && ArcLength(other.West, other.East) >= ArcLength(West, East))
        {
            return new GeoBounds(south, other.West, north, other.East);
        }

        // Disjoint or partially overlapping arcs: the union is one of the two
        // arcs that start at one box's west edge and end at the other's east edge.
        var a = ArcLength(West, other.East);
        var b = ArcLength(other.West, East);
        return a <= b
            ? new GeoBounds(south, West, north, other.East)
            : new GeoBounds(south, other.West, north, East);
    }

    /// <summary>
    /// Computes the bounds of <paramref name="positions"/>, or
    /// <see langword="null"/> when there are none.
    /// </summary>
    /// <remarks>
    /// When the positions span more than 180° of longitude they are assumed to
    /// cross the antimeridian: the result runs from the smallest non-negative
    /// longitude east across ±180° to the largest negative one.
    /// </remarks>
    public static GeoBounds? FromPositions(IEnumerable<GeoPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        double minNonNegLon = double.MaxValue, maxNegLon = double.MinValue;
        var any = false;

        foreach (var (lat, lon) in positions)
        {
            any = true;
            minLat = Math.Min(minLat, lat);
            maxLat = Math.Max(maxLat, lat);
            minLon = Math.Min(minLon, lon);
            maxLon = Math.Max(maxLon, lon);
            if (lon >= 0)
                minNonNegLon = Math.Min(minNonNegLon, lon);
            else
                maxNegLon = Math.Max(maxNegLon, lon);
        }

        if (!any)
            return null;

        if (maxLon - minLon > 180 && minNonNegLon != double.MaxValue && maxNegLon != double.MinValue)
            return new GeoBounds(minLat, minNonNegLon, maxLat, maxNegLon);

        return new GeoBounds(minLat, minLon, maxLat, maxLon);
    }

    /// <summary>Unions a sequence of boxes, or returns <see langword="null"/> when empty.</summary>
    public static GeoBounds? UnionAll(IEnumerable<GeoBounds> bounds)
    {
        ArgumentNullException.ThrowIfNull(bounds);

        GeoBounds? result = null;
        foreach (var b in bounds)
            result = result is { } r ? r.Union(b) : b;
        return result;
    }

    private static double ArcLength(double west, double east)
    {
        var length = east - west;
        return length >= 0 ? length : length + 360;
    }

    private static bool ArcContains(double west, double east, double lon) =>
        west <= east
            ? lon >= west && lon <= east
            : lon >= west || lon <= east;
}
