using EncDotNet.S57;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NetTopologySuite.Noding;
using NetTopologySuite.Noding.Snapround;
using NetTopologySuite.Operation.Polygonize;

namespace EncDotNet.S100.Datasets.S57;

/// <summary>
/// The connected nodes and edges of an S-57 chain-node topology, in the
/// dataset's integer coordinates, with the operations
/// <see cref="S101ToS57Translator"/> needs to change area geometry without
/// breaking shared edges: snap-rounded noding, the faces the edges bound,
/// and the boundary of a set of faces as S-57 spatial pointers.
/// </summary>
/// <remarks>
/// S-57 encodes an area by the edges that bound it, exterior rings clockwise
/// and interior rings anticlockwise (S-57 Part 3 §4.7.3), and two areas that
/// touch share the edges between them. The graph keeps that: an edge that is
/// split is replaced, in order, by its pieces in every pointer that used it.
/// </remarks>
internal sealed class S57EdgeGraph
{
    private const int Rcnm = 130;
    private const int Null = 255;

    private static readonly GeometryFactory Factory = new(new PrecisionModel(1.0));

    private readonly Func<uint> _nextId;
    private readonly Dictionary<(int X, int Y), uint> _nodeAt = new();

    /// <summary>An edge between two connected nodes, with its intermediate positions.</summary>
    public sealed record Edge(uint Begin, uint End, IReadOnlyList<(int X, int Y)> Inner);

    /// <summary>One use of an edge in a ring, in the direction the ring runs.</summary>
    public readonly record struct Use(uint Edge, bool Reverse);

    /// <summary>A face of the planar graph: its polygon and its boundary, interior on the right.</summary>
    public sealed record Face(Polygon Polygon, IReadOnlyList<Use> Boundary);

    public S57EdgeGraph(Func<uint> nextId) => _nextId = nextId;

    public Dictionary<uint, (int X, int Y)> Nodes { get; } = new();

    public Dictionary<uint, Edge> Edges { get; } = new();

    public void AddNode(uint id, int x, int y)
    {
        Nodes[id] = (x, y);
        _nodeAt.TryAdd((x, y), id);
    }

    /// <summary>The connected node at a position, created when there is none.</summary>
    public uint NodeAt((int X, int Y) position)
    {
        if (_nodeAt.TryGetValue(position, out var id)) return id;
        id = _nextId();
        AddNode(id, position.X, position.Y);
        return id;
    }

    /// <summary>Adds an edge along the positions, from the first to the last.</summary>
    public uint AddEdge(IReadOnlyList<(int X, int Y)> positions)
    {
        var id = _nextId();
        Edges[id] = new Edge(NodeAt(positions[0]), NodeAt(positions[^1]), positions.Skip(1).Take(positions.Count - 2).ToArray());
        return id;
    }

    public List<(int X, int Y)> Positions(uint edge, bool reverse = false)
    {
        var e = Edges[edge];
        var positions = new List<(int X, int Y)>(e.Inner.Count + 2) { Nodes[e.Begin] };
        positions.AddRange(e.Inner);
        positions.Add(Nodes[e.End]);
        if (reverse) positions.Reverse();
        return positions;
    }

    private uint StartNode(Use use) => use.Reverse ? Edges[use.Edge].End : Edges[use.Edge].Begin;

    private uint EndNode(Use use) => use.Reverse ? Edges[use.Edge].Begin : Edges[use.Edge].End;

    // ── Areas ───────────────────────────────────────────────────────────

