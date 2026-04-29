using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Merges consecutive S-101 SAFCON point-symbol instructions into a single
/// depth-label text instruction.
/// </summary>
/// <remarks>
/// The S-101 Lua portrayal emits depth contour and sounding labels as
/// sequences of <c>PointInstruction</c> entries referencing
/// <c>SAFCONxy</c> symbols, where each symbol represents one positioned
/// digit glyph in an SVG composition. This helper decodes the sequence
/// back into a depth text string and emits a single
/// <see cref="TextInstruction"/> in its place.
/// <para>SAFCON encoding (from SAFCON01.lua):</para>
/// <list type="bullet">
///   <item>Row 0: single/middle digit</item>
///   <item>Row 1: units of 2-digit</item>
///   <item>Row 2: tens of 2-digit</item>
///   <item>Row 3: first of 4-digit</item>
///   <item>Row 4: first of 5-digit</item>
///   <item>Row 5: fractional (depth 10–30)</item>
///   <item>Row 6: fractional (depth &lt;10)</item>
///   <item>Row 7: last digit of 4/5-digit</item>
///   <item>Row 8: first of 3-digit</item>
///   <item>Row 9: third of 3-digit</item>
/// </list>
/// </remarks>
public static class S101SafconLabelMerger
{
    /// <summary>
    /// Returns a new list in which runs of <see cref="PointInstruction"/>
    /// entries with <c>SAFCON*</c> symbol references that share a feature
    /// reference are replaced by a single <see cref="TextInstruction"/>.
    /// Other instructions are passed through unchanged.
    /// </summary>
    public static List<DrawingInstruction> Merge(IReadOnlyList<DrawingInstruction> instructions)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        var result = new List<DrawingInstruction>(instructions.Count);

        var i = 0;
        while (i < instructions.Count)
        {
            var instr = instructions[i];

            if (instr is PointInstruction p && IsSafconSymbol(p.SymbolReference))
            {
                var safcons = new List<PointInstruction> { p };
                var j = i + 1;
                while (j < instructions.Count &&
                       instructions[j] is PointInstruction pj &&
                       pj.FeatureReference == p.FeatureReference &&
                       IsSafconSymbol(pj.SymbolReference))
                {
                    safcons.Add(pj);
                    j++;
                }

                var depthText = DecodeSafconSequence(safcons);

                result.Add(new TextInstruction
                {
                    FeatureReference = p.FeatureReference,
                    Text = depthText,
                    ViewingGroup = p.ViewingGroup,
                    DrawingPriority = p.DrawingPriority,
                    Plane = p.Plane,
                    FontSize = 10,
                    FontColor = "DEPCN",
                    LinePlacementPosition = p.LinePlacementPosition,
                    ScaleMinimum = p.ScaleMinimum,
                    ScaleMaximum = p.ScaleMaximum,
                });

                i = j;
            }
            else
            {
                result.Add(instr);
                i++;
            }
        }

        return result;
    }

    private static bool IsSafconSymbol(string? symbolRef) =>
        symbolRef is not null &&
        symbolRef.StartsWith("SAFCON", StringComparison.Ordinal) &&
        symbolRef.Length == 8;

    private static string DecodeSafconSequence(List<PointInstruction> safcons)
    {
        var sb = new System.Text.StringBuilder();

        foreach (var instr in safcons)
        {
            var name = instr.SymbolReference!;
            // SAFCONxy — x is row (position type), y is digit
            var row = name[6] - '0';
            var digit = name[7];

            if (row == 5 || row == 6)
            {
                // Fractional digit — prepend decimal point
                sb.Append('.');
            }

            sb.Append(digit);
        }

        return sb.ToString();
    }
}
