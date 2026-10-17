namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// A deliberately small implementation of the S-100 Part 13 <c>HostSpatialRelate</c>
/// host function (§13-8.2.3.1), which relates two spatials by a DE-9IM
/// intersection pattern.
/// </summary>
/// <remarks>
/// <para>
/// Only the patterns in <see cref="SupportedPatterns"/> are evaluated, with the
/// first spatial a surface. Today that is <c>T*****FF*</c> ("covers, with
/// interiors meeting"), which the S-401 <c>MARSYS01</c> rule uses to find the
/// <c>NavigationalSystemOfMarks</c> around a buoy, beacon or notice mark. Any
/// other pattern, or a non-surface first spatial, is reported as unsupported
/// rather than answered, so the calling rule fails visibly (Default symbology
/// and a trace) instead of silently drawing the wrong symbol. A test asserts
/// that every pattern the bundled portrayal catalogues pass is supported.
/// </para>
/// <para>
/// Geometry is evaluated in the dataset's integer coordinate space (relations
/// don't change under the coordinate multiplication factors), with exact
/// <see cref="Int128"/> orientation tests. Surface rings are treated as a set of
/// edges, so ring orientation and component order don't matter. A curve is
/// taken to lie in a surface when its vertices and segment midpoints do and no
/// segment properly crosses a ring edge, which can misjudge a segment that
/// leaves the surface only through ring vertices.
/// </para>
/// </remarks>
internal sealed class S101SpatialRelate
{
    /// <summary>The DE-9IM patterns this host evaluates.</summary>
    internal static readonly IReadOnlySet<string> SupportedPatterns =
        new HashSet<string>(StringComparer.Ordinal) { CoversPattern };

    private const string CoversPattern = "T*****FF*";

    private const byte RcnmPoint = 110;
    private const byte RcnmMultiPoint = 115;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmCompositeCurve = 125;
    private const byte RcnmSurface = 130;

    private readonly S101Document _doc;
    private readonly Dictionary<string, Shape?> _shapes = new(StringComparer.Ordinal);

    public S101SpatialRelate(S101Document doc) => _doc = doc;

    /// <summary>
    /// Evaluates <paramref name="pattern"/> between two spatials.
    /// </summary>
    /// <returns>
    /// The relation as a <see cref="bool"/>, or an error message (a
    /// <see cref="string"/>) when the pattern, the geometry types or a spatial
    /// ID can't be evaluated.
    /// </returns>
    public object Relate(string spatialId1, string spatialId2, string pattern)
    {
        var normalized = pattern?.Trim().ToUpperInvariant() ?? string.Empty;
        if (!SupportedPatterns.Contains(normalized))
        {
            return $"HostSpatialRelate: intersection pattern '{pattern}' is not supported by this host " +
                   $"(supported: {string.Join(", ", SupportedPatterns)})";
        }

        var a = GetShape(spatialId1);
        if (a is null)
            return $"HostSpatialRelate: spatial '{spatialId1}' was not found";
        var b = GetShape(spatialId2);
        if (b is null)
            return $"HostSpatialRelate: spatial '{spatialId2}' was not found";

        if (a.Kind != ShapeKind.Surface)
        {
            return $"HostSpatialRelate: pattern '{normalized}' is only supported with a surface as the " +
                   $"first spatial, not {a.Kind} '{spatialId1}'";
        }

        return SurfaceCovers(a, b);
    }

    // ── T*****FF*: the interiors meet, and B lies in the closure of A ─────────

    private static bool SurfaceCovers(Shape a, Shape b)
    {
        if (!a.Bounds.Contains(b.Bounds))
            return false;

        switch (b.Kind)
        {
            case ShapeKind.Point:
                {
                    bool anyInside = false;
                    foreach (var p in b.Points)
                    {
                        var location = Locate(p, a);
                        if (location == Location.Exterior) return false;
                        anyInside |= location == Location.Interior;
                    }

                    return anyInside;
                }

            case ShapeKind.Curve:
                return CurvesInClosure(b.Lines, a, out var curveInside) && curveInside;

            case ShapeKind.Surface:
                {
                    // B's boundary must lie in the closure of A, and no part of A's
                    // boundary (an A hole, say) may lie inside B. With both, B lies in
                    // the closure of A, and two areas then share interior.
                    if (!CurvesInClosure(b.Lines, a, out _))
                        return false;

                    foreach (var line in a.Lines)
                    {
                        for (int i = 0; i < line.Length; i++)
                        {
                            if (Locate(line[i], b) == Location.Interior)
                                return false;
                            if (i > 0 && LocateMidpoint(line[i - 1], line[i], b) == Location.Interior)
                                return false;
                        }
                    }

                    return true;
                }

            default:
                return false;
        }
    }

