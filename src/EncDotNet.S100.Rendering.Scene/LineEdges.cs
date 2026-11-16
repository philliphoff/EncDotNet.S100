using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Rendering.Scene;

/// <summary>
/// One run of a line instruction left to draw once S-100 Part 9 line
/// suppression is applied: the instruction and the geographic polyline it
/// strokes there.
/// </summary>
/// <param name="Instruction">
/// The line instruction to stroke along <paramref name="Coordinates"/>, unchanged
/// from the display list (style, colour, width, scale limits, priority).
/// </param>
/// <param name="Coordinates">
/// The polyline, in the geographic coordinates of the feature geometry. A
/// shared edge appears in exactly one <see cref="LineEdge"/> among the
/// suppressible lines of different features.
/// </param>
public sealed record LineEdge(LineInstruction Instruction, IReadOnlyList<GeoPosition> Coordinates);

/// <summary>
/// Hands out a display list's line instructions as data, per edge, with
/// S-100 Part 9 line suppression already applied, for renderers that draw
/// the portrayal themselves (GIS symbology, vector PDF or SVG).
/// </summary>
/// <remarks>
/// <para>
/// Where a curve is shared by several features (adjoining areas, or a line
/// feature on an area's boundary), it is handed out once, by the instruction
/// drawn last in Part 9 order: the higher display plane and drawing priority,
/// then the later in the display list. A consumer that strokes every edge
/// therefore draws a shared dashed boundary dashed, not as overlapping
/// copies that read solid.
/// </para>
/// <para>
/// This is the suppression <see cref="VectorSceneBuilder"/> applies to the
/// viewer's scene, in the same draw order: the <see cref="LinePaintOp"/>s of
/// a built scene stroke the same runs, projected to EPSG:3857.
/// </para>
/// </remarks>
public static class LineEdges
{
    /// <summary>
    /// Resolves the line instructions of <paramref name="instructions"/> into
    /// the edges left to draw, in S-100 Part 9 draw order.
    /// </summary>
    /// <param name="instructions">The display list, in any order.</param>
    /// <param name="geometryProvider">The geometry of the display list's features.</param>
    /// <returns>
    /// One <see cref="LineEdge"/> per polyline to stroke, in draw order. A
    /// multi-part curve gives one edge per part; a line whose every segment is
    /// suppressed gives none; a line without geometry is left out.
    /// </returns>
    public static IReadOnlyList<LineEdge> Resolve(
        IReadOnlyList<DrawingInstruction> instructions,
        IFeatureGeometryProvider geometryProvider)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(geometryProvider);

        var sorted = VectorSceneBuilder.DrawOrder(instructions);
        var polylines = new Dictionary<LineInstruction, IReadOnlyList<IReadOnlyList<GeoPosition>>?>(ReferenceEqualityComparer.Instance);
        IReadOnlyList<IReadOnlyList<GeoPosition>>? PolylinesOf(LineInstruction line)
        {
            if (!polylines.TryGetValue(line, out var parts))
                polylines[line] = parts = VectorSceneBuilder.LinePolylines(line, geometryProvider);
            return parts;
        }

        var suppressed = LineSuppression.Apply(sorted, PolylinesOf);

        var edges = new List<LineEdge>();
        foreach (var instruction in sorted)
        {
            if (instruction is not LineInstruction line)
                continue;

            IEnumerable<IReadOnlyList<GeoPosition>>? kept = suppressed.TryGetValue(line, out var runs)
                ? runs
                : PolylinesOf(line);
            if (kept is null)
                continue;

            foreach (var polyline in kept)
            {
                if (polyline.Count >= 2)
                    edges.Add(new LineEdge(line, polyline));
            }
        }

        return edges;
    }
}