    /// <summary>
    /// The polygon an area's edge pointers bound, its rings chained by shared
    /// nodes; <c>null</c> when they bound none.
    /// </summary>
    public Geometry? Area(IEnumerable<S57SpatialPointer> pointers)
    {
        var uses = pointers
            .Where(p => p.Name.RecordNameCode == Rcnm && Edges.ContainsKey((uint)p.Name.RecordId))
            .Select(p => new Use((uint)p.Name.RecordId, (int)p.Orientation == 2))
            .ToList();
        var rings = Chain(uses).Select(Ring).Where(r => r is not null).Select(r => r!).ToList();
        if (rings.Count == 0) return null;

        // Usually one exterior ring with holes inside it.
        var ordered = rings.OrderByDescending(r => Math.Abs(NetTopologySuite.Algorithm.Area.OfRingSigned(r.CoordinateSequence))).ToList();
        var shell = Factory.CreatePolygon(ordered[0]);
        if (ordered.Skip(1).All(r => shell.Contains(Factory.CreatePolygon(r).InteriorPoint)))
            return Factory.CreatePolygon(ordered[0], ordered.Skip(1).ToArray());

        // Otherwise even-odd: each ring toggles the area it encloses.
        Geometry area = Factory.CreatePolygon();
        try
        {
            foreach (var ring in rings.OrderByDescending(r => Factory.CreatePolygon(r).Area))
                area = area.SymmetricDifference(Factory.CreatePolygon(ring).Buffer(0));
        }
        catch (TopologyException)
        {
            return null;
        }
        return area.IsEmpty ? null : area;
    }

    private LinearRing? Ring(List<Use> ring)
    {
        var coordinates = new List<Coordinate>();
        foreach (var use in ring)
        {
            var positions = Positions(use.Edge, use.Reverse);
            foreach (var (x, y) in coordinates.Count == 0 ? positions : positions.Skip(1))
                coordinates.Add(new Coordinate(x, y));
        }
        if (coordinates.Count < 4 || !coordinates[0].Equals2D(coordinates[^1])) return null;
        return Factory.CreateLinearRing(coordinates.ToArray());
    }

    // Chains edge uses into closed rings by shared nodes, re-orienting uses
    // that run the other way. Uses left over form open chains, which are
    // returned too so a caller can see them.
    private List<List<Use>> Chain(List<Use> uses)
    {
        var remaining = new List<Use>(uses);
        var rings = new List<List<Use>>();
        while (remaining.Count > 0)
        {
            var ring = new List<Use> { remaining[0] };
            remaining.RemoveAt(0);
            var start = StartNode(ring[0]);
            while (EndNode(ring[^1]) != start)
            {
                var end = EndNode(ring[^1]);
                int next = remaining.FindIndex(u => StartNode(u) == end);
                if (next < 0)
                {
                    next = remaining.FindIndex(u => EndNode(u) == end);
                    if (next < 0) break;
                    remaining[next] = remaining[next] with { Reverse = !remaining[next].Reverse };
                }
                ring.Add(remaining[next]);
                remaining.RemoveAt(next);
            }
            rings.Add(ring);
        }
        return rings;
    }

    // ── Noding ──────────────────────────────────────────────────────────

    /// <summary>
    /// Splits the edges where they cross or touch each other or the extra
    /// lines, rounding new nodes to the integer grid, and adds the pieces of
    /// the extra lines as edges.
    /// </summary>
    /// <returns>
    /// The pieces, in order, of each edge that was split (the edge itself is
    /// removed), and the edges the extra lines became.
    /// </returns>
    public (Dictionary<uint, List<Use>> Split, List<uint> Added) Node(
        IReadOnlyCollection<uint> edges, IReadOnlyList<IReadOnlyList<(int X, int Y)>> extra)
    {
        var strings = new List<ISegmentString>();
        foreach (var edge in edges)
            strings.Add(new NodedSegmentString(Positions(edge).Select(p => new Coordinate(p.X, p.Y)).ToArray(), (long)edge));
        for (int i = 0; i < extra.Count; i++)
            strings.Add(new NodedSegmentString(extra[i].Select(p => new Coordinate(p.X, p.Y)).ToArray(), (long)(-1 - i)));

        var noder = new SnapRoundingNoder(Factory.PrecisionModel);
        noder.ComputeNodes(strings);
        var pieces = noder.GetNodedSubstrings()
            .Where(s => s.Count >= 2)
            .GroupBy(s => (long)s.Context)
            .ToDictionary(g => g.Key, g => g.Select(s => s.Coordinates.Select(c => ((int)Math.Round(c.X), (int)Math.Round(c.Y))).ToList()).ToList());

        // Edges the noding left whole keep their ids; so does any piece that
        // repeats one of them (an extra line along an existing edge).
        var byKey = new Dictionary<string, Use>(StringComparer.Ordinal);
        var split = new Dictionary<uint, List<Use>>();
        var whole = new HashSet<uint>();
        foreach (var edge in edges)
        {
            if (pieces.TryGetValue(edge, out var list) && list.Count == 1 && list[0].SequenceEqual(Positions(edge)))
            {
                whole.Add(edge);
                Register(byKey, list[0], edge);
            }
        }

        foreach (var edge in edges)
        {
            if (whole.Contains(edge) || !pieces.TryGetValue(edge, out var list)) continue;
            var start = Positions(edge)[0];
            split[edge] = Order(list, start).Select(p => Piece(byKey, p)).ToList();
        }

        var added = new List<uint>();
        for (int i = 0; i < extra.Count; i++)
        {
            if (!pieces.TryGetValue(-1 - i, out var list)) continue;
            foreach (var piece in list)
                added.Add(Piece(byKey, piece).Edge);
        }

        foreach (var edge in split.Keys)
            Edges.Remove(edge);
        return (split, added);
    }

