using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// Computes a per-cell "land" mask for a gridded coverage surface so a renderer
/// can suppress the surface where it would otherwise paint over land.
/// </summary>
/// <remarks>
/// <para>
/// Used by the S-98 interoperability rule that clips the (non-normative) S-104
/// water-level surface to water areas when an S-101 ENC is loaded alongside it
/// (issue #483): the surface is layered like S-102 gridded bathymetry — under
/// ENC line work — and must not bleed over land. The land polygons come from
/// the S-101 <c>LandArea</c> features (WGS84, <c>(latitude, longitude)</c>).
/// </para>
/// <para>
/// The mask is evaluated at each grid cell's centre in the coverage's native
/// CRS: land polygons are reprojected WGS84 → native once, then a standard
/// even–odd ray-cast point-in-polygon test (honouring interior rings, which are
/// water — e.g. an inland lake) decides each cell. A per-polygon bounding-box
/// pre-check keeps the common case (a coastline touching a corner of the grid)
/// cheap.
/// </para>
/// </remarks>
public static class CoverageLandMask
{
    /// <summary>
    /// Builds a row-major <c>bool[rows * cols]</c> where <see langword="true"/>
    /// marks a cell whose centre falls on land and should therefore be hidden.
    /// </summary>
    /// <param name="georeferencer">Grid georeferencer (native CRS + affine parameters).</param>
    /// <param name="rows">Grid row count (matches the coverage field height).</param>
    /// <param name="cols">Grid column count (matches the coverage field width).</param>
    /// <param name="landAreas">
    /// Land-area surface geometries in WGS84 <c>(latitude, longitude)</c>; only
    /// <see cref="GeometryType.Surface"/> entries contribute. Curves and points
    /// are ignored.
    /// </param>
    /// <param name="wgs84ToNative">
    /// Transform from WGS84 to the grid's native CRS, applied as
    /// <c>Transform(longitude, latitude)</c>. Pass an identity transform when the
    /// grid is already geographic (EPSG:4326), the common S-104 case.
    /// </param>
    /// <returns>
    /// The mask, or <see langword="null"/> when there is nothing to clip (no
    /// surface land areas, or a degenerate grid) so callers can skip the work.
    /// </returns>
    public static bool[]? Compute(
        GridGeoreferencer georeferencer,
        int rows,
        int cols,
        IReadOnlyList<FeatureGeometry> landAreas,
        ICrsTransform wgs84ToNative)
    {
        ArgumentNullException.ThrowIfNull(georeferencer);
        ArgumentNullException.ThrowIfNull(landAreas);
        ArgumentNullException.ThrowIfNull(wgs84ToNative);

        if (rows <= 0 || cols <= 0 || landAreas.Count == 0)
        {
            return null;
        }

        var polygons = new List<NativePolygon>(landAreas.Count);
        foreach (var area in landAreas)
        {
            if (area.Type != GeometryType.Surface || area.Coordinates.Count < 3)
            {
                continue;
            }

            var exterior = ToNativeRing(area.Coordinates, wgs84ToNative);
            if (exterior.Length < 3)
            {
                continue;
            }

            var holes = new List<double[]>(area.InteriorRings.Count);
            foreach (var ring in area.InteriorRings)
            {
                if (ring.Count < 3)
                {
                    continue;
                }

                var hole = ToNativeRing(ring, wgs84ToNative);
                if (hole.Length >= 3)
                {
                    holes.Add(hole);
                }
            }

            polygons.Add(new NativePolygon(exterior, holes));
        }

        if (polygons.Count == 0)
        {
            return null;
        }

        var mask = new bool[rows * cols];
        bool any = false;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                var (x, y) = georeferencer.ToNative(row, col);

                foreach (var polygon in polygons)
                {
                    if (x < polygon.MinX || x > polygon.MaxX ||
                        y < polygon.MinY || y > polygon.MaxY)
                    {
                        continue;
                    }

                    if (!PointInRing(polygon.Exterior, x, y))
                    {
                        continue;
                    }

                    bool inHole = false;
                    foreach (var hole in polygon.Holes)
                    {
                        if (PointInRing(hole, x, y))
                        {
                            inHole = true;
                            break;
                        }
                    }

                    if (!inHole)
                    {
                        mask[row * cols + col] = true;
                        any = true;
                        break;
                    }
                }
            }
        }

        return any ? mask : null;
    }

    // Ring stored as a flat [x0, y0, x1, y1, ...] array to avoid per-vertex
    // allocations across a potentially large coastline.
    private static double[] ToNativeRing(IReadOnlyList<GeoPosition> ring, ICrsTransform transform)
    {
        var flat = new double[ring.Count * 2];
        bool identity = transform.IsIdentity;
        for (int i = 0; i < ring.Count; i++)
        {
            var p = ring[i];
            double x, y;
            if (identity)
            {
                x = p.Longitude;
                y = p.Latitude;
            }
            else
            {
                (x, y) = transform.Transform(p.Longitude, p.Latitude);
            }

            flat[i * 2] = x;
            flat[i * 2 + 1] = y;
        }

        return flat;
    }

    // Standard even–odd ray-cast against a flat [x,y,...] ring.
    private static bool PointInRing(double[] ring, double x, double y)
    {
        bool inside = false;
        int count = ring.Length / 2;
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            double xi = ring[i * 2], yi = ring[i * 2 + 1];
            double xj = ring[j * 2], yj = ring[j * 2 + 1];

            bool straddles = (yi > y) != (yj > y);
            if (straddles)
            {
                double xCross = (xj - xi) * (y - yi) / (yj - yi) + xi;
                if (x < xCross)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private readonly struct NativePolygon
    {
        public NativePolygon(double[] exterior, IReadOnlyList<double[]> holes)
        {
            Exterior = exterior;
            Holes = holes;

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;
            for (int i = 0; i < exterior.Length; i += 2)
            {
                double x = exterior[i], y = exterior[i + 1];
                if (x < minX) minX = x;
                if (x > maxX) maxX = x;
                if (y < minY) minY = y;
                if (y > maxY) maxY = y;
            }

            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public double[] Exterior { get; }
        public IReadOnlyList<double[]> Holes { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
    }
}
