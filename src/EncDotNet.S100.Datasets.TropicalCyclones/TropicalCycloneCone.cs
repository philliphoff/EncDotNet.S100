using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace EncDotNet.S100.Datasets.TropicalCyclones;

/// <summary>
/// An advisory's cone of uncertainty: the area the storm's centre is
/// expected to stay within, as NHC draws it, and that area split at 72 hours.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Surfaces"/> is NHC's polygon, unchanged (design note
/// principle 2: we draw NHC's cone, never one we compute).
/// </para>
/// <para>
/// NHC's GIS files hold only the whole five-day cone, so the split that the
/// design draws differently (days 1 to 3 plain, days 4 and 5 stippled, V2) is
/// made here: the cone is cut by the line through the forecast position at
/// 72 hours, square to the track there. That follows NHC's own graphic for
/// any track that doesn't turn back on itself within the cone. When the
/// forecast stops at or before 72 hours, the whole cone is days 1 to 3.
/// </para>
/// </remarks>
public sealed record TropicalCycloneCone
{
    /// <summary>The hour the cone is split at: 72, the end of day 3.</summary>
    public const int SplitTau = 72;

    /// <summary>NHC's cone, in WGS-84 degrees.</summary>
    public required IReadOnlyList<SurfacePart> Surfaces { get; init; }

    /// <summary>The part of the cone up to 72 hours (days 1 to 3).</summary>
    public required IReadOnlyList<SurfacePart> DaysOneToThree { get; init; }

    /// <summary>The part of the cone after 72 hours (days 4 and 5); empty when the forecast stops by then.</summary>
    public required IReadOnlyList<SurfacePart> DaysFourToFive { get; init; }

    /// <summary>
    /// Builds the cone of <paramref name="surfaces"/> for a forecast, splitting
    /// it at 72 hours as the remarks describe.
    /// </summary>
    /// <param name="surfaces">NHC's cone polygon.</param>
    /// <param name="forecast">The advisory's forecast points, in tau order.</param>
    public static TropicalCycloneCone Create(IReadOnlyList<SurfacePart> surfaces, IReadOnlyList<TropicalCycloneForecastPoint> forecast)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(forecast);
        var whole = new TropicalCycloneCone { Surfaces = surfaces, DaysOneToThree = surfaces, DaysFourToFive = [] };
        if (surfaces.Count == 0 || !forecast.Any(p => p.Tau > SplitTau) || !forecast.Any(p => p.Tau < SplitTau))
            return whole;

        var before = forecast.Last(p => p.Tau <= SplitTau);
        var after = forecast.First(p => p.Tau > SplitTau);
        var at = before.Tau == SplitTau
            ? before.Position
            : Interpolate(before.Position, after.Position, (double)(SplitTau - before.Tau) / (after.Tau - before.Tau));

        // The track's direction through the split: from the point before to
        // the point after, in a local frame where a degree of longitude is
        // shortened by the cosine of the latitude.
        var from = before.Tau == SplitTau ? forecast.LastOrDefault(p => p.Tau < SplitTau) ?? before : before;
        var cos = Math.Cos(at.Latitude * Math.PI / 180);
        var dx = Unwrap(after.Position.Longitude, from.Position.Longitude) - from.Position.Longitude;
        var dy = after.Position.Latitude - from.Position.Latitude;
        dx *= cos;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 1e-9 || cos < 1e-6)
            return whole;
        dx /= length;
        dy /= length;

        try
        {
            var factory = new GeometryFactory();
            var cone = ToGeometry(factory, surfaces, at.Longitude);
            if (cone.IsEmpty)
                return whole;

            // A half-plane ahead of the 72 h position, made finite: 40 degrees
            // wide either side of the track and 40 degrees ahead.
            const double reach = 40;
            Coordinate Local(double along, double across) => new(
                at.Longitude + (((along * dx) - (across * dy)) / cos),
                at.Latitude + (along * dy) + (across * dx));
            var ahead = factory.CreatePolygon(
            [
                Local(0, -reach), Local(reach, -reach), Local(reach, reach), Local(0, reach), Local(0, -reach),
            ]);

            var late = cone.Intersection(ahead);
            var early = cone.Difference(ahead);
            return new TropicalCycloneCone
            {
                Surfaces = surfaces,
                DaysOneToThree = FromGeometry(early),
                DaysFourToFive = FromGeometry(late),
            };
        }
        catch (Exception ex) when (ex is TopologyException or ArgumentException or AggregateException)
        {
            // A cone NTS can't cut is still drawn, unsplit.
            return whole;
        }
    }

    private static GeoPosition Interpolate(GeoPosition a, GeoPosition b, double fraction) =>
        new(
            a.Latitude + ((b.Latitude - a.Latitude) * fraction),
            a.Longitude + ((Unwrap(b.Longitude, a.Longitude) - a.Longitude) * fraction));

    // The longitude equal to `longitude` within 180 degrees of `reference`,
    // so a cone across the antimeridian is cut in one piece.
    private static double Unwrap(double longitude, double reference)
    {
        while (longitude - reference > 180)
            longitude -= 360;
        while (longitude - reference < -180)
            longitude += 360;
        return longitude;
    }

    private static Geometry ToGeometry(GeometryFactory factory, IReadOnlyList<SurfacePart> surfaces, double reference)
    {
        LinearRing Ring(IReadOnlyList<GeoPosition> ring)
        {
            var coordinates = new Coordinate[ring.Count];
            var previous = reference;
            for (var i = 0; i < ring.Count; i++)
            {
                var lon = Unwrap(ring[i].Longitude, previous);
                coordinates[i] = new Coordinate(lon, ring[i].Latitude);
                previous = lon;
            }

            if (!coordinates[0].Equals2D(coordinates[^1]))
                coordinates = [.. coordinates, coordinates[0].Copy()];
            return factory.CreateLinearRing(coordinates);
        }

        var polygons = surfaces
            .Where(s => s.ExteriorRing.Count >= 3)
            .Select(s => (Geometry)factory.CreatePolygon(Ring(s.ExteriorRing), [.. s.InteriorRings.Where(h => h.Count >= 3).Select(Ring)]).Buffer(0))
            .ToArray();
        return polygons.Length switch
        {
            0 => factory.CreatePolygon(),
            1 => polygons[0],
            _ => UnaryUnionOp.Union(polygons),
        };
    }

    private static IReadOnlyList<SurfacePart> FromGeometry(Geometry geometry)
    {
        static IReadOnlyList<GeoPosition> Ring(LineString ring) =>
            [.. ring.Coordinates.Select(c => new GeoPosition(c.Y, c.X))];

        var parts = new List<SurfacePart>();
        for (var i = 0; i < geometry.NumGeometries; i++)
        {
            if (geometry.GetGeometryN(i) is Polygon { IsEmpty: false, Area: > 0 } polygon)
                parts.Add(new SurfacePart(Ring(polygon.ExteriorRing), [.. polygon.InteriorRings.Select(Ring)]));
        }

        return parts;
    }
}
