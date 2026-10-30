using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// S-100 Part 9 line suppression: where lines of different features share an
/// edge, only the one drawn last in Part 9 order (the highest plane and
/// drawing priority, then the later in the display list) is drawn there.
/// </summary>
/// <remarks>
/// <para>
/// Edges are matched by their vertices. Features that share a curve record
/// (adjoining areas, or a line feature on an area's boundary) carry the same
/// coordinates for it, so each segment of the shared curve is the same pair of
/// points in either direction.
/// </para>
/// <para>
/// A line never suppresses another line of its own feature: a rule that draws
/// an outline and then a narrower colour on the same geometry means both. A
/// line with <see cref="LineInstruction.Suppression"/> off
/// (<c>LineInstructionUnsuppressed</c>) neither suppresses nor is suppressed.
/// A line is suppressed only by one that is drawn at every display scale it
/// is, so zooming past the suppressing line's scale limits never leaves the
/// edge empty.
/// </para>
/// </remarks>
internal static class LineSuppression
{
    /// <summary>
    /// Works out which parts of the suppressible lines in
    /// <paramref name="sorted"/> to draw.
    /// </summary>
    /// <param name="sorted">The display list in Part 9 draw order.</param>
    /// <param name="polylines">The polylines a line instruction draws, or <see langword="null"/> when it draws none.</param>
    /// <returns>
    /// For each line that loses at least one segment, the polylines left to
    /// draw (possibly none). Lines that keep every segment are absent, so they
    /// draw exactly as before.
    /// </returns>
    public static Dictionary<LineInstruction, List<IReadOnlyList<GeoPosition>>> Apply(
        IReadOnlyList<DrawingInstruction> sorted,
        Func<LineInstruction, IReadOnlyList<IReadOnlyList<GeoPosition>>?> polylines)
    {
        var result = new Dictionary<LineInstruction, List<IReadOnlyList<GeoPosition>>>(ReferenceEqualityComparer.Instance);

        // The suppressible lines with geometry, in draw order.
        var lines = new List<(LineInstruction Line, IReadOnlyList<IReadOnlyList<GeoPosition>> Polylines)>();
        foreach (var instruction in sorted)
        {
            if (instruction is LineInstruction { Suppression: true } line && polylines(line) is { Count: > 0 } parts)
                lines.Add((line, parts));
        }

        if (lines.Count < 2)
            return result;

        // Per segment, the last line to draw it and the last line of another
        // feature to draw it before that one.
        var top = new Dictionary<Segment, (int Last, int OtherFeature)>();
        for (int i = 0; i < lines.Count; i++)
        {
            foreach (var polyline in lines[i].Polylines)
            {
                for (int k = 1; k < polyline.Count; k++)
                {
                    var segment = Segment.Of(polyline[k - 1], polyline[k]);
                    if (!top.TryGetValue(segment, out var entry))
                    {
                        top[segment] = (i, -1);
                    }
                    else if (entry.Last != i)
                    {
                        top[segment] = SameFeature(lines[entry.Last].Line, lines[i].Line)
                            ? (i, entry.OtherFeature)
                            : (i, entry.Last);
                    }
                }
            }
        }

        for (int i = 0; i < lines.Count; i++)
        {
            var (line, parts) = lines[i];
            List<IReadOnlyList<GeoPosition>>? kept = null;

            for (int p = 0; p < parts.Count; p++)
            {
                var polyline = parts[p];
                var runs = KeptRuns(polyline, i, line, lines, top);
                if (runs is null)
                {
                    kept?.Add(polyline);
                    continue;
                }

                if (kept is null)
                {
                    kept = [];
                    for (int q = 0; q < p; q++)
                        kept.Add(parts[q]);
                }
                kept.AddRange(runs);
            }

            if (kept is not null)
                result[line] = kept;
        }

        return result;
    }

    /// <summary>
    /// The runs of <paramref name="polyline"/> left once its suppressed
    /// segments are taken out, or <see langword="null"/> when none is.
    /// </summary>
    private static List<IReadOnlyList<GeoPosition>>? KeptRuns(
        IReadOnlyList<GeoPosition> polyline,
        int index,
        LineInstruction line,
        List<(LineInstruction Line, IReadOnlyList<IReadOnlyList<GeoPosition>> Polylines)> lines,
        Dictionary<Segment, (int Last, int OtherFeature)> top)
    {
        List<IReadOnlyList<GeoPosition>>? runs = null;
        List<GeoPosition>? run = null;
        var firstRunStartsAtZero = false;

        for (int k = 1; k < polyline.Count; k++)
        {
            var (last, other) = top[Segment.Of(polyline[k - 1], polyline[k])];
            var by = SameFeature(lines[last].Line, line) ? other : last;
            var suppressed = by > index && Covers(lines[by].Line, line);

            if (suppressed)
            {
                if (runs is null)
                {
                    // The first suppressed segment: everything before it is one run.
                    runs = [];
                    if (k > 1)
                    {
                        runs.Add(Slice(polyline, 0, k - 1));
                        firstRunStartsAtZero = true;
                    }
                }
                else if (run is not null)
                {
                    runs.Add(run);
                }
                run = null;
                continue;
            }

            if (runs is not null)
            {
                run ??= [polyline[k - 1]];
                run.Add(polyline[k]);
            }
        }

        if (runs is null)
            return null;

        if (run is not null)
        {
            // A closed ring whose first and last segments both draw: join the
            // two runs at the closing vertex, so the line isn't broken there.
            if (firstRunStartsAtZero && runs.Count > 0 && polyline.Count > 2 && polyline[0] == polyline[^1])
            {
                run.AddRange(runs[0].Skip(1));
                runs[0] = run;
            }
            else
            {
                runs.Add(run);
            }
        }

        return runs;
    }

    private static List<GeoPosition> Slice(IReadOnlyList<GeoPosition> polyline, int from, int to)
    {
        var slice = new List<GeoPosition>(to - from + 1);
        for (int k = from; k <= to; k++)
            slice.Add(polyline[k]);
        return slice;
    }

    private static bool SameFeature(LineInstruction a, LineInstruction b) =>
        string.Equals(a.FeatureReference, b.FeatureReference, StringComparison.Ordinal);

    /// <summary>
    /// Whether <paramref name="over"/> draws at every display scale
    /// <paramref name="under"/> does (S-100 Part 9 §11.1 limits: the minimum
    /// is the largest denominator, the maximum the smallest).
    /// </summary>
    private static bool Covers(LineInstruction over, LineInstruction under)
    {
        if (over.ScaleMinimum is { } overMin && (under.ScaleMinimum is not { } underMin || underMin > overMin))
            return false;
        if (over.ScaleMaximum is { } overMax && (under.ScaleMaximum is not { } underMax || underMax < overMax))
            return false;
        return true;
    }

    /// <summary>A segment between two vertices, the same in either direction.</summary>
    private readonly record struct Segment(GeoPosition A, GeoPosition B)
    {
        public static Segment Of(GeoPosition a, GeoPosition b) =>
            a.Latitude < b.Latitude || (a.Latitude == b.Latitude && a.Longitude <= b.Longitude)
                ? new Segment(a, b)
                : new Segment(b, a);
    }
}