    private static void Register(Dictionary<string, Use> byKey, List<(int X, int Y)> positions, uint edge)
    {
        byKey.TryAdd(Key(positions), new Use(edge, false));
        var reversed = new List<(int X, int Y)>(positions);
        reversed.Reverse();
        byKey.TryAdd(Key(reversed), new Use(edge, true));
    }

    private Use Piece(Dictionary<string, Use> byKey, List<(int X, int Y)> positions)
    {
        if (byKey.TryGetValue(Key(positions), out var use)) return use;
        var id = AddEdge(positions);
        Register(byKey, positions, id);
        return new Use(id, false);
    }

    private static string Key(List<(int X, int Y)> positions)
        => string.Join(';', positions.Select(p => $"{p.X},{p.Y}"));

    // The pieces of one edge, from its start to its end.
    private static List<List<(int X, int Y)>> Order(List<List<(int X, int Y)>> pieces, (int X, int Y) start)
    {
        var ordered = new List<List<(int X, int Y)>>();
        var remaining = new List<List<(int X, int Y)>>(pieces);
        var at = start;
        while (remaining.Count > 0)
        {
            int next = remaining.FindIndex(p => p[0] == at);
            if (next < 0) next = 0;
            ordered.Add(remaining[next]);
            at = remaining[next][^1];
            remaining.RemoveAt(next);
        }
        return ordered;
    }

    /// <summary>Replaces each split edge in the pointers with its pieces, in the pointer's direction.</summary>
    public static List<S57SpatialPointer> Replace(IReadOnlyList<S57SpatialPointer> pointers, Dictionary<uint, List<Use>> split)
    {
        if (split.Count == 0 || !pointers.Any(p => p.Name.RecordNameCode == Rcnm && split.ContainsKey((uint)p.Name.RecordId)))
            return [.. pointers];
        var result = new List<S57SpatialPointer>(pointers.Count + 4);
        foreach (var pointer in pointers)
        {
            if (pointer.Name.RecordNameCode != Rcnm || !split.TryGetValue((uint)pointer.Name.RecordId, out var pieces))
            {
                result.Add(pointer);
                continue;
            }
            bool reverse = (int)pointer.Orientation == 2;
            var ordered = reverse ? Enumerable.Reverse(pieces) : pieces;
            foreach (var piece in ordered)
                result.Add(Pointer(piece.Edge, piece.Reverse ^ reverse, (int)pointer.Usage));
        }
        return result;
    }

    public static S57SpatialPointer Pointer(uint edge, bool reverse, int usage)
        => new()
        {
            Name = new S57RecordName { RecordNameCode = Rcnm, RecordId = (int)edge },
            Orientation = (S57Orientation)(reverse ? 2 : 1),
            Usage = (S57UsageIndicator)usage,
            Mask = (S57MaskingIndicator)Null,
        };

    // ── Faces ───────────────────────────────────────────────────────────

