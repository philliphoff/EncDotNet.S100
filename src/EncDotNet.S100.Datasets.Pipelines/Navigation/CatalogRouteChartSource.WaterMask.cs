using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines.Navigation;

/// <summary>
/// The water mask of the loaded charts (<see cref="IWaterMask"/>): at a point,
/// the finest cells covering it decide, and it's land when one of them charts
/// a <c>LandArea</c> there.
/// </summary>
/// <remarks>
/// A grid is classified by filling each cell's coverage and land polygons
/// row by row (even-odd, so holes stay water), which costs about the number
/// of polygon edges plus the number of points, rather than one
/// point-in-polygon test per point and polygon.
/// </remarks>
internal sealed partial class CatalogRouteChartSource : IWaterMask
{
    /// <inheritdoc />
    public WaterMaskClass Classify(GeoPosition position)
    {
        Span<WaterMaskClass> one = stackalloc WaterMaskClass[1];
        Classify(new WaterMaskGrid(position.Latitude, position.Longitude, 1e-9, 1e-9, 1, 1), one);
        return one[0];
    }

    /// <inheritdoc />
    public void Classify(WaterMaskGrid grid, Span<WaterMaskClass> result)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(result.Length, grid.Count, nameof(result));
        var count = grid.Count;
        var box = new BoundingBox(grid.South, Normalize(grid.West), grid.North, Normalize(grid.East));

        // The finest rank covering each point so far, and whether one of the
        // cells of that rank charts land there.
        var finest = new CellRank?[count];
        var covered = new bool[count];
        var land = new bool[count];
        var cellCoverage = new bool[count];
        var cellLand = new bool[count];
        foreach (var cell in _cells)
        {
            if (!Overlaps(cell.Bounds, box))
            {
                continue;
            }

            Array.Clear(cellCoverage);
            if (cell.Coverage.Count == 0)
            {
                FillBox(cell.Bounds, grid, cellCoverage);
            }
            else
            {
                foreach (var area in cell.Coverage)
                {
                    Fill([area.ExteriorRing, .. area.InteriorRings], grid, cellCoverage);
                }
            }

            Array.Clear(cellLand);
            var anyLand = false;
            foreach (var feature in cell.Index.Query(box))
            {
                if (feature.GeometryType != GeometryType.Surface
                    || SkinOfTheEarth.Classify(feature.FeatureType) != SkinOfTheEarthClass.Land)
                {
                    continue;
                }

                IS100Feature geometry = feature;
                foreach (var surface in geometry.Surfaces)
                {
                    Fill([surface.ExteriorRing, .. surface.InteriorRings], grid, cellLand);
                    anyLand = true;
                }
            }

            var rank = cell.Rank;
            for (var i = 0; i < count; i++)
            {
                if (!cellCoverage[i])
                {
                    continue;
                }

                var order = covered[i] ? CellScaleRanking.Compare(rank, finest[i]) : -1;
                if (order < 0)
                {
                    covered[i] = true;
                    finest[i] = rank;
                    land[i] = anyLand && cellLand[i];
                }
                else if (order == 0)
                {
                    land[i] |= anyLand && cellLand[i];
                }
            }
        }

        for (var i = 0; i < count; i++)
        {
            result[i] = !covered[i] ? WaterMaskClass.Uncharted : land[i] ? WaterMaskClass.Land : WaterMaskClass.Water;
        }
    }

    private static double Normalize(double lon) => ((lon + 540) % 360) - 180;

    private static void FillBox(BoundingBox bounds, WaterMaskGrid grid, bool[] target)
    {
        GeoPosition sw = new(bounds.SouthLatitude, bounds.WestLongitude), se = new(bounds.SouthLatitude, bounds.EastLongitude);
        GeoPosition ne = new(bounds.NorthLatitude, bounds.EastLongitude), nw = new(bounds.NorthLatitude, bounds.WestLongitude);
        Fill([new[] { sw, se, ne, nw, sw }], grid, target);
    }

    // Sets every grid point inside the rings (even-odd) in target. A point
    // exactly on a row's crossing counts as inside on its east side only, so
    // two polygons sharing an edge don't both claim it.
    private static void Fill(IReadOnlyList<IReadOnlyList<GeoPosition>> rings, WaterMaskGrid grid, bool[] target)
    {
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
            return;
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
                for (var col = from; col <= to; col++)
                {
                    target[(row * grid.Columns) + col] = true;
                }
            }
        }
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

        var xa = Column(a.Longitude, grid);
        var xb = xa + (Wrap(b.Longitude - a.Longitude) / grid.LongitudeStep);
        crossings ??= new List<double>?[grid.Rows];
        for (var row = first; row <= last; row++)
        {
            var t = (row - ya) / (yb - ya);
            (crossings[row] ??= []).Add(xa + (t * (xb - xa)));
        }
    }

    private static double Column(double longitude, WaterMaskGrid grid) => Wrap(longitude - grid.West) / grid.LongitudeStep;

    private static double Wrap(double degrees) => ((degrees + 540) % 360) - 180;
}
