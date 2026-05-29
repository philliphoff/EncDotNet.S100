using Mapsui.Projections;

namespace EncDotNet.S100.Renderers.Mapsui.DynamicSources;

/// <summary>
/// Small-angle helpers to translate vessel-local metric offsets into
/// WGS-84 lat/lon (and onward into Spherical Mercator) for renderers
/// that compose hull / arrow / range-ring geometry from a reference
/// position and a few-metres-to-kilometres offset.
/// </summary>
/// <remarks>
/// <para>
/// Adequate for vessel-scale offsets (≲ a few kilometres) at any
/// non-polar latitude. Not intended as a general geodesy primitive —
/// renderers that need true great-circle accuracy should use the
/// geodetic destination helper in
/// <see cref="DefaultDynamicFeatureRenderer"/>.
/// </para>
/// </remarks>
internal static class MercatorOffset
{
    private const double MetresPerDegreeLatitude = 111_320.0;

    /// <summary>
    /// Translates a local-frame offset (east, north) in metres at
    /// reference position (<paramref name="refLatDeg"/>,
    /// <paramref name="refLonDeg"/>) into a new lat/lon pair.
    /// </summary>
    public static (double Lat, double Lon) FromLocalMetres(
        double refLatDeg, double refLonDeg, double eastMetres, double northMetres)
    {
        var dLat = northMetres / MetresPerDegreeLatitude;
        var cosLat = Math.Cos(refLatDeg * Math.PI / 180.0);
        // Guard against cos=0 right at the pole.
        var dLon = cosLat == 0
            ? 0.0
            : eastMetres / (MetresPerDegreeLatitude * cosLat);
        return (refLatDeg + dLat, refLonDeg + dLon);
    }

    /// <summary>
    /// Convenience composition of <see cref="FromLocalMetres"/> and
    /// <see cref="SphericalMercator.FromLonLat(double, double)"/>.
    /// </summary>
    public static (double X, double Y) ToMercator(
        double refLatDeg, double refLonDeg, double eastMetres, double northMetres)
    {
        var (lat, lon) = FromLocalMetres(refLatDeg, refLonDeg, eastMetres, northMetres);
        var m = SphericalMercator.FromLonLat(lon, lat);
        return (m.x, m.y);
    }
}
