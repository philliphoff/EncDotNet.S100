using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Pipelines.Vector.Caching;

/// <summary>
/// Iterative Douglas-Peucker line simplifier operating directly on
/// <see cref="GeoPosition"/> coordinates. The perpendicular distance is
/// evaluated in metres via an equirectangular projection anchored at the
/// input's mid-latitude, which is accurate to well under one screen pixel
/// at chart display scales and avoids introducing an NTS dependency at the
/// Core layer.
/// </summary>
/// <remarks>
/// <para>
/// The classical Douglas-Peucker algorithm preserves the endpoints, is
/// symmetric with respect to input direction, and — critically for the
/// perf gate — is guaranteed by construction to be <em>tolerance-monotonic</em>:
/// running it at a smaller tolerance never drops a point that a larger
/// tolerance kept.
/// </para>
/// <para>
/// This implementation is iterative (uses a stack of segment intervals
/// rather than recursion) so it does not blow the .NET call stack on
/// pathological inputs with tens of thousands of vertices — of which the
/// dense S-101 cell used for the perf baseline has several. It is pure
/// (no allocations other than the output buffer and one boolean scratch
/// array) and thread-safe.
/// </para>
/// </remarks>
public static class DouglasPeuckerLineSimplifier
{
    /// <summary>
    /// Simplifies a polyline using Douglas-Peucker at the given tolerance.
    /// The first and last points are always preserved, matching the standard
    /// definition and satisfying the endpoint-preservation acceptance
    /// criterion for the LOD pyramid.
    /// </summary>
    /// <param name="coordinates">
    /// Input line vertices in lat/lon order. Must have at least two points;
    /// shorter inputs are returned unchanged.
    /// </param>
    /// <param name="toleranceMetres">
    /// Maximum perpendicular deviation, in metres, that a simplified segment
    /// may hide. Must be positive.
    /// </param>
    /// <returns>
    /// A new list containing the surviving vertices in their original order.
    /// </returns>
    public static IReadOnlyList<GeoPosition> Simplify(
        IReadOnlyList<GeoPosition> coordinates,
        double toleranceMetres)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toleranceMetres);

        if (coordinates.Count < 3)
        {
            return coordinates;
        }

        // Establish an equirectangular projection anchored at mid-latitude
        // so tolerances specified in metres translate to a single, uniform
        // planar frame for the whole line. Deviations from a true
        // ellipsoidal metric are negligible for the sub-metre / sub-pixel
        // tolerances the LOD pyramid uses at chart-display scales.
        var (midLatRadians, metresPerDegreeLatitude, metresPerDegreeLongitude) =
            ComputeProjectionScales(coordinates);

        var projected = new (double X, double Y)[coordinates.Count];
        for (var i = 0; i < coordinates.Count; i++)
        {
            projected[i] = Project(
                coordinates[i], metresPerDegreeLatitude, metresPerDegreeLongitude);
        }

        _ = midLatRadians;

        var keep = ComputeKeepMask(projected, toleranceMetres);

        var kept = new List<GeoPosition>(coordinates.Count);
        for (var i = 0; i < coordinates.Count; i++)
        {
            if (keep[i])
            {
                kept.Add(coordinates[i]);
            }
        }

        return kept;
    }

    /// <summary>
    /// Runs the Douglas-Peucker inner loop against pre-projected Cartesian
    /// coordinates and returns a boolean keep-mask parallel to
    /// <paramref name="projected"/>. Extracted from <see cref="Simplify"/> so
    /// alternative callers can substitute a different planar frame — in
    /// particular <see cref="LineLodPyramid.BuildForMercatorSelection"/>,
    /// which projects to true Web Mercator (EPSG:3857) so its DP output is
    /// bit-identical to the renderer's pre-#489 Cartesian pyramid.
    /// The DP maths (iterative, endpoint-preserving, cross-product-squared
    /// perpendicular distance) is byte-for-byte identical to
    /// <c>EncDotNet.S100.Renderers.Mapsui.CartesianDouglasPeucker.Simplify</c>.
    /// </summary>
    internal static bool[] ComputeKeepMask(
        ReadOnlySpan<(double X, double Y)> projected,
        double toleranceMetres)
    {
        var keep = new bool[projected.Length];
        if (projected.Length == 0)
        {
            return keep;
        }

        keep[0] = true;
        keep[^1] = true;

        if (projected.Length < 3)
        {
            return keep;
        }

        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, projected.Length - 1));

        var toleranceSquared = toleranceMetres * toleranceMetres;

        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            if (last - first < 2)
            {
                continue;
            }

            var maxSquaredDistance = 0.0;
            var farthestIndex = -1;

            var ax = projected[first].X;
            var ay = projected[first].Y;
            var bx = projected[last].X;
            var by = projected[last].Y;
            var dx = bx - ax;
            var dy = by - ay;
            var segmentSquared = (dx * dx) + (dy * dy);

            for (var i = first + 1; i < last; i++)
            {
                var px = projected[i].X;
                var py = projected[i].Y;

                double squaredDistance;
                if (segmentSquared == 0.0)
                {
                    // Zero-length segment: distance is to the (equal)
                    // endpoint.
                    var qx = px - ax;
                    var qy = py - ay;
                    squaredDistance = (qx * qx) + (qy * qy);
                }
                else
                {
                    // Perpendicular distance squared (see e.g. Douglas &
                    // Peucker, "Algorithms for the reduction of the number
                    // of points required to represent a digitized line",
                    // 1973).
                    var cross = (dx * (py - ay)) - (dy * (px - ax));
                    squaredDistance = (cross * cross) / segmentSquared;
                }

                if (squaredDistance > maxSquaredDistance)
                {
                    maxSquaredDistance = squaredDistance;
                    farthestIndex = i;
                }
            }

            if (farthestIndex >= 0 && maxSquaredDistance > toleranceSquared)
            {
                keep[farthestIndex] = true;
                stack.Push((first, farthestIndex));
                stack.Push((farthestIndex, last));
            }
        }

        return keep;
    }

    private static (double MidLatRadians, double MetresPerDegreeLatitude, double MetresPerDegreeLongitude)
        ComputeProjectionScales(IReadOnlyList<GeoPosition> coordinates)
    {
        var minLat = coordinates[0].Latitude;
        var maxLat = minLat;
        for (var i = 1; i < coordinates.Count; i++)
        {
            var lat = coordinates[i].Latitude;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
        }

        var midLatDegrees = (minLat + maxLat) * 0.5;
        var midLatRadians = midLatDegrees * (Math.PI / 180.0);

        // WGS-84 nominal metres per degree of latitude and longitude. The
        // latitude value varies < 1% across the ellipsoid so a constant is
        // fine here; the longitude value scales with cos(latitude).
        const double MetresPerDegreeLatitude = 111_320.0;
        var metresPerDegreeLongitude = MetresPerDegreeLatitude * Math.Cos(midLatRadians);

        return (midLatRadians, MetresPerDegreeLatitude, metresPerDegreeLongitude);
    }

    private static (double X, double Y) Project(
        GeoPosition position,
        double metresPerDegreeLatitude,
        double metresPerDegreeLongitude)
    {
        var x = position.Longitude * metresPerDegreeLongitude;
        var y = position.Latitude * metresPerDegreeLatitude;
        return (x, y);
    }
}
