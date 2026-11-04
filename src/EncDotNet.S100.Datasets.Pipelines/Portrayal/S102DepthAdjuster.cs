using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// Adds a water level to the depths of a sampled S-102 surface before it's
/// banded and its safety contour traced (S-98 Appendix D,
/// <c>docs/design/chart-wide-water-level-adjustment.md</c> §3.1).
/// </summary>
/// <remarks>
/// Each sampled node gets <c>d + h</c>, with <c>h</c> read at the node's
/// position. Nodes without a water level, and no-data nodes, keep their
/// value. The grid itself isn't touched: only the sampled copy the render
/// uses is adjusted.
/// </remarks>
internal static class S102DepthAdjuster
{
    /// <summary>The S-102 depth field.</summary>
    public const string DepthField = "depth";

    /// <summary>
    /// Returns <paramref name="layer"/> with <paramref name="adjustment"/>
    /// applied to its depths, or <paramref name="layer"/> itself when no node
    /// changes.
    /// </summary>
    /// <param name="layer">The sampled, styled surface.</param>
    /// <param name="adjustment">The water level to add.</param>
    /// <param name="verticalDatum">The S-102 dataset's vertical datum code, or <see langword="null"/>.</param>
    /// <param name="nativeToWgs84">Maps a native (x, y) to WGS-84 (longitude, latitude); <see langword="null"/> when the grid is already WGS-84.</param>
    /// <returns>The adjusted layer and how many nodes were adjusted.</returns>
    public static (StyledCoverageLayer Layer, int Adjusted) Apply(
        StyledCoverageLayer layer,
        IDepthAdjustment adjustment,
        int? verticalDatum,
        Func<double, double, (double Longitude, double Latitude)>? nativeToWgs84)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(adjustment);

        var coverage = layer.Coverage;
        if (!adjustment.Accepts(verticalDatum) || !coverage.Values.TryGetValue(DepthField, out var depths))
        {
            return (layer, 0);
        }

        var grid = coverage.Metadata;
        var georeferencer = layer.Georeferencer;
        var noData = layer.NoDataValue;
        float[]? adjusted = null;
        var count = 0;
        for (var row = 0; row < grid.NumRows; row++)
        {
            var rowBase = row * grid.NumColumns;
            for (var col = 0; col < grid.NumColumns; col++)
            {
                var index = rowBase + col;
                var depth = depths[index];
                if (depth == noData || !float.IsFinite(depth))
                {
                    continue;
                }

                var (x, y) = georeferencer.ToNative(row, col);
                var (lon, lat) = nativeToWgs84 is null ? (x, y) : nativeToWgs84(x, y);
                if (adjustment.At(lat, lon, verticalDatum) is not { } water)
                {
                    continue;
                }

                adjusted ??= (float[])depths.Clone();
                adjusted[index] = (float)(depth + water.HeightMeters);
                count++;
            }
        }

        if (adjusted is null)
        {
            return (layer, 0);
        }

        var values = new Dictionary<string, float[]>(coverage.Values, StringComparer.Ordinal)
        {
            [DepthField] = adjusted,
        };
        var sampled = new SampledCoverage
        {
            Region = coverage.Region,
            Metadata = coverage.Metadata,
            Values = values,
            ComputedFields = coverage.ComputedFields,
        };
        return (new StyledCoverageLayer
        {
            Coverage = sampled,
            ColorScheme = layer.ColorScheme,
            NoDataValue = layer.NoDataValue,
            Georeferencer = layer.Georeferencer,
            SymbolScheme = layer.SymbolScheme,
        }, count);
    }
}
