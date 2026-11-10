using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using EncDotNet.S57;
using EncDotNet.S57.Charts;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>
/// Assembles the geometry of an AML feature from the cell's vector topology:
/// a point from its node, a curve from its edges, a surface from its edges
/// (chain-node) or faces (full topology). Arc edges (S-57 Part 3 §5.1.4.4,
/// which RAL uses for airspace) are densified with <see cref="S57ArcDensifier"/>.
/// </summary>
/// <remarks>
/// Exterior rings come out clockwise and interior rings anticlockwise, seen
/// north up, whatever the cell's order, so a line style's right is always
/// inside the area (<see cref="AmlSignature"/>). Edges that don't chain into
/// closed rings are closed on themselves rather than dropped.
/// </remarks>
public sealed class AmlGeometry
{
    /// <summary>
    /// The default tolerance arcs are densified to, in decimal degrees: about
    /// 5 m on the ground. AML is drawn over charts no larger than about 1:4,000
    /// (scale band 7), where 5 m is about a millimetre.
    /// </summary>
    public const double DefaultArcTolerance = 5e-5;

    private readonly S57Chart _chart;
    private readonly double _arcTolerance;
    private readonly Dictionary<S57RecordName, GeoPosition[]> _edges = [];

    /// <summary>Creates an assembler over <paramref name="chart"/>.</summary>
    /// <param name="chart">The cell.</param>
    /// <param name="arcTolerance">The largest distance between an arc and its polyline, in decimal degrees.</param>
    public AmlGeometry(S57Chart chart, double arcTolerance = DefaultArcTolerance)
    {
        ArgumentNullException.ThrowIfNull(chart);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(arcTolerance);
        _chart = chart;
        _arcTolerance = arcTolerance;
    }

    /// <summary>
    /// The positions of a point feature: its node's position, or the position
    /// of each of its soundings (<see cref="Soundings"/>); none when its node
    /// is missing.
    /// </summary>
    /// <param name="feature">The feature.</param>
    public IReadOnlyList<GeoPosition> Points(S57PointFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var points = new List<GeoPosition>(feature.SpatialReferences.Count);
        foreach (var reference in feature.SpatialReferences)
        {
            if (Node(reference.Name) is { } position)
                points.Add(position);
            else if (_chart.GetIsolatedNode(reference.Name) is { HasSoundings: true } node)
                points.AddRange(node.Soundings.Select(s => Sounding(s).Position));
        }

        return points;
    }

    /// <summary>
    /// The soundings of a point feature, such as a <c>SOUNDG</c>, whose
    /// isolated node holds 3-D coordinates (<c>SG3D</c>): each position and
    /// its depth in the cell's units, positive down. Empty for a feature
    /// without soundings.
    /// </summary>
    /// <param name="feature">The feature.</param>
    public IReadOnlyList<AmlSounding> Soundings(S57PointFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        List<AmlSounding>? soundings = null;
        foreach (var reference in feature.SpatialReferences)
        {
            if (_chart.GetIsolatedNode(reference.Name) is { HasSoundings: true } node)
                (soundings ??= []).AddRange(node.Soundings.Select(Sounding));
        }

        return soundings ?? (IReadOnlyList<AmlSounding>)[];
    }

    /// <summary>The curves of a line feature: its edges, joined where they meet.</summary>
    /// <param name="feature">The feature.</param>
    public IReadOnlyList<IReadOnlyList<GeoPosition>> Curves(S57LineFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var curves = new List<List<GeoPosition>>();
        foreach (var reference in feature.EdgeReferences)
        {
            var edge = Edge(reference.Name, reference.Orientation == S57Orientation.Reverse);
            if (edge.Length < 2)
                continue;
            if (curves.Count > 0 && curves[^1][^1] == edge[0])
                curves[^1].AddRange(edge.Skip(1));
            else
                curves.Add([.. edge]);
        }

        return curves;
    }

    /// <summary>
    /// The surfaces of an area feature: each exterior ring, clockwise, with
    /// the interior rings inside it, anticlockwise.
    /// </summary>
    /// <param name="feature">The feature.</param>
    public IReadOnlyList<SurfacePart> Surfaces(S57AreaFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        var exterior = new List<(GeoPosition[] Coordinates, S57RecordName Name)>();
        var interior = new List<(GeoPosition[] Coordinates, S57RecordName Name)>();

        if (feature.HasExteriorEdgeReferences)
        {
            Collect(feature.ExteriorEdgeReferences, exterior);
            Collect(feature.InteriorEdgeReferences, interior);
        }
        else
        {
            foreach (var reference in feature.FaceReferences)
            {
                if (_chart.GetFace(reference.Name) is not { } face)
                    continue;
                Collect(face.ExteriorBoundary, exterior);
                Collect(face.InteriorBoundaries, interior);
            }
        }

        var outers = Rings(exterior).Select(r => Orient(r, clockwise: true)).ToList();
        var holes = Rings(interior).Select(r => Orient(r, clockwise: false)).ToList();
        if (outers.Count == 0)
            return [];

        var parts = outers.Select(o => (Ring: o, Holes: new List<IReadOnlyList<GeoPosition>>())).ToList();
        foreach (var hole in holes)
        {
            var owner = parts.FirstOrDefault(p => Contains(p.Ring, hole[0]));
            (owner.Ring is null ? parts[0] : owner).Holes.Add(hole);
        }

        return [.. parts.Select(p => new SurfacePart(p.Ring, p.Holes))];
    }

