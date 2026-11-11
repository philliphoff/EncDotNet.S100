namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// One traced isoline: a polyline in fractional grid coordinates.
/// </summary>
/// <param name="Level">The value the line follows.</param>
/// <param name="Points">
/// Interleaved <c>column, row</c> vertices in grid-node units (node (0, 0) is
/// <c>(0, 0)</c>; the node one column east is <c>(1, 0)</c>).
/// </param>
public sealed record TracedIsoline(double Level, double[] Points);

/// <summary>
/// Traces isolines through a regular grid of values with marching squares.
/// Nodes holding the no-data value break the lines: a cell with any no-data
/// corner contributes nothing.
/// </summary>
/// <remarks>
/// A coarse grid gives angular lines, so the grid is first subdivided with
/// bilinear interpolation until it is at least <see cref="TargetNodes"/> nodes
/// along its longer side (at most <see cref="MaxSubdivision"/> times). Saddle
/// cells are resolved with the cell-centre average.
/// </remarks>
public static class IsolineTracer
{
    /// <summary>The node count along the longer side the subdivision aims for.</summary>
    public const int TargetNodes = 64;

    /// <summary>The largest subdivision factor.</summary>
    public const int MaxSubdivision = 8;

    /// <summary>
    /// Traces each of <paramref name="levels"/> through <paramref name="values"/>.
    /// </summary>
    /// <param name="values">Row-major values, <paramref name="rows"/> × <paramref name="columns"/>.</param>
    /// <param name="rows">Number of rows.</param>
    /// <param name="columns">Number of columns.</param>
    /// <param name="noData">The no-data value; NaN is always no-data.</param>
    /// <param name="levels">The levels to trace.</param>
    /// <returns>The isolines, one or more per level that crosses the grid.</returns>
    public static IReadOnlyList<TracedIsoline> Trace(
        float[] values,
        int rows,
        int columns,
        float noData,
        IReadOnlyList<double> levels)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(levels);
        if (rows < 2 || columns < 2 || values.Length < rows * columns || levels.Count == 0)
            return [];

        int factor = Math.Clamp(
            (int)Math.Ceiling((TargetNodes - 1.0) / (Math.Max(rows, columns) - 1)), 1, MaxSubdivision);
        var grid = Subdivide(values, rows, columns, noData, factor, out int subRows, out int subColumns);

