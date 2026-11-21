namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Gives the chart's safety contour a halo, so it stays the strongest line
/// when another product draws many contours over the chart, as AML
/// Contour Line Bathymetry (CLB) does (AML design handoff decision 22). The
/// contour keeps its own weight, colour and draw order; only a halo in the
/// palette's background is added under it.
/// </summary>
/// <remarks>
/// <para>
/// It works on a display list after the portrayal rules and the display
/// filters have run, so turning it on or off never re-runs the rules. The
/// safety contour is the simple line S-101 <c>DEPARE03</c> draws in
/// <see cref="SafetyContourColorToken"/> (solid or, where the safety contour
/// is not at a charted depth, dashed). For each such line the halo, a solid
/// <see cref="HaloColorToken"/> line <see cref="HaloWidth"/> wide on the same
/// geometry, display plane, viewing group, priority, scales and time, goes in
/// just before the first safety-contour line of its plane and priority. The
/// list stays in its <c>(plane, priority)</c> order, and all the halos of a
/// priority are under all its safety-contour lines, so one edge's halo never
/// cuts across the next edge's line where they meet.
/// </para>
/// <para>
/// A host turns it on with <c>RenderContext.SafetyContourHalo</c>; the S-101
/// and S-57 processors apply it. Because the halos are drawing instructions,
/// tile caches keyed on the instructions tell the two states apart.
/// </para>
/// </remarks>
public static class SafetyContourHalo
{
    /// <summary>The colour token of the safety contour: <c>DEPSC</c>.</summary>
    public const string SafetyContourColorToken = "DEPSC";

    /// <summary>The colour token of the halo: <c>UIBCK</c>, the user interface background (white by day, black by night).</summary>
    public const string HaloColorToken = "UIBCK";

    /// <summary>The halo's width, in millimetres: 5 px of 0.32 mm, as the AML overlay's own halo.</summary>
    public const double HaloWidth = 5 * 0.32;

    /// <summary>
    /// <paramref name="instructions"/> with a halo under every safety-contour
    /// line, or <paramref name="instructions"/> itself when it has none.
    /// </summary>
    /// <param name="instructions">A display list in <c>(plane, priority)</c> order.</param>
    public static IReadOnlyList<DrawingInstruction> Apply(IReadOnlyList<DrawingInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        Dictionary<(DisplayPlane, int), List<DrawingInstruction>>? halos = null;
        foreach (var instruction in instructions)
        {
            if (IsSafetyContour(instruction))
            {
                halos ??= [];
                var key = (instruction.Plane, instruction.DrawingPriority);
                if (!halos.TryGetValue(key, out var list))
                    halos[key] = list = [];
                list.Add(Halo((LineInstruction)instruction));
            }
        }

        if (halos is null)
            return instructions;

        var result = new List<DrawingInstruction>(instructions.Count + halos.Values.Sum(l => l.Count));
        foreach (var instruction in instructions)
        {
            if (IsSafetyContour(instruction) && halos.Remove((instruction.Plane, instruction.DrawingPriority), out var group))
                result.AddRange(group);
            result.Add(instruction);
        }

        return result;
    }

    /// <summary>Whether <paramref name="instruction"/> draws the safety contour: a simple line in <see cref="SafetyContourColorToken"/>.</summary>
    /// <param name="instruction">The instruction.</param>
    public static bool IsSafetyContour(DrawingInstruction instruction) =>
        instruction is LineInstruction
        {
            LineStyleReference: LineInstruction.SimpleLineStyleReference,
            LineColor: SafetyContourColorToken,
        };

    private static LineInstruction Halo(LineInstruction line) => new()
    {
        FeatureReference = line.FeatureReference,
        Plane = line.Plane,
        ViewingGroup = line.ViewingGroup,
        AdditionalViewingGroups = line.AdditionalViewingGroups,
        DrawingPriority = line.DrawingPriority,
        ScaleMinimum = line.ScaleMinimum,
        ScaleMaximum = line.ScaleMaximum,
        TimeValid = line.TimeValid,
        LineStyleReference = LineInstruction.SimpleLineStyleReference,
        LineColor = HaloColorToken,
        LineWidth = Math.Max(HaloWidth, line.LineWidth),
        CoordinatesOverride = line.CoordinatesOverride,

        // Unsuppressed (LineInstructionUnsuppressed): it lies on the same edge
        // as the contour and as other features' lines, so S-100 Part 9 line
        // suppression would otherwise drop it there.
        Suppression = false,
    };
}
