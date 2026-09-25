namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Reprojects a native-CRS rectangle carried in a <see cref="BoundingBox"/>
/// (Y in the latitude members, X in the longitude members) to a WGS-84
/// envelope.
/// </summary>
internal static class BoundingBoxReprojection
{
    /// <summary>
    /// Transforms the four corners of <paramref name="native"/> through
    /// <paramref name="toWgs84"/> and returns their min/max envelope. An
    /// identity transform returns the rectangle unchanged.
    /// </summary>
    /// <param name="native">The rectangle in the source CRS.</param>
    /// <param name="toWgs84">A source → EPSG:4326 transform (X/easting first, Y/northing second).</param>
    public static BoundingBox ToWgs84(BoundingBox native, ICrsTransform toWgs84)
    {
        if (toWgs84.IsIdentity)
            return native;

        double south = double.PositiveInfinity, west = double.PositiveInfinity;
        double north = double.NegativeInfinity, east = double.NegativeInfinity;
        (double X, double Y)[] corners =
        [
            (native.WestLongitude, native.SouthLatitude),
            (native.WestLongitude, native.NorthLatitude),
            (native.EastLongitude, native.SouthLatitude),
            (native.EastLongitude, native.NorthLatitude),
        ];
        foreach (var (x, y) in corners)
        {
            var (lon, lat) = toWgs84.Transform(x, y);
            if (lat < south) south = lat;
            if (lat > north) north = lat;
            if (lon < west) west = lon;
            if (lon > east) east = lon;
        }

        return new BoundingBox(south, west, north, east);
    }
}
