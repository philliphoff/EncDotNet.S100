using EncDotNet.S100.DataModel;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Geometry;

/// <summary>A polygon with an exterior ring and holes, each ring closed (first point repeated last).</summary>
/// <param name="Exterior">The exterior ring, clockwise.</param>
/// <param name="Holes">The interior rings, anticlockwise.</param>
public sealed record GeoPolygon(IReadOnlyList<GeoPosition> Exterior, IReadOnlyList<IReadOnlyList<GeoPosition>> Holes);

/// <summary>
/// A rectilinear grid of nodes over a local rectangle, holding one value per
/// node. The nodes are given as column and row coordinates, so grids over
/// neighbouring rectangles can share the nodes along their common edge.
/// </summary>
public sealed class SampleGrid
{
    private readonly double[] _values;
    private readonly double[] _xs;
    private readonly double[] _ys;

    /// <summary>Samples <paramref name="field"/> at every (x, y) pair of the given coordinates.</summary>
    /// <param name="frame">The world frame, for converting nodes to positions.</param>
    /// <param name="xs">Column coordinates in metres east, ascending (at least two).</param>
    /// <param name="ys">Row coordinates in metres north, ascending (at least two).</param>
    /// <param name="field">The value at a local point.</param>
    public SampleGrid(WorldFrame frame, IReadOnlyList<double> xs, IReadOnlyList<double> ys, Func<double, double, double> field)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(xs);
        ArgumentNullException.ThrowIfNull(ys);
        ArgumentNullException.ThrowIfNull(field);
        if (xs.Count < 2 || ys.Count < 2)
        {
            throw new ArgumentException("A grid needs at least two columns and two rows.");
        }

        Frame = frame;
        _xs = [.. xs];
        _ys = [.. ys];
        Extent = new LocalRect(_xs[0], _ys[0], _xs[^1], _ys[^1]);
        _values = new double[Columns * Rows];
        for (var j = 0; j < Rows; j++)
        {
            for (var i = 0; i < Columns; i++)
            {
                _values[j * Columns + i] = field(_xs[i], _ys[j]);
            }
        }
    }

    /// <summary>
    /// The coordinates from <paramref name="min"/> to <paramref name="max"/>
    /// at <paramref name="spacing"/> metres (the last step may be shorter),
    /// plus any <paramref name="extra"/> coordinates that fall between.
    /// </summary>
    /// <param name="min">The first coordinate.</param>
    /// <param name="max">The last coordinate.</param>
    /// <param name="spacing">The step in metres.</param>
    /// <param name="extra">Further coordinates to include, such as the edges of areas cut out of the grid.</param>
    /// <returns>The ascending, distinct coordinates.</returns>
    public static IReadOnlyList<double> Lattice(double min, double max, double spacing, IEnumerable<double>? extra = null)
    {
        var values = new SortedSet<double> { min, max };
        for (var k = 1; min + k * spacing < max; k++)
        {
            values.Add(min + k * spacing);
        }

        foreach (var value in extra ?? [])
        {
            if (value > min && value < max)
            {
                values.Add(value);
            }
        }

        return [.. values];
    }

    /// <summary>The world frame.</summary>
    public WorldFrame Frame { get; }

    /// <summary>The covered rectangle.</summary>
    public LocalRect Extent { get; }

    /// <summary>Nodes east–west.</summary>
    public int Columns => _xs.Length;

    /// <summary>Nodes north–south.</summary>
    public int Rows => _ys.Length;

    /// <summary>The value at a node.</summary>
    /// <param name="i">Column (west to east).</param>
    /// <param name="j">Row (south to north).</param>
    public double this[int i, int j] => _values[j * Columns + i];

    /// <summary>
    /// The part of this grid inside <paramref name="rect"/>, sharing its
    /// nodes and values; the rectangle's edges must be node coordinates.
    /// </summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The sub-grid.</returns>
    public SampleGrid Within(LocalRect rect)
    {
        var columns = Enumerable.Range(0, Columns).Where(i => _xs[i] >= rect.West && _xs[i] <= rect.East).ToArray();
        var rows = Enumerable.Range(0, Rows).Where(j => _ys[j] >= rect.South && _ys[j] <= rect.North).ToArray();
        if (columns.Length < 2 || rows.Length < 2 || _xs[columns[0]] != rect.West || _xs[columns[^1]] != rect.East
            || _ys[rows[0]] != rect.South || _ys[rows[^1]] != rect.North)
        {
            throw new ArgumentException("The rectangle's edges must be node coordinates.", nameof(rect));
        }

        var values = new double[columns.Length * rows.Length];
        for (var j = 0; j < rows.Length; j++)
        {
            for (var i = 0; i < columns.Length; i++)
            {
                values[j * columns.Length + i] = this[columns[i], rows[j]];
            }
        }

        return new SampleGrid(Frame, columns.Select(i => _xs[i]).ToArray(), rows.Select(j => _ys[j]).ToArray(), values);
    }

    private SampleGrid(WorldFrame frame, double[] xs, double[] ys, double[] values)
    {
        Frame = frame;
        _xs = xs;
        _ys = ys;
        _values = values;
        Extent = new LocalRect(xs[0], ys[0], xs[^1], ys[^1]);
    }

    /// <summary>The local position of a node.</summary>
    /// <param name="i">Column.</param>
    /// <param name="j">Row.</param>
    /// <returns>Metres east and north.</returns>
    public (double X, double Y) Node(int i, int j) => (_xs[i], _ys[j]);
}

