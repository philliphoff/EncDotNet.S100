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
/// row by row (<see cref="GridScanline"/>).
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
                GridScanline.FillBox(cell.Bounds, grid, cellCoverage);
            }
            else
            {
                foreach (var area in cell.Coverage)
                {
                    GridScanline.Fill([area.ExteriorRing, .. area.InteriorRings], grid, cellCoverage);
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
                    GridScanline.Fill([surface.ExteriorRing, .. surface.InteriorRings], grid, cellLand);
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
}