    private static bool CurvesInClosure(IReadOnlyList<Pt[]> lines, Shape surface, out bool anyInside)
    {
        anyInside = false;
        foreach (var line in lines)
        {
            for (int i = 0; i < line.Length; i++)
            {
                var vertex = Locate(line[i], surface);
                if (vertex == Location.Exterior) return false;
                anyInside |= vertex == Location.Interior;

                if (i == 0) continue;

                var midpoint = LocateMidpoint(line[i - 1], line[i], surface);
                if (midpoint == Location.Exterior) return false;
                anyInside |= midpoint == Location.Interior;

                if (CrossesProperly(line[i - 1], line[i], surface))
                    return false;
            }
        }

        return true;
    }

    // ── Point location (even-odd over the ring edges) ─────────────────────────

    private enum Location { Interior, Boundary, Exterior }

    private static Location LocateMidpoint(Pt p, Pt q, Shape surface)
    {
        // Work at double scale so the midpoint stays on the integer grid.
        var mid = new Pt(p.X + q.X, p.Y + q.Y);
        return Locate(mid, surface, scale: 2);
    }

    private static Location Locate(Pt p, Shape surface, long scale = 1)
    {
        bool inside = false;
        foreach (var line in surface.Lines)
        {
            for (int i = 1; i < line.Length; i++)
            {
                var a = new Pt(line[i - 1].X * scale, line[i - 1].Y * scale);
                var b = new Pt(line[i].X * scale, line[i].Y * scale);

                if (Orientation(a, b, p) == 0 && Within(a.X, b.X, p.X) && Within(a.Y, b.Y, p.Y))
                    return Location.Boundary;

                if ((a.Y > p.Y) != (b.Y > p.Y))
                {
                    // Does the edge cross the horizontal ray to the right of p?
                    var side = Orientation(a, b, p);
                    if (b.Y > a.Y ? side > 0 : side < 0)
                        inside = !inside;
                }
            }
        }

        return inside ? Location.Interior : Location.Exterior;
    }

    private static bool CrossesProperly(Pt p, Pt q, Shape surface)
    {
        foreach (var line in surface.Lines)
        {
            for (int i = 1; i < line.Length; i++)
            {
                var a = line[i - 1];
                var b = line[i];
                var d1 = Orientation(a, b, p);
                var d2 = Orientation(a, b, q);
                var d3 = Orientation(p, q, a);
                var d4 = Orientation(p, q, b);
                if (d1 != 0 && d2 != 0 && d3 != 0 && d4 != 0 && (d1 > 0) != (d2 > 0) && (d3 > 0) != (d4 > 0))
                    return true;
            }
        }

        return false;
    }

    private static int Orientation(Pt a, Pt b, Pt c)
    {
        Int128 cross = (Int128)(b.X - a.X) * (c.Y - a.Y) - (Int128)(b.Y - a.Y) * (c.X - a.X);
        return cross.CompareTo(Int128.Zero);
    }

    private static bool Within(long a, long b, long v) => v >= Math.Min(a, b) && v <= Math.Max(a, b);

    // ── Geometry assembly from the Part 10a spatial records ───────────────────

    private Shape? GetShape(string spatialId)
    {
        if (_shapes.TryGetValue(spatialId, out var cached))
            return cached;

        var shape = BuildShape(spatialId);
        _shapes[spatialId] = shape;
        return shape;
    }