/// <summary>
/// Contouring over a <see cref="SampleGrid"/> by marching triangles: the
/// regions where the node values lie between two levels, and the lines where
/// they cross one level.
/// </summary>
/// <remarks>
/// <para>
/// Each grid cell is split into two triangles along its south-west to
/// north-east diagonal, and values are interpolated linearly inside each
/// triangle. A region is the union of the triangles clipped to the level
/// range: shared triangle edges cancel and what remains is the boundary.
/// Because the interpolation is linear and every crossing point is computed
/// from the two nodes of its triangle edge, regions traced for neighbouring
/// level ranges share their boundaries exactly: together they tile the grid
/// with no gaps or overlaps, however many levels a grid cell spans.
/// </para>
/// <para>
/// Rings follow S-100 Part 7 §7-4.3.2: exterior rings clockwise, interior
/// rings anticlockwise (the surface on the right of the boundary).
/// </para>
/// </remarks>
public static class Contouring
{
    /// <summary>
    /// The region where the node values lie in [<paramref name="lower"/>,
    /// <paramref name="upper"/>], clipped to the grid's extent.
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <param name="lower">The lower level, or <see cref="double.NegativeInfinity"/>.</param>
    /// <param name="upper">The upper level, or <see cref="double.PositiveInfinity"/>.</param>
    /// <returns>The polygons, in a stable order.</returns>
    public static IReadOnlyList<GeoPolygon> Regions(SampleGrid grid, double lower, double upper)
    {
        ArgumentNullException.ThrowIfNull(grid);

        // Boundary edges, region on the left, keyed by start point; an edge
        // whose reverse is already there cancels it.
        var edges = new Dictionary<((double, double) From, (double, double) To), int>();
        var order = new List<((double, double) From, (double, double) To)>();
        void Add((double X, double Y) from, (double X, double Y) to)
        {
            if (from == to)
            {
                return;
            }

            if (edges.Remove((to, from)))
            {
                return;
            }

            if (edges.TryAdd((from, to), order.Count))
            {
                order.Add((from, to));
            }
        }

        var polygon = new List<Vertex>(8);
        var clipped = new List<Vertex>(8);
        for (var j = 0; j < grid.Rows - 1; j++)
        {
            for (var i = 0; i < grid.Columns - 1; i++)
            {
                foreach (var triangle in Triangles(i, j))
                {
                    polygon.Clear();
                    foreach (var node in triangle)
                    {
                        polygon.Add(new Vertex(grid, node));
                    }

                    if (!double.IsNegativeInfinity(lower))
                    {
                        Clip(grid, polygon, clipped, lower, keepAbove: true);
                        (polygon, clipped) = (clipped, polygon);
                    }

                    if (!double.IsPositiveInfinity(upper))
                    {
                        Clip(grid, polygon, clipped, upper, keepAbove: false);
                        (polygon, clipped) = (clipped, polygon);
                    }

                    for (var k = 0; k < polygon.Count; k++)
                    {
                        Add(polygon[k].Position, polygon[(k + 1) % polygon.Count].Position);
                    }
                }
            }
        }

        var remaining = order.Where(edges.ContainsKey).ToList();
        var rings = Link(remaining, closedOnly: true);

        var exteriors = new List<(List<(double X, double Y)> Ring, double Area, List<List<(double X, double Y)>> Holes)>();
        var holes = new List<List<(double X, double Y)>>();
        foreach (var ring in rings)
        {
            var area = SignedArea(ring);
            if (area > 0)
            {
                exteriors.Add((ring, area, new List<List<(double X, double Y)>>()));
            }
            else if (area < 0)
            {
                holes.Add(ring);
            }
        }

        foreach (var hole in holes)
        {
            var probe = Midpoint(hole[0], hole[1]);
            var best = -1;
            for (var k = 0; k < exteriors.Count; k++)
            {
                if ((best < 0 || exteriors[k].Area < exteriors[best].Area) && Contains(exteriors[k].Ring, probe))
                {
                    best = k;
                }
            }

            if (best >= 0)
            {
                exteriors[best].Holes.Add(hole);
            }
        }

        // Traced with the region on the left (anticlockwise exteriors);
        // S-100 wants it on the right, so every ring is reversed.
        return exteriors
            .Select(e => new GeoPolygon(ToGeo(grid.Frame, Reversed(e.Ring)), e.Holes.Select(h => ToGeo(grid.Frame, Reversed(h))).ToList()))
            .ToList();
    }

