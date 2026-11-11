using EncDotNet.S100.DataModel;
using EncDotNet.S100.Navigation;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Reads a chart cell's vertical datum from its <c>SoundingDatum</c>
/// meta-features (S-101 FC; the S-57 <c>M_SDAT</c> meta-object translated to
/// <c>SoundingDatum</c>). Depths and soundings in the area of a
/// <c>SoundingDatum</c> are relative to its <c>verticalDatum</c>, an S-100
/// vertical datum code.
/// </summary>
/// <remarks>
/// Shared by the route check's charted-depth source, the pick report's depth
/// card and the viewer's depth basis indicator, so all three read the same
/// datum for the same cell.
/// </remarks>
internal static class SoundingDatumResolver
{
    /// <summary>The meta-feature's type.</summary>
    public const string FeatureType = "SoundingDatum";

    private const string VerticalDatumAttribute = "verticalDatum";

    /// <summary>
    /// Reads the <c>verticalDatum</c> code of a <c>SoundingDatum</c> surface
    /// feature.
    /// </summary>
    /// <param name="feature">Any feature; others return <see langword="false"/>.</param>
    /// <param name="code">The S-100 vertical datum code.</param>
    /// <returns><see langword="true"/> when the feature is a <c>SoundingDatum</c> surface with a code.</returns>
    public static bool TryReadCode(Feature feature, out int code)
    {
        ArgumentNullException.ThrowIfNull(feature);
        code = 0;
        if (feature.GeometryType != GeometryType.Surface
            || !string.Equals(feature.FeatureType, FeatureType, StringComparison.Ordinal)
            || !feature.Attributes.TryGetValue(VerticalDatumAttribute, out var raw)
            || !ChartedDepthResolver.TryReadMeters(raw, out var value)
            || !double.IsFinite(value))
        {
            return false;
        }

        code = (int)value;
        return true;
    }

    /// <summary>Extracts every <c>SoundingDatum</c> area of a cell with its code.</summary>
    /// <param name="features">The cell's resolved vector features (with geometry).</param>
    /// <returns>The areas, in feature order; empty when the cell declares none.</returns>
    public static IReadOnlyList<SoundingDatumArea> Resolve(IEnumerable<Feature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var areas = new List<SoundingDatumArea>();
        foreach (var feature in features)
        {
            if (feature.Coordinates.Count >= 3 && TryReadCode(feature, out var code))
            {
                areas.Add(new SoundingDatumArea(
                    new CoverageArea { ExteriorRing = feature.Coordinates, InteriorRings = feature.InteriorRings },
                    code));
            }
        }

        return areas;
    }

    /// <summary>The datum of the area containing a WGS-84 point, or <see langword="null"/> when none does.</summary>
    /// <param name="areas">The cell's areas (<see cref="Resolve"/>).</param>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    /// <returns>The datum code, or <see langword="null"/>.</returns>
    public static int? At(IReadOnlyList<SoundingDatumArea> areas, double latitude, double longitude)
    {
        ArgumentNullException.ThrowIfNull(areas);
        foreach (var area in areas)
        {
            if (area.Area.Contains(latitude, longitude))
            {
                return area.Code;
            }
        }

        return null;
    }

    /// <summary>
    /// The cell's datum: the code its <c>SoundingDatum</c> areas declare, or
    /// when they declare several, the one declared most often (the lowest code
    /// on a tie, so the answer is stable). <see langword="null"/> when the cell
    /// declares none.
    /// </summary>
    /// <param name="areas">The cell's areas (<see cref="Resolve"/>).</param>
    /// <returns>The datum code, or <see langword="null"/>.</returns>
    public static int? Predominant(IReadOnlyList<SoundingDatumArea> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        return areas.Count == 0
            ? null
            : areas.GroupBy(a => a.Code)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key)
                .First().Key;
    }
}

/// <summary>One <c>SoundingDatum</c> area and the vertical datum code it declares.</summary>
/// <param name="Area">The area's polygon, in degrees.</param>
/// <param name="Code">The S-100 vertical datum code.</param>
internal sealed record SoundingDatumArea(CoverageArea Area, int Code);