    private Shape? BuildShape(string spatialId)
    {
        var separator = spatialId.IndexOf(':');
        if (separator <= 0
            || !byte.TryParse(spatialId.AsSpan(0, separator), out var rcnm)
            || !uint.TryParse(spatialId.AsSpan(separator + 1), out var rcid))
        {
            return null;
        }

        switch (rcnm)
        {
            case RcnmPoint when _doc.Points.TryGetValue(rcid, out var point):
                return Shape.FromPoints([new Pt(point.X, point.Y)]);

            case RcnmMultiPoint when _doc.MultiPoints.TryGetValue(rcid, out var multiPoint):
                return multiPoint.Points.Count == 0
                    ? null
                    : Shape.FromPoints(multiPoint.Points.Select(p => new Pt(p.X, p.Y)).ToArray());

            case RcnmCurveSegment or RcnmCompositeCurve:
                {
                    var lines = new List<Pt[]>();
                    AddCurve(rcnm, rcid, lines);
                    return lines.Count == 0 ? null : Shape.FromLines(ShapeKind.Curve, lines);
                }

            case RcnmSurface when _doc.Surfaces.TryGetValue(rcid, out var surface):
                {
                    var lines = new List<Pt[]>();
                    foreach (var ring in surface.RingAssociations)
                        AddCurve(ring.RecordName, ring.RecordId, lines);
                    return lines.Count == 0 ? null : Shape.FromLines(ShapeKind.Surface, lines);
                }

            default:
                return null;
        }
    }

    private void AddCurve(byte rcnm, uint rcid, List<Pt[]> lines)
    {
        if (rcnm == RcnmCurveSegment && _doc.CurveSegments.TryGetValue(rcid, out var segment))
        {
            var coords = new List<Pt>(segment.IntermediateCoordinates.Count + 2);
            AddEndpoint(segment, topology: 1, coords);
            foreach (var (y, x) in segment.IntermediateCoordinates)
                coords.Add(new Pt(x, y));
            AddEndpoint(segment, topology: 2, coords);
            if (coords.Count >= 2)
                lines.Add([.. coords]);
        }
        else if (rcnm == RcnmCompositeCurve && _doc.CompositeCurves.TryGetValue(rcid, out var composite))
        {
            foreach (var component in composite.CurveComponents)
                AddCurve(component.RecordName, component.RecordId, lines);
        }
    }

    private void AddEndpoint(S101CurveSegmentRecord segment, byte topology, List<Pt> coords)
    {
        foreach (var association in segment.PointAssociations)
        {
            if (association.Topology == topology && _doc.Points.TryGetValue(association.RecordId, out var point))
            {
                coords.Add(new Pt(point.X, point.Y));
                return;
            }
        }
    }

    // ── Shapes ────────────────────────────────────────────────────────────────

    private readonly record struct Pt(long X, long Y);

    private readonly record struct Box(long MinX, long MinY, long MaxX, long MaxY)
    {
        public bool Contains(Box other) =>
            other.MinX >= MinX && other.MaxX <= MaxX && other.MinY >= MinY && other.MaxY <= MaxY;
    }

    private enum ShapeKind { Point, Curve, Surface }

    private sealed class Shape
    {
        private Shape(ShapeKind kind, Pt[] points, IReadOnlyList<Pt[]> lines, Box bounds)
        {
            Kind = kind;
            Points = points;
            Lines = lines;
            Bounds = bounds;
        }

        public ShapeKind Kind { get; }

        public Pt[] Points { get; }

        public IReadOnlyList<Pt[]> Lines { get; }

        public Box Bounds { get; }

        public static Shape FromPoints(Pt[] points) => new(ShapeKind.Point, points, [], BoundsOf(points));

        public static Shape FromLines(ShapeKind kind, List<Pt[]> lines) =>
            new(kind, [], lines, BoundsOf(lines.SelectMany(l => l)));

        private static Box BoundsOf(IEnumerable<Pt> points)
        {
            long minX = long.MaxValue, minY = long.MaxValue, maxX = long.MinValue, maxY = long.MinValue;
            foreach (var p in points)
            {
                minX = Math.Min(minX, p.X);
                minY = Math.Min(minY, p.Y);
                maxX = Math.Max(maxX, p.X);
                maxY = Math.Max(maxY, p.Y);
            }

            return new Box(minX, minY, maxX, maxY);
        }
    }
}
