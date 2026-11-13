using EncDotNet.S100.DataModel;
using EncDotNet.S100.Tides.Stations;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate;

namespace EncDotNet.S100.Viewer.Services.Tides;

/// <summary>
/// A tide station's cell (tide predictions design D13): where it's the
/// nearest predictable station, within its 10 km reach. Shown only when the
/// station is picked.
/// </summary>
internal static class TideStationCell
{
    /// <summary>The station's reach (D19): 10 km.</summary>
    public const double ReachMeters = 10_000;

    /// <summary>The cell's outline, or null when it can't be made.</summary>
    /// <param name="catalogue">The station list.</param>
    /// <param name="station">The station.</param>
    /// <param name="predictable">Whether another station can be predicted, so it has a cell of its own.</param>
    /// <returns>The ring, in WGS-84.</returns>
    public static IReadOnlyList<GeoPosition>? Outline(
        TideStationCatalogue catalogue,
        TideStationEntry station,
        Func<string, bool> predictable)
    {
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(station);
        ArgumentNullException.ThrowIfNull(predictable);
        var factory = new GeometryFactory();
        var centre = Project(station.Position);

        // A 10 km disc: Web Mercator stretches distance by 1 / cos(latitude).
        var scale = 1 / Math.Cos(station.Position.Latitude * Math.PI / 180);
        var disc = factory.CreatePoint(centre).Buffer(ReachMeters * scale, 24);

        var neighbours = catalogue.FindNearest(station.Position, maxCount: 40, maxDistanceMeters: 2 * ReachMeters)
            .Where(m => m.Station.Id != station.Id && predictable(m.Station.Id))
            .Select(m => Project(m.Station.Position))
            .Where(c => !c.Equals2D(centre))
            .ToList();
        Geometry cell = disc;
        if (neighbours.Count > 0)
        {
            var builder = new VoronoiDiagramBuilder();
            builder.SetSites([centre, .. neighbours]);
            builder.ClipEnvelope = disc.EnvelopeInternal;
            var diagram = builder.GetDiagram(factory);
            for (var i = 0; i < diagram.NumGeometries; i++)
            {
                var polygon = diagram.GetGeometryN(i);
                if (polygon.Covers(factory.CreatePoint(centre)))
                {
                    cell = polygon.Intersection(disc);
                    break;
                }
            }
        }

        if (cell is not Polygon { IsEmpty: false } result)
            return null;

        return [.. result.ExteriorRing.Coordinates.Select(c =>
        {
            var (lon, lat) = Mapsui.Projections.SphericalMercator.ToLonLat(c.X, c.Y);
            return new GeoPosition(lat, lon);
        })];
    }

    private static Coordinate Project(GeoPosition position)
    {
        var (x, y) = Mapsui.Projections.SphericalMercator.FromLonLat(position.Longitude, position.Latitude);
        return new Coordinate(x, y);
    }
}
