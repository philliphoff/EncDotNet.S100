using EncDotNet.S100.DataModel;
using EncDotNet.S100.Rendering.Scene;
using NetTopologySuite.Geometries;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// A chart cell's data-coverage footprint as one NTS geometry in EPSG:3857,
/// built the way the viewer builds it: every coverage area projected, unioned
/// and normalised with a zero-width buffer, so overlapping or self-touching
/// areas form one clean footprint.
/// </summary>
internal static class CoverageFootprint
{
    /// <summary>
    /// Builds the footprint of <paramref name="areas"/>, or <see langword="null"/>
    /// when there is none usable: no ring of three distinct points, an empty
    /// normalised result, or a normalisation that fails.
    /// </summary>
    /// <param name="areas">The cell's coverage areas, in WGS-84.</param>
    public static Geometry? FromAreas(IReadOnlyList<CoverageArea> areas)
    {
        ArgumentNullException.ThrowIfNull(areas);
        if (areas.Count == 0)
            return null;

        var polygons = new List<Polygon>(areas.Count);
        foreach (var area in areas)
        {
            if (ToMercatorRing(area.ExteriorRing) is not { } shell)
                continue;

            LinearRing[]? holes = null;
            if (area.InteriorRings.Count > 0)
            {
                var holeList = new List<LinearRing>(area.InteriorRings.Count);
                foreach (var interior in area.InteriorRings)
                {
                    if (ToMercatorRing(interior) is { } hole)
                        holeList.Add(hole);
                }

                if (holeList.Count > 0)
                    holes = [.. holeList];
            }

            polygons.Add(new Polygon(shell, holes));
        }

        if (polygons.Count == 0)
            return null;

        Geometry geometry = polygons.Count == 1 ? polygons[0] : new MultiPolygon([.. polygons]);
        try
        {
            var normalized = geometry.Buffer(0);
            return normalized.IsEmpty ? null : normalized;
        }
        catch (TopologyException)
        {
            return null;
        }
    }

    /// <summary>
    /// The rings of <paramref name="geometry"/>'s polygons, exteriors and holes
    /// alike, as EPSG:3857 point lists for an even-odd clip path.
    /// </summary>
    /// <param name="geometry">A polygonal geometry.</param>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> Rings(Geometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        var rings = new List<IReadOnlyList<(double X, double Y)>>();
        Add(geometry);
        return rings;

        void Add(Geometry part)
        {
            switch (part)
            {
                case Polygon polygon:
                    rings.Add(ToPoints(polygon.ExteriorRing));
                    foreach (var hole in polygon.InteriorRings)
                        rings.Add(ToPoints(hole));
                    break;
                case GeometryCollection collection:
                    foreach (var child in collection.Geometries)
                        Add(child);
                    break;
            }
        }

        static (double X, double Y)[] ToPoints(LineString ring)
        {
            var coordinates = ring.Coordinates;
            var points = new (double X, double Y)[coordinates.Length];
            for (int i = 0; i < coordinates.Length; i++)
                points[i] = (coordinates[i].X, coordinates[i].Y);
            return points;
        }
    }

    private static LinearRing? ToMercatorRing(IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 3)
            return null;

        var coordinates = new List<Coordinate>(ring.Count + 1);
        foreach (var position in ring)
        {
            var (x, y) = WebMercator.FromLonLat(position.Longitude, position.Latitude);
            coordinates.Add(new Coordinate(x, y));
        }

        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0].Copy());

        return coordinates.Count < 4 ? null : new LinearRing([.. coordinates]);
    }
}
