using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Resolves a vector cell's declared data-coverage polygons from its
/// <c>DataCoverage</c> surface features (S-101 FC §3.1.1; the S-57
/// <c>M_COVR</c> meta-object translated to <c>DataCoverage</c>). The result
/// drives cross-cell scale-band overlap suppression (issue #438 Phase 2).
/// </summary>
/// <remarks>
/// Mapsui-free: works purely on the Mapsui-agnostic <see cref="Feature"/>
/// model and returns EPSG:4326 <see cref="CoverageArea"/> rings, leaving the
/// projection to EPSG:3857 and geometry algebra to the renderer assembly.
/// </remarks>
internal static class CoverageAreaResolver
{
    private const string DataCoverageFeatureType = "DataCoverage";
    private const string CategoryOfCoverageAttribute = "categoryOfCoverage";

    /// <summary>
    /// Extracts the coverage polygons from <paramref name="features"/>: every
    /// <c>DataCoverage</c> surface feature whose <c>categoryOfCoverage</c> is
    /// not "no coverage available" (S-101 / S-57 CATCOV = 2). Returns an empty
    /// list when the cell declares no usable coverage geometry.
    /// </summary>
    /// <param name="features">The cell's resolved vector features (with geometry).</param>
    public static IReadOnlyList<CoverageArea> Resolve(IEnumerable<Feature> features)
    {
        ArgumentNullException.ThrowIfNull(features);

        var areas = new List<CoverageArea>();
        foreach (var feature in features)
        {
            if (!string.Equals(feature.FeatureType, DataCoverageFeatureType, StringComparison.Ordinal))
                continue;
            if (feature.GeometryType != GeometryType.Surface)
                continue;
            // A polygon needs at least three distinct positions.
            if (feature.Coordinates.Count < 3)
                continue;
            if (IsNoCoverage(feature))
                continue;

            areas.Add(new CoverageArea
            {
                ExteriorRing = feature.Coordinates,
                InteriorRings = feature.InteriorRings,
            });
        }

        return areas;
    }

    /// <summary>
    /// Reports whether a <c>DataCoverage</c> feature is explicitly flagged as
    /// "no coverage available" (S-57 M_COVR CATCOV = 2), so it is excluded from
    /// the coverage footprint. Absence of the attribute is treated as coverage
    /// available (the common single-M_COVR case).
    /// </summary>
    private static bool IsNoCoverage(Feature feature)
    {
        if (!feature.Attributes.TryGetValue(CategoryOfCoverageAttribute, out var raw) || raw is null)
            return false;

        var text = raw.ToString();
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (int.TryParse(text, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var code))
        {
            return code == 2;
        }

        return string.Equals(text, "noCoverageAvailable", StringComparison.OrdinalIgnoreCase);
    }
}
