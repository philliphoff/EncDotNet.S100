using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// Fills polygons onto a <see cref="WaterMaskGrid"/> row by row (even-odd, so
/// holes stay outside), which costs about the number of polygon edges plus the
/// number of grid points, rather than one point-in-polygon test per point and
/// polygon. The water mask and the charted-depth grid both fill this way.
/// </summary>
internal static class GridScanline
{
    /// <summary>A run of grid points inside the rings: one row, columns <c>From</c> to <c>To</c> inclusive.</summary>
    /// <param name="Row">The row, from the south.</param>
    /// <param name="From">The first column inside.</param>
    /// <param name="To">The last column inside.</param>
    public readonly record struct Run(int Row, int From, int To);

    /// <summary>
    /// The runs of grid points inside the rings (even-odd). A point exactly
    /// on a row's crossing counts as inside on its east side only, so two
    /// polygons sharing an edge don't both claim it.
    /// </summary>
    /// <param name="rings">The rings: an exterior ring and its holes, or several.</param>
    /// <param name="grid">The grid.</param>
    /// <returns>The runs, row by row.</returns>
    public static List<Run> Runs(IReadOnlyList<IReadOnlyList<GeoPosition>> rings, WaterMaskGrid grid)
    {
        var result = new List<Run>();
        List<double>?[]? crossings = null;
        foreach (var ring in rings)
        {
            for (var k = 0; k + 1 < ring.Count; k++)
            {
                AddCrossings(ring[k], ring[k + 1], grid, ref crossings);
            }

            if (ring.Count > 2 && ring[0] != ring[^1])
            {
                AddCrossings(ring[^1], ring[0], grid, ref crossings);
            }
        }

        if (crossings is null)
        {
            return result;
        }

        for (var row = 0; row < grid.Rows; row++)
        {
            if (crossings[row] is not { Count: >= 2 } xs)
            {
                continue;
            }

            xs.Sort();
            for (var k = 0; k + 1 < xs.Count; k += 2)
            {
                var from = Math.Max(0, (int)Math.Ceiling(xs[k]));
                var to = Math.Min(grid.Columns - 1, (int)Math.Ceiling(xs[k + 1]) - 1);
                if (from <= to)
                {
                    result.Add(new Run(row, from, to));
                }
            }
        }

        return result;
    }

    /// <summary>Sets every grid point inside the rings (even-odd) in <paramref name="target"/>.</summary>
    /// <param name="rings">The rings.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="target">One flag per grid point, row by row from the south-west.</param>
    public static void Fill(IReadOnlyList<IReadOnlyList<GeoPosition>> rings, WaterMaskGrid grid, bool[] target)
    {
        foreach (var run in Runs(rings, grid))
        {
            Array.Fill(target, true, (run.Row * grid.Columns) + run.From, run.To - run.From + 1);
        }
    }

    /// <summary>Lowers every grid point inside the rings (even-odd) to at most <paramref name="value"/>; NaN counts as unset.</summary>
    /// <param name="rings">The rings.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="target">One value per grid point, row by row from the south-west.</param>
    /// <param name="value">The value.</param>
    public static void FillMinimum(IReadOnlyList<IReadOnlyList<GeoPosition>> rings, WaterMaskGrid grid, float[] target, float value)
    {
        foreach (var run in Runs(rings, grid))
        {
            var start = run.Row * grid.Columns;
            for (var col = run.From; col <= run.To; col++)
            {
                ref var slot = ref target[start + col];
                if (float.IsNaN(slot) || value < slot)
                {
                    slot = value;
                }
            }
        }
    }

    /// <summary>Sets every grid point inside a box.</summary>
    /// <param name="bounds">The box.</param>
    /// <param name="grid">The grid.</param>
    /// <param name="target">One flag per grid point.</param>
    public static void FillBox(EncDotNet.S100.Pipelines.BoundingBox bounds, WaterMaskGrid grid, bool[] target)
    {
        GeoPosition sw = new(bounds.SouthLatitude, bounds.WestLongitude), se = new(bounds.SouthLatitude, bounds.EastLongitude);
        GeoPosition ne = new(bounds.NorthLatitude, bounds.EastLongitude), nw = new(bounds.NorthLatitude, bounds.WestLongitude);
        Fill([new[] { sw, se, ne, nw, sw }], grid, target);
    }

    private static void AddCrossings(GeoPosition a, GeoPosition b, WaterMaskGrid grid, ref List<double>?[]? crossings)
    {
        var ya = (a.Latitude - grid.South) / grid.LatitudeStep;
        var yb = (b.Latitude - grid.South) / grid.LatitudeStep;
        if (ya == yb)
        {
            return;
        }

        // Rows r with min <= r < max, so a vertex on a row counts once.
        var first = Math.Max(0, (int)Math.Ceiling(Math.Min(ya, yb)));
        var last = Math.Min(grid.Rows - 1, (int)Math.Ceiling(Math.Max(ya, yb)) - 1);
        if (first > last)
        {
            return;
        }

        var xa = Wrap(a.Longitude - grid.West) / grid.LongitudeStep;
        var xb = xa + (Wrap(b.Longitude - a.Longitude) / grid.LongitudeStep);
        crossings ??= new List<double>?[grid.Rows];
        for (var row = first; row <= last; row++)
        {
            var t = (row - ya) / (yb - ya);
            (crossings[row] ??= []).Add(xa + (t * (xb - xa)));
        }
    }

    private static double Wrap(double degrees) => ((degrees + 540) % 360) - 180;
}