        var result = new List<TracedIsoline>();
        foreach (var level in levels)
        {
            foreach (var line in TraceLevel(grid, subRows, subColumns, level))
            {
                for (int i = 0; i < line.Length; i++)
                    line[i] /= factor;
                result.Add(new TracedIsoline(level, line));
            }
        }
        return result;
    }

    /// <summary>
    /// Bilinearly subdivides each cell <paramref name="factor"/> times. A new
    /// node is no-data (NaN) when any corner that weighs on it is no-data.
    /// </summary>
    private static double[] Subdivide(
        float[] values, int rows, int columns, float noData, int factor,
        out int subRows, out int subColumns)
    {
        subRows = (rows - 1) * factor + 1;
        subColumns = (columns - 1) * factor + 1;
        var grid = new double[subRows * subColumns];

        for (int i = 0; i < subRows; i++)
        {
            int r0 = Math.Min(i / factor, rows - 2);
            double tr = (double)i / factor - r0;
            for (int j = 0; j < subColumns; j++)
            {
                int c0 = Math.Min(j / factor, columns - 2);
                double tc = (double)j / factor - c0;

                double sum = 0;
                bool missing = false;
                Add(r0, c0, (1 - tr) * (1 - tc));
                Add(r0, c0 + 1, (1 - tr) * tc);
                Add(r0 + 1, c0, tr * (1 - tc));
                Add(r0 + 1, c0 + 1, tr * tc);
                grid[i * subColumns + j] = missing ? double.NaN : sum;

                void Add(int r, int c, double weight)
                {
                    if (weight <= 1e-12)
                        return;
                    float v = values[r * columns + c];
                    if (float.IsNaN(v) || v == noData)
                        missing = true;
                    else
                        sum += weight * v;
                }
            }
        }
        return grid;
    }

    // Edge ids: the horizontal edge from node (r, c) to (r, c + 1) is
    // 2 * (r * columns + c); the vertical edge from (r, c) to (r + 1, c) is
    // 2 * (r * columns + c) + 1. Each edge belongs to at most two cells, so
    // segments are joined into polylines through their shared edges.
    private static List<double[]> TraceLevel(double[] grid, int rows, int columns, double level)
    {
        var segments = new List<(int A, int B)>();
        var points = new Dictionary<int, (double X, double Y)>();

        for (int r = 0; r < rows - 1; r++)
        {
            for (int c = 0; c < columns - 1; c++)
            {
                double v00 = grid[r * columns + c];
                double v01 = grid[r * columns + c + 1];
                double v10 = grid[(r + 1) * columns + c];
                double v11 = grid[(r + 1) * columns + c + 1];
                if (double.IsNaN(v00) || double.IsNaN(v01) || double.IsNaN(v10) || double.IsNaN(v11))
                    continue;

                int index = (v00 >= level ? 1 : 0)
                    | (v01 >= level ? 2 : 0)
                    | (v11 >= level ? 4 : 0)
                    | (v10 >= level ? 8 : 0);
                if (index is 0 or 15)
                    continue;

                int south = 2 * (r * columns + c);
                int north = 2 * ((r + 1) * columns + c);
                int west = 2 * (r * columns + c) + 1;
                int east = 2 * (r * columns + c + 1) + 1;

                int Edge(int id)
                {
                    if (!points.ContainsKey(id))
                    {
                        points[id] = id == south ? (c + Fraction(v00, v01, level), r)
                            : id == north ? (c + Fraction(v10, v11, level), r + 1)
                            : id == west ? (c, r + Fraction(v00, v10, level))
                            : (c + 1, r + Fraction(v01, v11, level));
                    }
                    return id;
                }

                void Segment(int a, int b) => segments.Add((Edge(a), Edge(b)));

                switch (index)
                {
                    case 1 or 14: Segment(west, south); break;
                    case 2 or 13: Segment(south, east); break;
                    case 3 or 12: Segment(west, east); break;
                    case 4 or 11: Segment(east, north); break;
                    case 6 or 9: Segment(south, north); break;
                    case 7 or 8: Segment(west, north); break;
                    case 5 or 10:
                        {
                            // Saddle: the centre value decides which corners connect.
                            bool centreHigh = (v00 + v01 + v10 + v11) / 4.0 >= level;
                            bool lowerLeftHigh = index == 5;
                            if (centreHigh == lowerLeftHigh)
                            {
                                // The high corners (v00 and v11 for 5) join through the centre.
                                Segment(west, north);
                                Segment(south, east);
                            }
                            else
                            {
                                Segment(west, south);
                                Segment(east, north);
                            }
                            break;
                        }
                }
            }
        }

        return Join(segments, points);
    }

    private static double Fraction(double a, double b, double level)
    {
        double span = b - a;
        return span == 0 ? 0.5 : Math.Clamp((level - a) / span, 0, 1);
    }

    private static List<double[]> Join(List<(int A, int B)> segments, Dictionary<int, (double X, double Y)> points)
    {
        var byEdge = new Dictionary<int, List<int>>();
        for (int i = 0; i < segments.Count; i++)
        {
            AddTo(segments[i].A, i);
            AddTo(segments[i].B, i);
        }

        var used = new bool[segments.Count];
        var lines = new List<double[]>();

        // Start open lines at their free ends first, then pick up closed loops.
        var starts = Enumerable.Range(0, segments.Count)
            .OrderBy(i => byEdge[segments[i].A].Count == 1 || byEdge[segments[i].B].Count == 1 ? 0 : 1);
        foreach (int start in starts)
        {
            if (used[start])
                continue;

            var (a, b) = segments[start];
            // Walk from the free end when there is one.
            if (byEdge[a].Count != 1 && byEdge[b].Count == 1)
                (a, b) = (b, a);

            used[start] = true;
            var chain = new List<int> { a, b };
            int current = b;
            while (Next(current) is { } next)
            {
                used[next] = true;
                current = segments[next].A == current ? segments[next].B : segments[next].A;
                chain.Add(current);
            }

            var xy = new double[chain.Count * 2];
            for (int i = 0; i < chain.Count; i++)
            {
                var (x, y) = points[chain[i]];
                xy[2 * i] = x;
                xy[2 * i + 1] = y;
            }
            lines.Add(xy);
        }
        return lines;

        int? Next(int edge)
        {
            foreach (int candidate in byEdge[edge])
            {
                if (!used[candidate])
                    return candidate;
            }
            return null;
        }

        void AddTo(int edge, int segment)
        {
            if (!byEdge.TryGetValue(edge, out var list))
                byEdge[edge] = list = new List<int>(2);
            list.Add(segment);
        }
    }
}