    /// <summary>The faces the edges bound (the edges must be noded).</summary>
    public List<Face> Faces(IEnumerable<uint> edges)
    {
        var start = new Dictionary<((int, int), (int, int)), Use>();
        var polygonizer = new Polygonizer(extractOnlyPolygonal: false);
        foreach (var edge in edges.Distinct())
        {
            var positions = Positions(edge);
            if (positions.Count < 2) continue;
            start.TryAdd((positions[0], positions[1]), new Use(edge, false));
            start.TryAdd((positions[^1], positions[^2]), new Use(edge, true));
            polygonizer.Add(Factory.CreateLineString(positions.Select(p => new Coordinate(p.X, p.Y)).ToArray()));
        }

        var faces = new List<Face>();
        foreach (var geometry in polygonizer.GetPolygons())
        {
            if (geometry is not Polygon polygon || polygon.IsEmpty) continue;
            var boundary = new List<Use>();
            var ok = Walk(polygon.Shell, clockwise: true, start, boundary);
            foreach (var hole in polygon.Holes)
                ok &= Walk(hole, clockwise: false, start, boundary);
            if (ok) faces.Add(new Face(polygon, boundary));
        }
        return faces;
    }

    // Decomposes a ring into edge uses, oriented so the face is on the right.
    private bool Walk(LineString ring, bool clockwise, Dictionary<((int, int), (int, int)), Use> start, List<Use> uses)
    {
        var coordinates = ring.Coordinates.Select(c => ((int)Math.Round(c.X), (int)Math.Round(c.Y))).ToList();
        if (Orientation.IsCCW(ring.CoordinateSequence) == clockwise)
            coordinates.Reverse();
        coordinates.RemoveAt(coordinates.Count - 1);
        int n = coordinates.Count;
        int first = Enumerable.Range(0, n).FirstOrDefault(i => start.ContainsKey((coordinates[i], coordinates[(i + 1) % n])), -1);
        if (first < 0) return false;

        int walked = 0, at = first;
        while (walked < n)
        {
            if (!start.TryGetValue((coordinates[at], coordinates[(at + 1) % n]), out var use)) return false;
            uses.Add(use);
            int segments = Edges[use.Edge].Inner.Count + 1;
            walked += segments;
            at = (at + segments) % n;
        }
        return walked == n;
    }

    /// <summary>
    /// The boundary of the union of the faces, as one list of S-57 spatial
    /// pointers per exterior ring with the interior rings it encloses.
    /// </summary>
    public List<List<S57SpatialPointer>> Boundary(IEnumerable<Face> faces)
    {
        // Edges two of the faces share cancel out.
        var counts = new Dictionary<uint, int>();
        var uses = new List<Use>();
        foreach (var face in faces)
        {
            foreach (var use in face.Boundary)
            {
                counts[use.Edge] = counts.GetValueOrDefault(use.Edge) + 1;
                uses.Add(use);
            }
        }
        var outline = uses.Where(u => counts[u.Edge] == 1).ToList();

        var exteriors = new List<(List<Use> Ring, Polygon Polygon)>();
        var holes = new List<(List<Use> Ring, Polygon Polygon)>();
        foreach (var ring in Chain(outline))
        {
            if (Ring(ring) is not { } linear) continue;
            var polygon = Factory.CreatePolygon(linear);
            (Orientation.IsCCW(linear.CoordinateSequence) ? holes : exteriors).Add((ring, polygon));
        }

        var result = new List<List<S57SpatialPointer>>();
        var claimed = new HashSet<int>();
        foreach (var (ring, polygon) in exteriors.OrderBy(e => e.Polygon.Area))
        {
            var pointers = ring.Select(u => Pointer(u.Edge, u.Reverse, 1)).ToList();
            for (int h = 0; h < holes.Count; h++)
            {
                if (claimed.Contains(h) || !polygon.Contains(holes[h].Polygon.InteriorPoint)) continue;
                claimed.Add(h);
                pointers.AddRange(holes[h].Ring.Select(u => Pointer(u.Edge, u.Reverse, 2)));
            }
            result.Add(pointers);
        }
        return result;
    }
}
