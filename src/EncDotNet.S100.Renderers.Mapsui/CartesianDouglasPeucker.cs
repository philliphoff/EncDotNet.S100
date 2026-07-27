namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Cartesian variant of
/// <c>EncDotNet.S100.Pipelines.Vector.Caching.DouglasPeuckerLineSimplifier</c>
/// that operates directly on <see cref="CartesianPoint"/>s in the map
/// projection frame (EPSG:3857 metres). Skips the equirectangular scaling
/// used by the Core lat/lon simplifier — the inputs are already metric.
/// Identical algorithm otherwise: iterative, endpoint-preserving,
/// tolerance-monotonic Douglas-Peucker.
/// </summary>
internal static class CartesianDouglasPeucker
{
    /// <summary>
    /// Runs Douglas-Peucker at the given tolerance in metres. The first and
    /// last points are always preserved. Inputs shorter than three vertices
    /// are copied through unchanged.
    /// </summary>
    public static CartesianPoint[] Simplify(
        ReadOnlySpan<CartesianPoint> coordinates,
        double toleranceMetres)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(toleranceMetres);

        if (coordinates.Length < 3)
        {
            return coordinates.ToArray();
        }

        var keep = new bool[coordinates.Length];
        keep[0] = true;
        keep[^1] = true;

        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, coordinates.Length - 1));

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

            var ax = coordinates[first].X;
            var ay = coordinates[first].Y;
            var bx = coordinates[last].X;
            var by = coordinates[last].Y;
            var dx = bx - ax;
            var dy = by - ay;
            var segmentSquared = (dx * dx) + (dy * dy);

            for (var i = first + 1; i < last; i++)
            {
                var px = coordinates[i].X;
                var py = coordinates[i].Y;

                double squaredDistance;
                if (segmentSquared == 0.0)
                {
                    var qx = px - ax;
                    var qy = py - ay;
                    squaredDistance = (qx * qx) + (qy * qy);
                }
                else
                {
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

        var count = 0;
        for (var i = 0; i < keep.Length; i++)
        {
            if (keep[i]) count++;
        }

        var kept = new CartesianPoint[count];
        var w = 0;
        for (var i = 0; i < coordinates.Length; i++)
        {
            if (keep[i])
            {
                kept[w++] = coordinates[i];
            }
        }
        return kept;
    }
}
