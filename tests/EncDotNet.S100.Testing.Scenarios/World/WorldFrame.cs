using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Testing.Scenarios.World;

/// <summary>
/// The world's local frame: metres east (<c>x</c>) and north (<c>y</c>) of
/// an origin, mapped to WGS-84 degrees by a fixed equirectangular scale.
/// </summary>
/// <remarks>
/// The mapping is the definition of the world, not an approximation of the
/// Earth: a position's depth and tide are whatever the model says at its
/// local coordinates, so expected values stay exact. Over the few tens of
/// kilometres a scenario covers, the scale is within a fraction of a percent
/// of the geodesic one.
/// </remarks>
/// <param name="OriginLatitude">Latitude of the local origin, in degrees.</param>
/// <param name="OriginLongitude">Longitude of the local origin, in degrees.</param>
public sealed record WorldFrame(double OriginLatitude, double OriginLongitude)
{
    /// <summary>Metres per degree of latitude.</summary>
    public const double MetresPerDegreeLatitude = 111_320.0;

    /// <summary>Metres per degree of longitude at the origin's latitude.</summary>
    public double MetresPerDegreeLongitude => MetresPerDegreeLatitude * Math.Cos(OriginLatitude * Math.PI / 180.0);

    /// <summary>The geographic position of a local point.</summary>
    /// <param name="x">Metres east of the origin.</param>
    /// <param name="y">Metres north of the origin.</param>
    /// <returns>The position.</returns>
    public GeoPosition ToGeo(double x, double y) =>
        new(OriginLatitude + y / MetresPerDegreeLatitude, OriginLongitude + x / MetresPerDegreeLongitude);

    /// <summary>The local coordinates of a geographic position.</summary>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <returns>Metres east and north of the origin.</returns>
    public (double X, double Y) ToLocal(double latitude, double longitude) =>
        ((longitude - OriginLongitude) * MetresPerDegreeLongitude, (latitude - OriginLatitude) * MetresPerDegreeLatitude);
}

/// <summary>A rectangle in the local frame, in metres.</summary>
/// <param name="West">Western edge (metres east of the origin).</param>
/// <param name="South">Southern edge (metres north of the origin).</param>
/// <param name="East">Eastern edge.</param>
/// <param name="North">Northern edge.</param>
public readonly record struct LocalRect(double West, double South, double East, double North)
{
    /// <summary>Width in metres.</summary>
    public double Width => East - West;

    /// <summary>Height in metres.</summary>
    public double Height => North - South;

    /// <summary>Whether the point lies inside or on the edge.</summary>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <returns><c>true</c> when inside.</returns>
    public bool Contains(double x, double y) => x >= West && x <= East && y >= South && y <= North;

    /// <summary>
    /// The signed distance from the rectangle's edge: negative inside,
    /// positive outside (Chebyshev inside, Euclidean outside).
    /// </summary>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <returns>The distance in metres.</returns>
    public double SignedDistance(double x, double y)
    {
        var dx = Math.Max(West - x, x - East);
        var dy = Math.Max(South - y, y - North);
        if (dx <= 0 && dy <= 0)
        {
            return Math.Max(dx, dy);
        }

        var ox = Math.Max(dx, 0);
        var oy = Math.Max(dy, 0);
        return Math.Sqrt(ox * ox + oy * oy);
    }
}
