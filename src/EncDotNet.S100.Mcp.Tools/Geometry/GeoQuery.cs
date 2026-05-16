using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools.Geometry;

/// <summary>
/// Discriminated union over the supported geographic input shapes.
/// </summary>
/// <remarks>
/// <para>
/// Tools that accept a spatial parameter take a <see cref="GeoQuery"/>
/// rather than a bare lat/lon pair so the same wire shape handles
/// point, bbox, polygon and polyline-with-corridor questions.
/// </para>
/// <para>
/// Every variant projects to a coarse <see cref="GeoBoundingBox"/>
/// (via <see cref="GetBoundingBox"/>) for use against the
/// <c>BoundingBox</c>-based filters elsewhere in the catalog. Finer
/// containment / intersection (ray casting for polygons, per-vertex
/// expansion for polylines) is delegated to the per-tool consumer.
/// </para>
/// </remarks>
public abstract record GeoQuery
{
    private GeoQuery() { }

    /// <summary>Single-point query.</summary>
    public sealed record Point(GeoPoint Value) : GeoQuery;

    /// <summary>Bounding-box query.</summary>
    public sealed record Box(GeoBoundingBox Value) : GeoQuery;

    /// <summary>Polygon-membership query.</summary>
    public sealed record Polygon(GeoPolygon Value) : GeoQuery;

    /// <summary>Polyline / corridor query.</summary>
    public sealed record Polyline(GeoPolyline Value) : GeoQuery;

    /// <summary>
    /// Coarse bounding box for the query, suitable for first-pass
    /// dataset-bounds intersection.
    /// </summary>
    public GeoBoundingBox GetBoundingBox() => this switch
    {
        Point p => new GeoBoundingBox(
            p.Value.Latitude,
            p.Value.Longitude,
            p.Value.Latitude,
            p.Value.Longitude),
        Box b => b.Value,
        Polygon pg => BoundingBoxOf(pg.Value.Ring),
        Polyline pl => InflateForCorridor(BoundingBoxOf(pl.Value.Vertices), pl.Value.CorridorWidthMeters),
        _ => throw new InvalidOperationException("Unknown GeoQuery variant."),
    };

    /// <summary>Project to the legacy pipeline <see cref="BoundingBox"/> shape.</summary>
    public BoundingBox ToPipelineBoundingBox()
    {
        var b = GetBoundingBox();
        return new BoundingBox(
            b.SouthLatitude,
            b.WestLongitude,
            b.NorthLatitude,
            b.EastLongitude);
    }

    private static GeoBoundingBox BoundingBoxOf(System.Collections.Immutable.ImmutableArray<GeoPoint> points)
    {
        if (points.IsDefaultOrEmpty)
        {
            return new GeoBoundingBox(0, 0, 0, 0);
        }

        var south = double.PositiveInfinity;
        var north = double.NegativeInfinity;
        var west = double.PositiveInfinity;
        var east = double.NegativeInfinity;

        foreach (var p in points)
        {
            if (p.Latitude < south) south = p.Latitude;
            if (p.Latitude > north) north = p.Latitude;
            if (p.Longitude < west) west = p.Longitude;
            if (p.Longitude > east) east = p.Longitude;
        }

        return new GeoBoundingBox(south, west, north, east);
    }

    private static GeoBoundingBox InflateForCorridor(GeoBoundingBox box, double? halfWidthMeters)
    {
        if (halfWidthMeters is not { } half || half <= 0)
        {
            return box;
        }

        // Equirectangular approximation. 1° lat ≈ 111 320 m; longitude
        // shrinks with latitude. Use the polewards-most latitude so the
        // inflated box never under-covers the corridor.
        const double MetersPerDegreeLat = 111_320.0;
        var latPad = half / MetersPerDegreeLat;
        var refLat = Math.Max(Math.Abs(box.SouthLatitude), Math.Abs(box.NorthLatitude));
        var cosLat = Math.Cos(refLat * Math.PI / 180.0);
        var lonPad = cosLat > 1e-9
            ? half / (MetersPerDegreeLat * cosLat)
            : 180.0;

        return new GeoBoundingBox(
            Math.Max(-90.0, box.SouthLatitude - latPad),
            Math.Max(-180.0, box.WestLongitude - lonPad),
            Math.Min(90.0, box.NorthLatitude + latPad),
            Math.Min(180.0, box.EastLongitude + lonPad));
    }
}
