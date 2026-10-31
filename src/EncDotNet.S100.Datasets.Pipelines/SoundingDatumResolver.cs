using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A vector cell's sounding datum: the <c>verticalDatum</c> of its
/// <c>SoundingDatum</c> meta features (S-101 Ed 2.0.0 §5.1 NOTE and DCEG
/// §3.9), the datum its depths and soundings are referred to.
/// </summary>
/// <remarks>
/// S-101 has no dataset-level sounding datum: the vertical CRS in the CRS
/// record isn't used for it (S-101 Ed 2.0.0 §5.3 NOTE). <c>VerticalDatumOfData</c>
/// (DCEG §3.10) is the datum of heights and vertical clearances, not of
/// depths, so it isn't read here. A translated S-57 cell carries one
/// <c>SoundingDatum</c> per coverage from DSPM SDAT when it has no M_SDAT.
/// </remarks>
internal sealed class SoundingDatumResolver
{
    private const string SoundingDatumFeatureType = "SoundingDatum";
    private const string VerticalDatumAttribute = "verticalDatum";

    private readonly List<(CoverageArea Area, int Code)> _areas = new();
    private readonly int? _cellDefault;

    /// <summary>Collects the cell's <c>SoundingDatum</c> surfaces.</summary>
    /// <param name="features">The cell's resolved features, with geometry.</param>
    public SoundingDatumResolver(IEnumerable<Feature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        var codes = new HashSet<int>();
        foreach (var feature in features)
        {
            if (!string.Equals(feature.FeatureType, SoundingDatumFeatureType, StringComparison.Ordinal)
                || !TryReadCode(feature, out var code))
            {
                continue;
            }

            codes.Add(code);
            if (feature.GeometryType == GeometryType.Surface && feature.Coordinates.Count >= 3)
            {
                _areas.Add((new CoverageArea { ExteriorRing = feature.Coordinates, InteriorRings = feature.InteriorRings }, code));
            }
        }

        // One code across the whole cell is its default, for a point just off
        // a meta feature's ring (or a SoundingDatum without surface geometry).
        _cellDefault = codes.Count == 1 ? codes.First() : null;
    }

    /// <summary>
    /// The sounding datum at a point as an S-100 code: the <c>SoundingDatum</c>
    /// area containing it, else the cell's only declared datum, else <c>null</c>.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <returns>The datum code, or <c>null</c> when the cell doesn't declare one there.</returns>
    public int? At(double latitude, double longitude)
    {
        foreach (var (area, code) in _areas)
        {
            if (area.Contains(latitude, longitude))
            {
                return code;
            }
        }

        return _cellDefault;
    }

    private static bool TryReadCode(Feature feature, out int code)
    {
        code = 0;
        if (!feature.Attributes.TryGetValue(VerticalDatumAttribute, out var raw) || raw is null)
        {
            return false;
        }

        return raw switch
        {
            int i => (code = i) > 0,
            long l and > 0 and <= int.MaxValue => (code = (int)l) > 0,
            _ => int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out code) && code > 0,
        };
    }
}
