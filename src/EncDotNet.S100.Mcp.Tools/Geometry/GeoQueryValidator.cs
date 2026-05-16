namespace EncDotNet.S100.Mcp.Tools.Geometry;

/// <summary>
/// Validates <see cref="GeoQuery"/> inputs against the WGS-84 ranges
/// and the shape-specific constraints (closed ring, ≥2 vertices, etc.).
/// </summary>
/// <remarks>
/// Returns a typed <see cref="ToolError"/> rather than throwing so
/// tool implementations can surface the failure through the standard
/// <see cref="ToolResult{T}"/> pipeline.
/// </remarks>
public static class GeoQueryValidator
{
    /// <summary>
    /// Validates <paramref name="query"/>. Returns <c>null</c> when the
    /// query is well-formed, or an <see cref="InvalidArgument"/> /
    /// <see cref="GeometryInvalid"/> error otherwise.
    /// </summary>
    public static ToolError? Validate(GeoQuery query, string parameterName = "query")
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(parameterName);

        return query switch
        {
            GeoQuery.Point p => ValidatePoint(p.Value, parameterName),
            GeoQuery.Box b => ValidateBox(b.Value, parameterName),
            GeoQuery.Polygon pg => ValidatePolygon(pg.Value, parameterName),
            GeoQuery.Polyline pl => ValidatePolyline(pl.Value, parameterName),
            _ => new InvalidArgument(parameterName, "unknown GeoQuery variant"),
        };
    }

    private static ToolError? ValidatePoint(GeoPoint point, string parameterName)
    {
        if (!IsValidLat(point.Latitude))
        {
            return new InvalidArgument(
                $"{parameterName}.latitude",
                $"value {point.Latitude} is outside the WGS-84 range [-90, 90]");
        }

        if (!IsValidLon(point.Longitude))
        {
            return new InvalidArgument(
                $"{parameterName}.longitude",
                $"value {point.Longitude} is outside the WGS-84 range [-180, 180]");
        }

        return null;
    }

    private static ToolError? ValidateBox(GeoBoundingBox box, string parameterName)
    {
        if (!IsValidLat(box.SouthLatitude) || !IsValidLat(box.NorthLatitude))
        {
            return new InvalidArgument(
                parameterName,
                "latitude bounds must be within the WGS-84 range [-90, 90]");
        }

        if (!IsValidLon(box.WestLongitude) || !IsValidLon(box.EastLongitude))
        {
            return new InvalidArgument(
                parameterName,
                "longitude bounds must be within the WGS-84 range [-180, 180]");
        }

        if (box.NorthLatitude < box.SouthLatitude)
        {
            return new GeometryInvalid(
                parameterName,
                $"north latitude {box.NorthLatitude} is south of south latitude {box.SouthLatitude}");
        }

        // Antimeridian-crossing boxes (west > east) are not supported by
        // the current tool implementations and are flagged so callers
        // get a clear error rather than silently empty results.
        if (box.EastLongitude < box.WestLongitude)
        {
            return new GeometryInvalid(
                parameterName,
                "antimeridian-crossing bounding boxes (west > east) are not supported");
        }

        return null;
    }

    private static ToolError? ValidatePolygon(GeoPolygon polygon, string parameterName)
    {
        if (polygon.Ring.IsDefaultOrEmpty || polygon.Ring.Length < 4)
        {
            return new GeometryInvalid(
                parameterName,
                "polygon ring must contain at least four points");
        }

        foreach (var p in polygon.Ring)
        {
            if (ValidatePoint(p, parameterName) is { } err)
            {
                return err;
            }
        }

        var first = polygon.Ring[0];
        var last = polygon.Ring[^1];
        if (first.Latitude != last.Latitude || first.Longitude != last.Longitude)
        {
            return new GeometryInvalid(
                parameterName,
                "polygon ring must be closed (first and last point must be equal)");
        }

        return null;
    }

    private static ToolError? ValidatePolyline(GeoPolyline polyline, string parameterName)
    {
        if (polyline.Vertices.IsDefaultOrEmpty || polyline.Vertices.Length < 2)
        {
            return new GeometryInvalid(
                parameterName,
                "polyline must contain at least two vertices");
        }

        foreach (var p in polyline.Vertices)
        {
            if (ValidatePoint(p, parameterName) is { } err)
            {
                return err;
            }
        }

        if (polyline.CorridorWidthMeters is { } width && (double.IsNaN(width) || width < 0))
        {
            return new InvalidArgument(
                $"{parameterName}.corridorWidthMeters",
                $"value {width} must be null or non-negative");
        }

        return null;
    }

    private static bool IsValidLat(double v) => !double.IsNaN(v) && v >= -90.0 && v <= 90.0;
    private static bool IsValidLon(double v) => !double.IsNaN(v) && v >= -180.0 && v <= 180.0;
}
