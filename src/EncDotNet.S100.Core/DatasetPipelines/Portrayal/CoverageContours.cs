using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.Pipelines.Portrayal;

/// <summary>
/// Turns the value-based <see cref="ContourStyle"/>s of an
/// <see cref="ICoverageStyle"/> into <see cref="Isoline"/>s over a sampled
/// coverage, ready for an <see cref="IsolineCoverageSubLayer"/>, which both
/// renderers draw (lines of constant pixel width with labels along them).
/// </summary>
/// <remarks>
/// <para>
/// The lines are traced with marching squares (<see cref="IsolineTracer"/>)
/// through the field's values in the <see cref="StyledCoverageLayer"/> that
/// <see cref="CoveragePipeline"/> sampled, so they follow the viewport subset
/// and stride the pipeline chose. No-data nodes break the lines.
/// </para>
/// <para>
/// Only contours whose <see cref="ContourStyle.Value"/> lies within the
/// sample's range are traced, so a style can list every level it might ever
/// need (every 4 hPa from 860 to 1100, say) at no cost.
/// </para>
/// </remarks>
public static class CoverageContours
{
    /// <summary>
    /// Traces each of <paramref name="contours"/> through the
    /// <paramref name="fieldName"/> values of <paramref name="layer"/>.
    /// </summary>
    /// <param name="layer">The sampled coverage, from <see cref="CoveragePipeline.ProcessAsync"/>.</param>
    /// <param name="fieldName">The field the contours are traced on; its unit is the contours' <see cref="ContourStyle.Unit"/>.</param>
    /// <param name="contours">The contours, e.g. <see cref="ICoverageStyle.Contours"/>.</param>
    /// <param name="formatLabel">
    /// The label of a contour with <see cref="ContourStyle.ShowLabel"/> set;
    /// the value in the invariant culture when <see langword="null"/>. A
    /// contour without <see cref="ContourStyle.ShowLabel"/> gets an empty label
    /// and is drawn unlabelled.
    /// </param>
    /// <param name="nativeToWgs84">
    /// Converts the grid's native coordinates (x, y) to WGS-84 (longitude,
    /// latitude), or <see langword="null"/> when the grid is geographic
    /// (EPSG:4326), as weather grids are.
    /// </param>
    /// <returns>The isolines, in EPSG:3857 metres, in the order of <paramref name="contours"/>.</returns>
    /// <exception cref="KeyNotFoundException">The sample has no field called <paramref name="fieldName"/>.</exception>
    public static IReadOnlyList<Isoline> Trace(
        StyledCoverageLayer layer,
        string fieldName,
        IReadOnlyList<ContourStyle> contours,
        Func<ContourStyle, string>? formatLabel = null,
        Func<double, double, (double Longitude, double Latitude)>? nativeToWgs84 = null)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);
        ArgumentNullException.ThrowIfNull(contours);
        if (contours.Count == 0)
            return [];

        var grid = layer.Coverage.Metadata;
        int rows = grid.NumRows, columns = grid.NumColumns;
        var values = layer.Coverage.GetValues(fieldName);

        float noData = layer.NoDataValue;
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        foreach (float value in values)
        {
            if (float.IsNaN(value) || value == noData)
                continue;
            minimum = Math.Min(minimum, value);
            maximum = Math.Max(maximum, value);
        }

        if (!double.IsFinite(minimum))
            return [];

        // Contours by level, keeping the first style given for a level.
        var byLevel = new Dictionary<double, ContourStyle>();
        var levels = new List<double>();
        foreach (var contour in contours)
        {
            ArgumentNullException.ThrowIfNull(contour, nameof(contours));
            if (contour.Value < minimum || contour.Value > maximum || !byLevel.TryAdd(contour.Value, contour))
                continue;
            levels.Add(contour.Value);
        }

        if (levels.Count == 0)
            return [];

        var traced = IsolineTracer.Trace(values, rows, columns, noData, levels);
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
                (mercator[i], mercator[i + 1]) = ToMercator(longitude, latitude);
            }

            var style = byLevel[line.Level];
            result.Add(new Isoline
            {
                Level = line.Level,
                Label = !style.ShowLabel
                    ? string.Empty
                    : formatLabel?.Invoke(style) ?? style.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                MercatorCoordinates = mercator,
            });
        }

        return result;
    }

    // EPSG:3857, as EncDotNet.S100.Rendering.Scene.WebMercator.FromLonLat, which
    // Core doesn't reference. Latitudes are clamped to the projection's limit.
    private static (double X, double Y) ToMercator(double longitude, double latitude)
    {
        const double earthRadius = 6378137.0;
        const double maxLatitude = 85.05112878;
        double lat = Math.Clamp(latitude, -maxLatitude, maxLatitude) * Math.PI / 180.0;
        return (earthRadius * longitude * Math.PI / 180.0, earthRadius * Math.Log(Math.Tan(Math.PI / 4.0 + lat / 2.0)));
    }
}