    private void Collect(IReadOnlyList<S57EdgeReference> references, List<(GeoPosition[], S57RecordName)> into)
    {
        foreach (var reference in references)
            into.Add((Edge(reference.EdgeName, reference.Orientation == S57Orientation.Reverse), reference.EdgeName));
    }

    // The edge's coordinates, from its beginning node to its end node (or the
    // other way), with its arcs densified.
    private GeoPosition[] Edge(S57RecordName name, bool reverse)
    {
        if (!_edges.TryGetValue(name, out var coordinates))
        {
            coordinates = BuildEdge(name);
            _edges[name] = coordinates;
        }

        if (!reverse)
            return coordinates;
        var reversed = (GeoPosition[])coordinates.Clone();
        Array.Reverse(reversed);
        return reversed;
    }

    private GeoPosition[] BuildEdge(S57RecordName name)
    {
        if (_chart.GetEdge(name) is not { } edge)
            return [];

        var points = new List<GeoPosition>();
        void Add(GeoPosition position)
        {
            if (points.Count == 0 || points[^1] != position)
                points.Add(position);
        }

        if (edge.BeginningNode is { } begin && Node(begin) is { } start)
            Add(start);
        var middle = edge.HasArcCurves
            ? S57ArcDensifier.Densify(edge.ArcCurves, _arcTolerance, _chart.CoordinateMultiplicationFactor)
            : edge.IntermediatePoints;
        foreach (var coordinate in middle)
            Add(Position(coordinate));
        if (edge.EndNode is { } endName && Node(endName) is { } end)
            Add(end);
        return [.. points];
    }

    private GeoPosition? Node(S57RecordName name)
    {
        if (_chart.GetConnectedNode(name) is { } connected)
            return Position(connected.Position);
        if (_chart.GetIsolatedNode(name) is { Position: { } position })
            return Position(position);
        return null;
    }

    private AmlSounding Sounding(S57Sounding sounding)
    {
        var (longitude, latitude, depth) = _chart.ToDecimalValues(sounding);
        return new AmlSounding(new GeoPosition(latitude, longitude), depth);
    }

    private GeoPosition Position(S57Coordinate2D coordinate)
    {
        var (longitude, latitude) = _chart.ToDecimalDegrees(coordinate);
        return new GeoPosition(latitude, longitude);
    }

    // Chains edges into closed rings: each ring starts with the first unused
    // edge and takes the edge that starts (or, reversed, ends) where it ends,
    // until it closes. A chain that can't close is closed on itself.
    private static List<GeoPosition[]> Rings(List<(GeoPosition[] Coordinates, S57RecordName Name)> edges)
    {
        var rings = new List<GeoPosition[]>();
        var unused = edges.Where(e => e.Coordinates.Length >= 2).Select(e => e.Coordinates).ToList();
        while (unused.Count > 0)
        {
            var ring = new List<GeoPosition>(unused[0]);
            unused.RemoveAt(0);
            while (ring[0] != ring[^1])
            {
                int next = unused.FindIndex(e => e[0] == ring[^1]);
                bool reverse = false;
                if (next < 0)
                {
                    next = unused.FindIndex(e => e[^1] == ring[^1]);
                    reverse = next >= 0;
                }

                if (next < 0)
                {
                    ring.Add(ring[0]);
                    break;
                }

                var edge = unused[next];
                unused.RemoveAt(next);
                ring.AddRange(reverse ? edge.Reverse().Skip(1) : edge.Skip(1));
            }

            if (ring.Count >= 4)
                rings.Add([.. ring]);
        }

        return rings;
    }

    private static GeoPosition[] Orient(GeoPosition[] ring, bool clockwise)
    {
        bool isClockwise = SignedArea(ring) < 0;
        if (isClockwise == clockwise)
            return ring;
        var reversed = (GeoPosition[])ring.Clone();
        Array.Reverse(reversed);
        return reversed;
    }

    // Twice the signed area, longitude as x and latitude as y: positive when anticlockwise.
    internal static double SignedArea(IReadOnlyList<GeoPosition> ring)
    {
        double sum = 0;
        for (int i = 0; i < ring.Count - 1; i++)
            sum += (ring[i].Longitude * ring[i + 1].Latitude) - (ring[i + 1].Longitude * ring[i].Latitude);
        return sum;
    }

    private static bool Contains(IReadOnlyList<GeoPosition> ring, GeoPosition point)
    {
        bool inside = false;
        for (int i = 0, j = ring.Count - 1; i < ring.Count; j = i++)
        {
            var a = ring[i];
            var b = ring[j];
            if ((a.Latitude > point.Latitude) != (b.Latitude > point.Latitude)
                && point.Longitude < ((b.Longitude - a.Longitude) * (point.Latitude - a.Latitude) / (b.Latitude - a.Latitude)) + a.Longitude)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
