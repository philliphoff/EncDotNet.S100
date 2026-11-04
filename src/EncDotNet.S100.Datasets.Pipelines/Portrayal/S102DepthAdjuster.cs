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
    /// <param name="createNativeToWgs84">
    /// Creates a map from a native (x, y) to WGS-84 (longitude, latitude), or
    /// returns <see langword="null"/> when the grid is already WGS-84. Called
    /// once per worker, since a projection isn't guaranteed thread-safe.
    /// </param>
    /// <param name="cancellationToken">Cancels the pass, for a superseded render.</param>
    /// <returns>The adjusted layer and how many nodes were adjusted.</returns>
    public static (StyledCoverageLayer Layer, int Adjusted) Apply(
        StyledCoverageLayer layer,
        IDepthAdjustment adjustment,
        int? verticalDatum,
        Func<Func<double, double, (double Longitude, double Latitude)>?> createNativeToWgs84,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(adjustment);
        ArgumentNullException.ThrowIfNull(createNativeToWgs84);

        var coverage = layer.Coverage;
        if (!adjustment.Accepts(verticalDatum) || !coverage.Values.TryGetValue(DepthField, out var depths))
        {
            return (layer, 0);
        }

        var grid = coverage.Metadata;
        var georeferencer = layer.Georeferencer;
        var noData = layer.NoDataValue;
        // Rows are independent, and the water level is read at every node, so
        // the pass runs in parallel.
        var adjusted = (float[])depths.Clone();
        var count = 0;
        Parallel.For(
            0,
            grid.NumRows,
            new ParallelOptions { CancellationToken = cancellationToken },
            () => (Count: 0, ToWgs84: createNativeToWgs84()),
            (row, _, local) =>
            {
                var nativeToWgs84 = local.ToWgs84;
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
                    if (adjustment.At(lat, lon, verticalDatum) is { } water)
                    {
                        adjusted[index] = (float)(depth + water.HeightMeters);
                        local.Count++;
                    }
                }

                return local;
            },
            local => Interlocked.Add(ref count, local.Count));

        if (count == 0)
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
