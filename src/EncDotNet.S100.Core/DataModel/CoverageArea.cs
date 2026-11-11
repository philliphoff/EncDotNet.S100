namespace EncDotNet.S100.DataModel;

/// <summary>
/// A single polygonal region of a dataset's declared data coverage, expressed
/// in WGS-84 (EPSG:4326). Corresponds to one <c>DataCoverage</c> surface
/// feature (S-101 FC §3.1.1; the S-57 <c>M_COVR</c> meta-object translated to
/// <c>DataCoverage</c>), whose exterior ring bounds the covered area and whose
/// interior rings punch no-coverage holes.
/// </summary>
/// <remarks>
/// Used to drive cross-cell scale-band overlap suppression (issue #438
/// Phase 2): a coarser cell is clipped to <em>its</em> coverage minus the union
/// of finer overlapping cells' coverage, so overlapping multi-scale cells do
/// not double-draw. Holes are honoured so a coarser cell still shows through a
/// finer cell's no-coverage gaps.
/// </remarks>
public sealed class CoverageArea
{
    /// <summary>
    /// The exterior ring bounding the covered area, in EPSG:4326
    /// (lat/lon per S-100 Part 10b §6.2). Expected to be a closed, non-empty
    /// ring of at least three distinct positions.
    /// </summary>
    public required IReadOnlyList<GeoPosition> ExteriorRing { get; init; }

    /// <summary>
    /// Interior rings (holes) subtracted from <see cref="ExteriorRing"/> —
    /// no-coverage gaps within the covered area. Empty when the region is
    /// solid.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings { get; init; } = [];

    /// <summary>
    /// Whether a WGS-84 point lies inside the covered area: inside
    /// <see cref="ExteriorRing"/> and outside every interior ring. Uses an
    /// even-odd ray cast in longitude/latitude, so it suits cell-sized areas
    /// that don't cross the antimeridian.
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees.</param>
    /// <param name="longitude">Longitude in decimal degrees.</param>
    /// <returns><see langword="true"/> when the point is covered.</returns>
    public bool Contains(double latitude, double longitude)
    {
        if (!InRing(ExteriorRing, latitude, longitude))
            return false;

        foreach (var hole in InteriorRings)
        {
            if (InRing(hole, latitude, longitude))
                return false;
        }

        return true;
    }

    private static bool InRing(IReadOnlyList<GeoPosition> ring, double latitude, double longitude)
    {
        var inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Latitude > latitude) != (b.Latitude > latitude)
                && longitude < (b.Longitude - a.Longitude) * (latitude - a.Latitude) / (b.Latitude - a.Latitude) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
