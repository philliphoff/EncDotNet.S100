using EncDotNet.S100.DataModel;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// Projects geographic rings and polylines to EPSG:3857 so that edges crossing
/// the seam of their longitude frame (the ±180° antimeridian, or 0°/360° in a
/// 0…360 frame) are not drawn the long way round, and so that a ring enclosing
/// a pole is drawn as a polar cap rather than as a wedge (issue #760).
/// </summary>
/// <remarks>
/// <para>Each edge of a geographic ring takes the shorter way round the globe.
/// Projecting the vertices one by one breaks this in two cases:</para>
/// <list type="bullet">
/// <item><description>An edge that crosses the frame's seam (e.g. 359.9° →
/// 0.1°) is drawn as a stripe across the world.</description></item>
/// <item><description>A ring around a pole has no edge back to its start in a
/// planar frame: its longitudes run once round the globe, so the projected
/// polygon collapses into a wedge.</description></item>
/// </list>
/// <para>Longitudes are first <em>unwrapped</em>: each vertex is shifted by a
/// multiple of 360° so it lies within 180° of the vertex before it. A ring
/// whose unwrapped longitudes run once round the globe encloses a pole, and is
/// closed along it.</para>
/// <para>The result is then kept within the ring's own 360° longitude frame
/// (<c>[0°, 360°]</c> for a 0…360 ring, <c>[−180°, 180°]</c> otherwise) so it
/// lines up with the rest of a dataset kept in that frame: anything that
/// leaves the frame is clipped at its edge and the rest is wrapped back in. A
/// ring wholly within its frame (the overwhelming majority, including data
/// kept in a continuous frame such as the US NWS S-411 product, ~175°E →
/// ~225°E, issue #413) projects exactly as before.</para>
/// <para>Rings and polylines are also clipped to the Web-Mercator latitude
/// limit (±85.05°), beyond which northing diverges.</para>
/// </remarks>
internal static class AntimeridianGeometry
{
    private static readonly GeometryFactory Factory = new();

    /// <summary>
    /// Projects a surface (an exterior ring plus its holes) to one or more
    /// EPSG:3857 polygons. A surface within its frame yields one polygon; one
    /// clipped at the frame's edge, or a polar cap, may yield several.
    /// </summary>
    public static IReadOnlyList<(IReadOnlyList<(double X, double Y)> Shell,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> Holes)> ProjectSurface(
        IReadOnlyList<GeoPosition> shell,
        IReadOnlyList<IReadOnlyList<GeoPosition>> holes)
    {
        double west = WindowWest(shell);
        if (IsPlain(shell, west) && holes.All(h => h.Count < 3 || IsPlain(h, west)))
        {
            // The common case: nothing crosses a seam or the latitude limit.
            var plainHoles = new List<IReadOnlyList<(double X, double Y)>>(holes.Count);
            foreach (var hole in holes)
            {
                if (hole.Count >= 3)
                    plainHoles.Add(Project(hole));
            }

            return [(Project(shell), plainHoles)];
        }

        var unwrappedShell = Unwrap(shell);
        double winding = unwrappedShell[^1].Lon - unwrappedShell[0].Lon
            + (IsClosed(shell) ? 0 : Wrap180(shell[0].Longitude - shell[^1].Longitude));
        bool polar = Math.Abs(winding) > 180.0;

        var unwrappedHoles = new List<(double Lon, double Lat)[]>(holes.Count);
        double shellMid = MidLongitude(unwrappedShell);
        foreach (var hole in holes)
        {
            if (hole.Count < 3)
                continue;
            var unwrapped = Unwrap(hole);
            // Keep each hole in the same longitude frame as its shell.
            ShiftNear(unwrapped, shellMid);
            unwrappedHoles.Add(unwrapped);
        }

        if (polar || !WithinWindow(unwrappedShell, west))
        {
            var ring = polar ? CloseAlongPole(shell, unwrappedShell) : ToCoordinates(unwrappedShell);
            var clipped = ClipToWindow(ring, unwrappedHoles, west);
            if (clipped is not null)
                return clipped;
        }

        var clippedShell = ClipRingToMercatorLimit(unwrappedShell);
        if (clippedShell.Length < 3)
            return [];

        var projectedHoles = new List<IReadOnlyList<(double X, double Y)>>(unwrappedHoles.Count);
        foreach (var hole in unwrappedHoles)
        {
            var clipped = ClipRingToMercatorLimit(hole);
            if (clipped.Length >= 3)
                projectedHoles.Add(Project(clipped));
        }

        return [(Project(clippedShell), projectedHoles)];
    }