    /// <summary>
    /// The lines where the node values cross <paramref name="level"/>: open
    /// lines end on the grid's edge, closed lines repeat their first point.
    /// Values at or above the level are on a line's left.
    /// </summary>
    /// <param name="grid">The grid.</param>
    /// <param name="level">The level.</param>
    /// <returns>The lines, in a stable order.</returns>
    public static IReadOnlyList<IReadOnlyList<GeoPosition>> Lines(SampleGrid grid, double level)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var segments = new List<((double, double) From, (double, double) To)>();
        Span<bool> above = stackalloc bool[3];
        for (var j = 0; j < grid.Rows - 1; j++)
        {
            for (var i = 0; i < grid.Columns - 1; i++)
            {
                foreach (var triangle in Triangles(i, j))
                {
                    for (var k = 0; k < 3; k++)
                    {
                        above[k] = grid[triangle[k].I, triangle[k].J] >= level;
                    }

                    // Walking the triangle anticlockwise, values at or above
                    // the level are on the left from the exit to the entry.
                    (double, double)? exit = null;
                    (double, double)? entry = null;
                    for (var k = 0; k < 3; k++)
                    {
                        var next = (k + 1) % 3;
                        if (above[k] && !above[next])
                        {
                            exit = Crossing(grid, triangle[k], triangle[next], level);
                        }
                        else if (!above[k] && above[next])
                        {
                            entry = Crossing(grid, triangle[k], triangle[next], level);
                        }
                    }

                    if (exit is { } from && entry is { } to && from != to)
                    {
                        segments.Add((from, to));
                    }
                }
            }
        }

