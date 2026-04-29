namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// A unified, resource-unresolved S-100 Part 9 drawing instruction. Instances
/// reference catalogue resources (symbols, line styles, area fills, fonts) by
/// name, and reference dataset features by id (<see cref="FeatureReference"/>);
/// the renderer is responsible for resolving geometry and visual resources.
/// </summary>
/// <remarks>
/// This shape matches the S-100 Part 9 display list produced by both the
/// XSLT-style portrayal pipelines (S-124, S-129, S-421) and the Lua-style
/// portrayal pipeline (S-101 — see
/// <c>EncDotNet.S100.Datasets.S101.DrawingInstructionParser</c>).
/// </remarks>
public abstract class DrawingInstruction
{
    /// <summary>
    /// Identifier of the dataset feature this instruction portrays. The
    /// renderer uses this to look up geometry from the dataset.
    /// </summary>
    public required string FeatureReference { get; init; }

    /// <summary>Whether the instruction renders under or over the radar overlay.</summary>
    public DisplayPlane Plane { get; init; } = DisplayPlane.UnderRadar;

    /// <summary>Viewing group controlling visibility of this instruction.</summary>
    public int ViewingGroup { get; init; }

    /// <summary>
    /// Drawing priority within the display plane (ascending, back-to-front).
    /// Within the same priority, the S-100 Part 9 type order applies:
    /// areas → lines → points → text.
    /// </summary>
    public int DrawingPriority { get; init; }

    /// <summary>
    /// Minimum display scale denominator at which this instruction is visible
    /// (i.e. zoomed-out limit). Null means no lower bound.
    /// </summary>
    public double? ScaleMinimum { get; init; }

    /// <summary>
    /// Maximum display scale denominator at which this instruction is visible
    /// (i.e. zoomed-in limit). Null means no upper bound.
    /// </summary>
    public double? ScaleMaximum { get; init; }

    /// <summary>
    /// Returns a numeric type order for S-100 Part 9 intra-priority sorting:
    /// areas (0) → lines (1) → points (2) → text (3).
    /// </summary>
    internal abstract int TypeSortOrder { get; }
}

/// <summary>
/// A point-symbol placement instruction. Geometry is the feature's point
/// position, resolved by <see cref="DrawingInstruction.FeatureReference"/>.
/// </summary>
public sealed class PointInstruction : DrawingInstruction
{
    /// <summary>Reference (by name) to a symbol in the portrayal catalogue.</summary>
    public string? SymbolReference { get; init; }

    /// <summary>Symbol scale factor (1.0 = nominal size).</summary>
    public double SymbolScale { get; init; } = 1.0;

    /// <summary>Symbol rotation in degrees (clockwise from north). Null means upright.</summary>
    public double? Rotation { get; init; }

    /// <summary>Local horizontal offset in symbol units.</summary>
    public double LocalOffsetX { get; init; }

    /// <summary>Local vertical offset in symbol units.</summary>
    public double LocalOffsetY { get; init; }

    /// <summary>
    /// Optional fractional position (0.0–1.0) along the feature's curve geometry
    /// at which to anchor the symbol. Used for line-placed symbols.
    /// </summary>
    public double? LinePlacementPosition { get; init; }

    internal override int TypeSortOrder => 2;
}

/// <summary>
/// A line rendering instruction. Geometry is the feature's curve, resolved by
/// <see cref="DrawingInstruction.FeatureReference"/>.
/// </summary>
public sealed class LineInstruction : DrawingInstruction
{
    /// <summary>Reference (by name) to a line style in the portrayal catalogue.</summary>
    public string? LineStyleReference { get; init; }

    /// <summary>Pen width (mm). Zero means use the catalogue default.</summary>
    public double LineWidth { get; init; }

    /// <summary>S-100 colour token (e.g. <c>CHBLK</c>) for the line stroke.</summary>
    public string? LineColor { get; init; }

    /// <summary>
    /// Optional dash pattern as a sequence of (offset-mm, length-mm) pairs.
    /// </summary>
    public IReadOnlyList<(double Offset, double Length)>? Dashes { get; init; }

    internal override int TypeSortOrder => 1;
}

/// <summary>
/// An area fill instruction. Geometry is the feature's surface (one or more
/// rings), resolved by <see cref="DrawingInstruction.FeatureReference"/>.
/// </summary>
public sealed class AreaInstruction : DrawingInstruction
{
    /// <summary>Reference (by name) to an area fill in the portrayal catalogue.</summary>
    public string? AreaFillReference { get; init; }

    /// <summary>
    /// S-100 colour token for a solid-colour fill. When set,
    /// <see cref="AreaFillReference"/> denotes a pattern fill is not in use.
    /// </summary>
    public string? FillColor { get; init; }

    /// <summary>Optional fill transparency (0.0 = opaque, 1.0 = fully transparent).</summary>
    public double? Transparency { get; init; }

    /// <summary>Optional reference (by name) to a line style for the area outline.</summary>
    public string? OutlineStyleReference { get; init; }

    internal override int TypeSortOrder => 0;
}

/// <summary>
/// A text label rendering instruction. Anchor position is the feature's
/// representative point, resolved by <see cref="DrawingInstruction.FeatureReference"/>.
/// </summary>
public sealed class TextInstruction : DrawingInstruction
{
    /// <summary>The literal text to render.</summary>
    public required string Text { get; init; }

    /// <summary>Optional reference (by name) to a font in the portrayal catalogue.</summary>
    public string? FontReference { get; init; }

    /// <summary>Font size (points).</summary>
    public double FontSize { get; init; } = 10.0;

    /// <summary>S-100 colour token (e.g. <c>CHBLK</c>) for the text foreground.</summary>
    public string FontColor { get; init; } = "CHBLK";

    /// <summary>Text rotation in degrees (clockwise from north). Null means upright.</summary>
    public double? Rotation { get; init; }

    /// <summary>
    /// Optional fractional position (0.0–1.0) along the feature's curve geometry
    /// at which to anchor the text. Used for line-placed labels.
    /// </summary>
    public double? LinePlacementPosition { get; init; }

    internal override int TypeSortOrder => 3;
}
