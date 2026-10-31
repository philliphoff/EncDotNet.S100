using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// The line work SoundCharts traces from an S-102 grid: the safety contour
/// (S-164 UserSelectedSafetyContour) and the border along the limit of the
/// S-102 data, where S-101 takes over (S-98 Part A §A-6.9.1).
/// </summary>
/// <remarks>
/// Both are traced with marching squares (<see cref="IsolineTracer"/>)
/// through the depth values the coverage pipeline sampled, so they follow the
/// viewport subset, stride and shoal-biased overview the bands use. No-data
/// nodes break the safety contour, which therefore ends at the coverage
/// limit. The limit is traced as the 0.5 isoline of a valid-data mask, so it
/// runs halfway between the last valid node and the first no-data one, and
/// isn't drawn along the edges of the sampled grid: there the data continues
/// into the next tile or the viewport goes on.
/// </remarks>
public static class S102DepthLines
{
    /// <summary>The S-102 coverage field the lines are traced on.</summary>
    public const string DepthField = "depth";

    /// <summary>Traces the depth isoline at <paramref name="depthMetres"/>.</summary>
    /// <param name="layer">The sampled S-102 coverage.</param>
    /// <param name="depthMetres">The safety contour, in metres (positive down).</param>
    /// <param name="nativeToWgs84">Converts the grid's native x, y to WGS-84 longitude, latitude; <see langword="null"/> for a geographic grid.</param>
    /// <returns>The isolines, in EPSG:3857 metres, unlabelled.</returns>
    public static IReadOnlyList<Isoline> SafetyContour(
        StyledCoverageLayer layer,
        double depthMetres,
        Func<double, double, (double Longitude, double Latitude)>? nativeToWgs84 = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var style = new ContourStyle { Value = depthMetres, Unit = "m", LineWidth = 1, Color = "#000000" };
        return CoverageContours.Trace(layer, DepthField, [style], nativeToWgs84: nativeToWgs84);
    }

    /// <summary>Traces the limit of the valid depth values.</summary>
    /// <param name="layer">The sampled S-102 coverage.</param>
    /// <param name="nativeToWgs84">Converts the grid's native x, y to WGS-84 longitude, latitude; <see langword="null"/> for a geographic grid.</param>
    /// <returns>The border, in EPSG:3857 metres, unlabelled; empty when every node, or none, has a value.</returns>
    public static IReadOnlyList<Isoline> CoverageLimit(
        StyledCoverageLayer layer,
        Func<double, double, (double Longitude, double Latitude)>? nativeToWgs84 = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var grid = layer.Coverage.Metadata;
        int rows = grid.NumRows, columns = grid.NumColumns;
        var depths = layer.Coverage.GetValues(DepthField);
        float noData = layer.NoDataValue;

        var mask = new float[rows * columns];
        bool anyValid = false, anyMissing = false;
        for (int i = 0; i < mask.Length && i < depths.Length; i++)
        {
            bool valid = !float.IsNaN(depths[i]) && depths[i] != noData && depths[i] != S102CoverageSource.FillValue;
            mask[i] = valid ? 1f : 0f;
            anyValid |= valid;
            anyMissing |= !valid;
        }

        if (!anyValid || !anyMissing)
        {
            return [];
        }

        var traced = IsolineTracer.Trace(mask, rows, columns, noData: -1f, [0.5]);
        var result = new List<Isoline>(traced.Count);
        foreach (var line in traced)
        {
            var points = line.Points;
            var mercator = new double[points.Length];
            for (int i = 0; i + 1 < points.Length; i += 2)
            {
                double x = grid.OriginLongitude + points[i] * grid.SpacingLongitudinal;
                double y = grid.OriginLatitude + points[i + 1] * grid.SpacingLatitudinal;
                var (longitude, latitude) = nativeToWgs84 is null ? (x, y) : nativeToWgs84(x, y);
                (mercator[i], mercator[i + 1]) = WebMercator.FromLonLat(longitude, latitude);
            }

            result.Add(new Isoline { Level = 0.5, Label = string.Empty, MercatorCoordinates = mercator });
        }

        return result;
    }

    /// <summary>
    /// The EPSG:3857 extent of the sampled grid as the surface is drawn: the
    /// bounds of its corner nodes, padded by half a cell. The line sub-layers
    /// use it so they frame the dataset exactly as the surface does.
    /// </summary>
    /// <param name="layer">The sampled S-102 coverage.</param>
    /// <param name="nativeToWgs84">Converts the grid's native x, y to WGS-84 longitude, latitude; <see langword="null"/> for a geographic grid.</param>
    /// <returns>The extent.</returns>
    public static MercatorBounds SurfaceExtent(
        StyledCoverageLayer layer,
        Func<double, double, (double Longitude, double Latitude)>? nativeToWgs84 = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        var grid = layer.Coverage.Metadata;
        int lastRow = Math.Max(grid.NumRows - 1, 0), lastColumn = Math.Max(grid.NumColumns - 1, 0);
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        foreach (var (row, column) in new[] { (0, 0), (0, lastColumn), (lastRow, 0), (lastRow, lastColumn) })
        {
            double x = grid.OriginLongitude + (column * grid.SpacingLongitudinal);
            double y = grid.OriginLatitude + (row * grid.SpacingLatitudinal);
            var (longitude, latitude) = nativeToWgs84 is null ? (x, y) : nativeToWgs84(x, y);
            var (mx, my) = WebMercator.FromLonLat(longitude, latitude);
            (minX, minY, maxX, maxY) = (Math.Min(minX, mx), Math.Min(minY, my), Math.Max(maxX, mx), Math.Max(maxY, my));
        }

        double halfX = (maxX - minX) / Math.Max(lastColumn, 1) / 2;
        double halfY = (maxY - minY) / Math.Max(lastRow, 1) / 2;
        return new MercatorBounds(minX - halfX, minY - halfY, maxX + halfX, maxY + halfY);
    }
}
