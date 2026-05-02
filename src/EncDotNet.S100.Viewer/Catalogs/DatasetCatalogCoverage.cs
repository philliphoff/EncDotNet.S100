using System.Collections.Immutable;

namespace EncDotNet.S100.Viewer.Catalogs;

/// <summary>
/// Coverage geometry for a <see cref="DatasetCatalogEntry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Coverage is expressed as a single exterior ring of (latitude, longitude)
/// vertices in <c>EPSG:4326</c>, plus a precomputed bounding box for cheap
/// hit-testing. The ring is intentionally simple — full multi-polygon
/// support can be added if a future source needs it.
/// </para>
/// </remarks>
internal sealed record DatasetCatalogCoverage(
    ImmutableArray<(double Latitude, double Longitude)> Ring,
    double MinLatitude,
    double MinLongitude,
    double MaxLatitude,
    double MaxLongitude)
{
    /// <summary>
    /// Builds a coverage record from a ring of (lat, lon) vertices, computing
    /// the bounding box on the fly. Returns <see langword="null"/> when the
    /// ring is empty.
    /// </summary>
    public static DatasetCatalogCoverage? FromRing(
        ImmutableArray<(double Latitude, double Longitude)> ring)
    {
        if (ring.IsDefaultOrEmpty)
            return null;

        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        foreach (var (lat, lon) in ring)
        {
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }

        return new DatasetCatalogCoverage(ring, minLat, minLon, maxLat, maxLon);
    }
}