        return Link(segments, closedOnly: false).Select(l => ToGeo(grid.Frame, l)).ToList();
    }

    /// <summary>A cell's two triangles, each anticlockwise.</summary>
    private static (int I, int J)[][] Triangles(int i, int j) =>
    [
        [(i, j), (i + 1, j), (i + 1, j + 1)],
        [(i, j), (i + 1, j + 1), (i, j + 1)],
    ];

    /// <summary>
    /// Clips a convex polygon to the side of <paramref name="level"/> to keep
    /// (Sutherland–Hodgman). Every vertex lies on a triangle edge, so each
    /// new point is computed from that edge's two nodes.
    /// </summary>
    private static void Clip(SampleGrid grid, List<Vertex> polygon, List<Vertex> result, double level, bool keepAbove)
    {
        result.Clear();
        for (var k = 0; k < polygon.Count; k++)
        {
            var current = polygon[k];
            var next = polygon[(k + 1) % polygon.Count];
            var currentIn = keepAbove ? current.Value >= level : current.Value <= level;
            var nextIn = keepAbove ? next.Value >= level : next.Value <= level;
            if (currentIn)
            {
                result.Add(current);
            }

            if (currentIn != nextIn)
            {
                result.Add(Vertex.Crossing(grid, current, next, level));
            }
        }
    }

    /// <summary>
    /// The point on the edge between two nodes where the interpolated value
    /// is <paramref name="level"/>, computed from the lower-indexed node so
    /// every triangle sharing the edge gets the same point.
    /// </summary>
    private static (double X, double Y) Crossing(SampleGrid grid, (int I, int J) a, (int I, int J) b, double level)
    {
        if (b.J < a.J || (b.J == a.J && b.I < a.I))
        {
            (a, b) = (b, a);
        }

        var va = grid[a.I, a.J];
        var vb = grid[b.I, b.J];
        var t = (level - va) / (vb - va);
        var pa = grid.Node(a.I, a.J);
        var pb = grid.Node(b.I, b.J);
        return (pa.X + t * (pb.X - pa.X), pa.Y + t * (pb.Y - pa.Y));
    }

    /// <summary>
    /// A polygon vertex during clipping: a node, or a point on the triangle
    /// edge between two nodes with its interpolated value.
    /// </summary>
    private readonly struct Vertex
    {
        private readonly (int I, int J) _a;
        private readonly (int I, int J) _b;

        public Vertex(SampleGrid grid, (int I, int J) node)
        {
            _a = node;
            _b = node;
            Value = grid[node.I, node.J];
            Position = grid.Node(node.I, node.J);
        }

        private Vertex((int I, int J) a, (int I, int J) b, double value, (double X, double Y) position)
        {
            _a = a;
            _b = b;
            Value = value;
            Position = position;
        }

        public double Value { get; }

        public (double X, double Y) Position { get; }

        public static Vertex Crossing(SampleGrid grid, Vertex from, Vertex to, double level)
        {
            // Both vertices lie on one triangle edge (a node is on both of
            // its edges); find it, so the point depends only on that edge.
            var (a, b) = Edge(from, to);
            return new Vertex(a, b, level, Contouring.Crossing(grid, a, b, level));
        }

        private static ((int I, int J) A, (int I, int J) B) Edge(Vertex from, Vertex to)
        {
            if (from._a == from._b && to._a == to._b)
            {
                return (from._a, to._a);
            }

            if (from._a == from._b)
            {
                return to._a == from._a || to._b == from._a ? (to._a, to._b) : throw new InvalidOperationException("Vertices aren't on one edge.");
            }

            if (to._a == to._b)
            {
                return from._a == to._a || from._b == to._a ? (from._a, from._b) : throw new InvalidOperationException("Vertices aren't on one edge.");
            }

            return (from._a, from._b);
        }
    }

    /// <summary>
    /// Joins segments end to start into lines: open lines from the starts
    /// nothing leads into, then closed rings (first point repeated last).
    /// </summary>
    private static List<List<(double X, double Y)>> Link(List<((double X, double Y) From, (double X, double Y) To)> segments, bool closedOnly)
    {
        var next = new Dictionary<(double, double), List<int>>();
        var ends = new HashSet<(double, double)>();
        for (var k = 0; k < segments.Count; k++)
        {
            if (!next.TryGetValue(segments[k].From, out var list))
            {
                next[segments[k].From] = list = new List<int>(1);
            }

            list.Add(k);
            ends.Add(segments[k].To);
        }

        var used = new bool[segments.Count];
        var lines = new List<List<(double X, double Y)>>();

        List<(double X, double Y)> Follow(int first)
        {
            var line = new List<(double X, double Y)> { segments[first].From };
            var k = first;
            while (true)
            {
                used[k] = true;
                var to = segments[k].To;
                line.Add(to);
                var candidates = next.TryGetValue(to, out var list) ? list : null;
                var following = -1;
                foreach (var c in candidates ?? [])
                {
                    if (!used[c])
                    {
                        following = c;
                        break;
                    }
                }

                if (following < 0)
                {
                    return line;
                }

                k = following;
            }
        }

        if (!closedOnly)
        {
            for (var k = 0; k < segments.Count; k++)
            {
                if (!used[k] && !ends.Contains(segments[k].From))
                {
                    lines.Add(Follow(k));
                }
            }
        }

        for (var k = 0; k < segments.Count; k++)
        {
            if (!used[k])
            {
                lines.Add(Follow(k));
            }
        }

        return lines.Select(Clean).Where(l => l.Count >= 2).ToList();
    }

    // Drops repeated points, and middle points of straight runs along a
    // grid line (the edge runs of clipped regions).
    private static List<(double X, double Y)> Clean(List<(double X, double Y)> line)
    {
        var closed = line.Count > 2 && line[0] == line[^1];
        var result = new List<(double X, double Y)>(line.Count);
        foreach (var p in line)
        {
            if (result.Count > 0 && result[^1] == p)
            {
                continue;
            }

            if (result.Count >= 2 && Straight(result[^2], result[^1], p))
            {
                result[^1] = p;
                continue;
            }

            result.Add(p);
        }

        if (closed && result.Count >= 4 && Straight(result[^2], result[0], result[1]))
        {
            // The seam point is itself the middle of a straight run.
            result.RemoveAt(result.Count - 1);
            result.RemoveAt(0);
            result.Add(result[0]);
        }

        return closed && result.Count < 4 ? [] : result;
    }

    // Three points on one grid line, in order.
    private static bool Straight((double X, double Y) a, (double X, double Y) b, (double X, double Y) c) =>
        (a.X == b.X && b.X == c.X && (b.Y - a.Y) * (c.Y - b.Y) > 0)
        || (a.Y == b.Y && b.Y == c.Y && (b.X - a.X) * (c.X - b.X) > 0);

    private static List<(double X, double Y)> Reversed(List<(double X, double Y)> ring)
    {
        var copy = new List<(double X, double Y)>(ring);
        copy.Reverse();
        return copy;
    }

    private static IReadOnlyList<GeoPosition> ToGeo(WorldFrame frame, List<(double X, double Y)> points) =>
        points.Select(p => frame.ToGeo(p.X, p.Y)).ToList();

    private static (double X, double Y) Midpoint((double X, double Y) a, (double X, double Y) b) => ((a.X + b.X) / 2, (a.Y + b.Y) / 2);

    /// <summary>Shoelace area: positive for anticlockwise rings.</summary>
    internal static double SignedArea(IReadOnlyList<(double X, double Y)> ring)
    {
        double sum = 0;
        for (var k = 0; k < ring.Count - 1; k++)
        {
            sum += ring[k].X * ring[k + 1].Y - ring[k + 1].X * ring[k].Y;
        }

        return sum / 2;
    }

    private static bool Contains(List<(double X, double Y)> ring, (double X, double Y) p)
    {
        var inside = false;
        for (int a = 0, b = ring.Count - 1; a < ring.Count; b = a++)
        {
            if ((ring[a].Y > p.Y) != (ring[b].Y > p.Y)
                && p.X < (ring[b].X - ring[a].X) * (p.Y - ring[a].Y) / (ring[b].Y - ring[a].Y) + ring[a].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }
}