    /// <summary>
    /// Projects a polyline to one or more EPSG:3857 polylines. The polyline is
    /// unwrapped so it never jumps across the world; where it leaves its own
    /// 360° frame (e.g. the outline of a polar ring) it is split at the frame's
    /// edge and continues from the opposite edge.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> ProjectPolyline(
        IReadOnlyList<GeoPosition> coords)
    {
        double west = WindowWest(coords);
        if (IsPlain(coords, west))
            return [Project(coords)];

        var unwrapped = Unwrap(coords);
        bool within = WithinWindow(unwrapped, west);

        var pieces = new List<IReadOnlyList<(double X, double Y)>>();
        foreach (var run in ClipPolylineToMercatorLimit(unwrapped))
        {
            if (within)
                pieces.Add(Project(run));
            else
                SplitAtWindow(run, west, pieces);
        }

        return pieces;
    }

    // Closes a ring round a pole along the pole itself: up from its last
    // vertex, across the top (or bottom) of the frame, and down to its first.
    private static List<Coordinate> CloseAlongPole(
        IReadOnlyList<GeoPosition> shell, (double Lon, double Lat)[] unwrappedShell)
    {
        double latSum = 0;
        foreach (var (_, lat) in unwrappedShell)
            latSum += lat;
        double poleLat = latSum >= 0 ? 90.0 : -90.0;

        var first = unwrappedShell[0];
        var ring = ToCoordinates(unwrappedShell);
        if (!IsClosed(shell))
        {
            // Walk the closing edge too, ending one turn round from the start.
            var tail = unwrappedShell[^1];
            ring.Add(new Coordinate(tail.Lon + Wrap180(first.Lon - tail.Lon), first.Lat));
        }

        var last = ring[^1];
        ring.Add(new Coordinate(last.X, poleLat));
        ring.Add(new Coordinate(first.Lon, poleLat));
        ring.Add(new Coordinate(first.Lon, first.Lat));
        return ring;
    }

    // Clips a surface to the window [west, west + 360°] × the Web-Mercator
    // latitude band, wrapping whatever lies outside the window back into it.
    // Returns null when the overlay fails, so the caller falls back to the
    // plain projection rather than dropping the area.
    private static IReadOnlyList<(IReadOnlyList<(double X, double Y)> Shell,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> Holes)>? ClipToWindow(
        List<Coordinate> shell, List<(double Lon, double Lat)[]> holes, double west)
    {
        var window = Factory.ToGeometry(new Envelope(
            west, west + 360.0, -WebMercator.MaxLatitude, WebMercator.MaxLatitude));

        try
        {
            if (!shell[0].Equals2D(shell[^1]))
                shell.Add(shell[0].Copy());
            Geometry surface = Factory.CreatePolygon(
                Factory.CreateLinearRing([.. shell]),
                [.. holes.Select(h => Factory.CreateLinearRing([.. ClosedCoordinates(h)]))]);
            if (!surface.IsValid)
                surface = surface.Buffer(0);

            // Copies one and two worlds east and west cover whatever part of
            // the surface lies outside the window; their union removes the
            // joins between the pieces.
            var pieces = new List<Geometry>();
            for (int k = -2; k <= 2; k++)
            {
                var copy = Shift(surface, 360.0 * k);
                if (!copy.EnvelopeInternal.Intersects(window.EnvelopeInternal))
                    continue;
                var clipped = copy.Intersection(window);
                if (!clipped.IsEmpty)
                    pieces.Add(clipped);
            }

            var merged = UnaryUnionOp.Union(pieces);
            if (merged is null || merged.IsEmpty)
                return null;

            var result = new List<(IReadOnlyList<(double X, double Y)>, IReadOnlyList<IReadOnlyList<(double X, double Y)>>)>();
            for (int i = 0; i < merged.NumGeometries; i++)
            {
                if (merged.GetGeometryN(i) is not Polygon polygon || polygon.IsEmpty)
                    continue;
                var projectedHoles = new List<IReadOnlyList<(double X, double Y)>>(polygon.NumInteriorRings);
                for (int h = 0; h < polygon.NumInteriorRings; h++)
                    projectedHoles.Add(Project(polygon.GetInteriorRingN(h)));
                result.Add((Project(polygon.ExteriorRing), projectedHoles));
            }

            return result.Count > 0 ? result : null;
        }
        catch (TopologyException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Splits a polyline into pieces lying in the window [west, west + 360°]:
    // the first vertex is shifted into the window, and each time the line
    // leaves it the piece ends on the window edge and the next starts on the
    // opposite edge.
    private static void SplitAtWindow(
        List<(double Lon, double Lat)> line, double west, List<IReadOnlyList<(double X, double Y)>> pieces)
    {
        double east = west + 360.0;
        double shift = -360.0 * Math.Floor((line[0].Lon - west) / 360.0);
        var (prevLon, prevLat) = (line[0].Lon + shift, line[0].Lat);
        var piece = new List<(double X, double Y)> { WebMercator.FromLonLat(prevLon, prevLat) };
        for (int i = 1; i < line.Count; i++)
        {
            var (lon, lat) = (line[i].Lon + shift, line[i].Lat);
            while (lon > east || lon < west)
            {
                double edge = lon > east ? east : west;
                double t = (edge - prevLon) / (lon - prevLon);
                double edgeLat = prevLat + t * (lat - prevLat);
                piece.Add(WebMercator.FromLonLat(edge, edgeLat));
                if (piece.Count >= 2)
                    pieces.Add(piece);

                double delta = lon > east ? -360.0 : 360.0;
                shift += delta;
                lon += delta;
                prevLon = edge + delta;
                prevLat = edgeLat;
                piece = [WebMercator.FromLonLat(prevLon, prevLat)];
            }

            piece.Add(WebMercator.FromLonLat(lon, lat));
            (prevLon, prevLat) = (lon, lat);
        }

        if (piece.Count >= 2)
            pieces.Add(piece);
    }

    // Clips a ring to the Web-Mercator latitude band (Sutherland–Hodgman
    // against the two parallels). Northing diverges towards the poles, so
    // vertices beyond the band would stretch the projected ring — and any
    // extent fitted to it — far past the top or bottom of the world.
    private static (double Lon, double Lat)[] ClipRingToMercatorLimit((double Lon, double Lat)[] ring)
    {
        bool inside = true;
        foreach (var (_, lat) in ring)
            inside &= Math.Abs(lat) <= WebMercator.MaxLatitude;
        if (inside)
            return ring;

        var clipped = ClipRingAtLatitude(ring, WebMercator.MaxLatitude, north: true);
        return ClipRingAtLatitude(clipped, -WebMercator.MaxLatitude, north: false);
    }

    private static (double Lon, double Lat)[] ClipRingAtLatitude(
        (double Lon, double Lat)[] ring, double limit, bool north)
    {
        if (ring.Length == 0)
            return ring;

        bool Inside((double Lon, double Lat) p) => north ? p.Lat <= limit : p.Lat >= limit;

        var output = new List<(double Lon, double Lat)>(ring.Length + 2);
        var prev = ring[^1];
        foreach (var current in ring)
        {
            if (Inside(current))
            {
                if (!Inside(prev))
                    output.Add(CrossAtLatitude(prev, current, limit));
                output.Add(current);
            }
            else if (Inside(prev))
            {
                output.Add(CrossAtLatitude(prev, current, limit));
            }

            prev = current;
        }

        return [.. output];
    }

    // Splits a polyline into the runs that lie within the Web-Mercator
    // latitude band, each ending on the band edge where it leaves.
    private static List<List<(double Lon, double Lat)>> ClipPolylineToMercatorLimit((double Lon, double Lat)[] line)
    {
        static bool Inside((double Lon, double Lat) p) => Math.Abs(p.Lat) <= WebMercator.MaxLatitude;

        var runs = new List<List<(double Lon, double Lat)>>();
        List<(double Lon, double Lat)>? run = null;
        for (int i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (i > 0)
            {
                var prev = line[i - 1];
                foreach (var limit in (ReadOnlySpan<double>)[WebMercator.MaxLatitude, -WebMercator.MaxLatitude])
                {
                    if ((prev.Lat - limit) * (current.Lat - limit) < 0)
                    {
                        var cross = CrossAtLatitude(prev, current, limit);
                        if (run is not null)
                        {
                            run.Add(cross);
                            if (run.Count >= 2)
                                runs.Add(run);
                            run = null;
                        }
                        else
                        {
                            run = [cross];
                        }
                    }
                }
            }

            if (Inside(current))
            {
                run ??= [];
                run.Add(current);
            }
            else if (run is not null)
            {
                if (run.Count >= 2)
                    runs.Add(run);
                run = null;
            }
        }

        if (run is { Count: >= 2 })
            runs.Add(run);
        return runs;
    }

    private static (double Lon, double Lat) CrossAtLatitude(
        (double Lon, double Lat) a, (double Lon, double Lat) b, double limit)
    {
        double t = (limit - a.Lat) / (b.Lat - a.Lat);
        return (a.Lon + t * (b.Lon - a.Lon), limit);
    }

    private static Geometry Shift(Geometry geometry, double dLon)
    {
        if (dLon == 0)
            return geometry;
        var copy = geometry.Copy();
        copy.Apply(new ShiftFilter(dLon));
        copy.GeometryChanged();
        return copy;
    }

    private sealed class ShiftFilter(double dLon) : ICoordinateSequenceFilter
    {
        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence seq, int i) => seq.SetX(i, seq.GetX(i) + dLon);
    }

    /// <summary>
    /// The western edge of the 360° frame a ring or polyline's own longitudes
    /// sit in: 0° for a 0…360 frame, −180° for the usual ±180° frame.
    /// </summary>
    private static double WindowWest(IReadOnlyList<GeoPosition> coords)
    {
        double min = double.MaxValue;
        foreach (var p in coords)
            min = Math.Min(min, p.Longitude);
        return 180.0 * Math.Floor(min / 180.0);
    }

    // Whether a ring or polyline can be projected vertex by vertex: no edge
    // crosses a seam, no vertex lies beyond the latitude limit, and it sits
    // within its frame (which, without seam crossings, it always does).
    private static bool IsPlain(IReadOnlyList<GeoPosition> coords, double west)
    {
        for (int i = 0; i < coords.Count; i++)
        {
            var p = coords[i];
            if (Math.Abs(p.Latitude) > WebMercator.MaxLatitude
                || p.Longitude > west + 360.0
                || (i > 0 && Math.Abs(p.Longitude - coords[i - 1].Longitude) > 180.0))
            {
                return false;
            }
        }

        return true;
    }

    private static bool WithinWindow((double Lon, double Lat)[] coords, double west)
    {
        foreach (var (lon, _) in coords)
        {
            if (lon < west || lon > west + 360.0)
                return false;
        }

        return true;
    }

    private static (double Lon, double Lat)[] Unwrap(IReadOnlyList<GeoPosition> coords)
    {
        var result = new (double Lon, double Lat)[coords.Count];
        if (coords.Count == 0)
            return result;

        double offset = 0;
        result[0] = (coords[0].Longitude, coords[0].Latitude);
        for (int i = 1; i < coords.Count; i++)
        {
            double step = coords[i].Longitude - coords[i - 1].Longitude;
            if (Math.Abs(step) > 180.0)
                offset -= 360.0 * Math.Round(step / 360.0);
            result[i] = (coords[i].Longitude + offset, coords[i].Latitude);
        }

        return result;
    }

    private static void ShiftNear((double Lon, double Lat)[] ring, double targetLon)
    {
        double shift = 360.0 * Math.Round((targetLon - MidLongitude(ring)) / 360.0);
        if (shift == 0)
            return;
        for (int i = 0; i < ring.Length; i++)
            ring[i].Lon += shift;
    }

    private static double MidLongitude((double Lon, double Lat)[] ring)
    {
        double min = double.MaxValue, max = double.MinValue;
        foreach (var (lon, _) in ring)
        {
            min = Math.Min(min, lon);
            max = Math.Max(max, lon);
        }

        return (min + max) / 2.0;
    }

    private static bool IsClosed(IReadOnlyList<GeoPosition> ring) =>
        ring.Count > 1 && ring[0] == ring[^1];

    private static double Wrap180(double degrees) =>
        degrees - 360.0 * Math.Round(degrees / 360.0);

    private static List<Coordinate> ToCoordinates((double Lon, double Lat)[] ring)
    {
        var coords = new List<Coordinate>(ring.Length + 4);
        foreach (var (lon, lat) in ring)
            coords.Add(new Coordinate(lon, lat));
        return coords;
    }

    private static List<Coordinate> ClosedCoordinates((double Lon, double Lat)[] ring)
    {
        var coords = ToCoordinates(ring);
        if (!coords[0].Equals2D(coords[^1]))
            coords.Add(coords[0].Copy());
        return coords;
    }

    private static (double X, double Y)[] Project(IReadOnlyList<(double Lon, double Lat)> coords)
    {
        var result = new (double X, double Y)[coords.Count];
        for (int i = 0; i < coords.Count; i++)
            result[i] = WebMercator.FromLonLat(coords[i].Lon, coords[i].Lat);
        return result;
    }

    private static (double X, double Y)[] Project(IReadOnlyList<GeoPosition> coords)
    {
        var result = new (double X, double Y)[coords.Count];
        for (int i = 0; i < coords.Count; i++)
            result[i] = WebMercator.FromLonLat(coords[i].Longitude, coords[i].Latitude);
        return result;
    }

    private static (double X, double Y)[] Project(LineString ring)
    {
        var coords = ring.Coordinates;
        var result = new (double X, double Y)[coords.Length];
        for (int i = 0; i < coords.Length; i++)
            result[i] = WebMercator.FromLonLat(coords[i].X, coords[i].Y);
        return result;
    }
}
