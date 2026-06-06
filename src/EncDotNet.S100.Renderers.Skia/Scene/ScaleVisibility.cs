namespace EncDotNet.S100.Renderers.Skia.Scene;

/// <summary>
/// Shared S-100 Part 9 §11.1 scale-visibility semantics. Centralised so the
/// headless Skia backend (which evaluates a single fixed scale per render) and
/// the Mapsui backend (which evaluates per-frame via <c>Min/MaxVisible</c>)
/// agree on the inclusion rule, including at the boundaries.
/// </summary>
public static class ScaleVisibility
{
    /// <summary>
    /// S-100 Part 9 scale denominator → Mapsui resolution (m/px in EPSG:3857)
    /// at 96 DPI: 1 px = 0.28 mm = 0.00028 m on the nominal display surface,
    /// so resolution ≈ scaleDenominator × 0.00028. Exposed so the Mapsui
    /// adapter and this helper derive their bounds from the same constant.
    /// </summary>
    public const double DenomToResolutionMetres = 0.00028;

    /// <summary>
    /// Returns whether an op is visible at the given display scale denominator.
    /// </summary>
    /// <remarks>
    /// Per the S-100 Part 9 §11.1 convention (and the field docs on
    /// <c>DrawingInstruction</c>), <c>ScaleMinimum</c> is the most zoomed-out
    /// limit — the <i>largest</i> allowed denominator — and <c>ScaleMaximum</c>
    /// is the most zoomed-in limit — the <i>smallest</i> allowed denominator.
    /// An op is therefore visible when
    /// <c>ScaleMaximum ≤ denominator ≤ ScaleMinimum</c> (bounds inclusive,
    /// matching Mapsui's inclusive <c>Min/MaxVisible</c> comparison).
    /// </remarks>
    public static bool IsVisibleAtScale(PaintOp op, double scaleDenominator)
    {
        ArgumentNullException.ThrowIfNull(op);

        if (op.ScaleMaximum.HasValue && scaleDenominator < op.ScaleMaximum.Value)
            return false;
        if (op.ScaleMinimum.HasValue && scaleDenominator > op.ScaleMinimum.Value)
            return false;
        return true;
    }
}
