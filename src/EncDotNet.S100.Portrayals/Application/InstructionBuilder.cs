using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals.Application;

/// <summary>
/// Builds the S-100 Part 9 drawing instructions of a portrayal defined in
/// code: point symbols, text, lines and areas. The builder carries what a
/// catalogue rule would set before its drawing commands (viewing group,
/// drawing priority, display plane and scale limits) and stamps them on every
/// instruction it builds.
/// </summary>
/// <remarks>
/// The builder is immutable. Derive a variant with a <c>with</c> expression,
/// e.g. labels that share the symbols' plane and priority but are only shown
/// at larger scales:
/// <code>
/// var symbols = new InstructionBuilder { ViewingGroup = 31050, DrawingPriority = 15 };
/// var labels = symbols with { ScaleMinimum = 100_000 };
/// </code>
/// The instructions are ordinary <see cref="DrawingInstruction"/>s, so they
/// follow the same rules as a catalogue product's: viewing groups, SCAMIN,
/// symbol and text scale, label declutter and picking.
/// </remarks>
public sealed record InstructionBuilder
{
    /// <summary>The viewing group stamped on each instruction. Defaults to 0.</summary>
    public int ViewingGroup { get; init; }

    /// <summary>The drawing priority stamped on each instruction. Defaults to 0.</summary>
    public int DrawingPriority { get; init; }

    /// <summary>
    /// The display plane stamped on each instruction. Defaults to
    /// <see cref="EncDotNet.S100.Pipelines.Vector.DisplayPlane.UnderRadar"/>, as <see cref="DrawingInstruction.Plane"/> does.
    /// </summary>
    public EncDotNet.S100.Pipelines.Vector.DisplayPlane Plane { get; init; } =
        EncDotNet.S100.Pipelines.Vector.DisplayPlane.UnderRadar;

    /// <summary>
    /// The scale minimum (SCAMIN) stamped on each instruction: the largest
    /// scale denominator it is drawn at. <see langword="null"/> (the default)
    /// draws it at every scale.
    /// </summary>
    public double? ScaleMinimum { get; init; }

    /// <summary>
    /// The scale maximum stamped on each instruction: the smallest scale
    /// denominator it is drawn at. <see langword="null"/> (the default) sets no
    /// limit.
    /// </summary>
    public double? ScaleMaximum { get; init; }

    /// <summary>Builds a point-symbol instruction.</summary>
    /// <param name="featureReference">The feature the symbol is drawn at.</param>
    /// <param name="symbolReference">The symbol name, resolved by the sub-layer's symbol provider (see <see cref="SymbolSet"/>).</param>
    /// <param name="rotation">Clockwise rotation in degrees, or <see langword="null"/> for upright.</param>
    /// <param name="rotationCrs">The frame <paramref name="rotation"/> is measured in.</param>
    public PointInstruction Point(
        string featureReference,
        string symbolReference,
        double? rotation = null,
        SymbolRotationCrs rotationCrs = SymbolRotationCrs.Portrayal)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureReference);
        ArgumentException.ThrowIfNullOrEmpty(symbolReference);

        return new PointInstruction
        {
            FeatureReference = featureReference,
            SymbolReference = symbolReference,
            Rotation = rotation,
            RotationCrs = rotationCrs,
            Plane = Plane,
            ViewingGroup = ViewingGroup,
            DrawingPriority = DrawingPriority,
            ScaleMinimum = ScaleMinimum,
            ScaleMaximum = ScaleMaximum,
        };
    }

    /// <summary>Builds a text instruction.</summary>
    /// <param name="featureReference">The feature the text is anchored at.</param>
    /// <param name="text">The text.</param>
    /// <param name="style">The font, colours, halo, alignment and offset.</param>
    public TextInstruction Text(string featureReference, string text, TextInstructionStyle style)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureReference);
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(style);

        return new TextInstruction
        {
            FeatureReference = featureReference,
            Text = text,
            FontSize = style.FontSize,
            FontColor = style.FontColor,
            FontWeight = style.FontWeight,
            FontProportion = style.FontProportion,
            HaloColor = style.HaloColor,
            HaloWidth = style.HaloWidth,
            HorizontalAlignment = style.HorizontalAlignment,
            VerticalAlignment = style.VerticalAlignment,
            OffsetX = style.OffsetX,
            OffsetY = style.OffsetY,
            Plane = Plane,
            ViewingGroup = ViewingGroup,
            DrawingPriority = DrawingPriority,
            ScaleMinimum = ScaleMinimum,
            ScaleMaximum = ScaleMaximum,
        };
    }

    /// <summary>
    /// Builds a simple-line instruction (the
    /// <see cref="LineInstruction.SimpleLineStyleReference"/> style): one colour and
    /// width, solid or dashed.
    /// </summary>
    /// <param name="featureReference">The feature whose curve is stroked.</param>
    /// <param name="colorToken">The stroke's colour token.</param>
    /// <param name="width">The pen width in millimetres.</param>
    /// <param name="dashLength">The dash length in millimetres; 0 for a solid line.</param>
    /// <param name="gapLength">The gap between dashes in millimetres; 0 for a solid line.</param>
    public LineInstruction Line(
        string featureReference,
        string colorToken,
        double width,
        double dashLength = 0,
        double gapLength = 0)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureReference);
        ArgumentException.ThrowIfNullOrEmpty(colorToken);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(dashLength);
        ArgumentOutOfRangeException.ThrowIfNegative(gapLength);

        bool dashed = dashLength > 0 && gapLength > 0;
        return new LineInstruction
        {
            FeatureReference = featureReference,
            LineStyleReference = LineInstruction.SimpleLineStyleReference,
            LineColor = colorToken,
            LineWidth = width,
            // The renderers read the gap from the dash entry and the dash from
            // DashOnLength, as the S-101 Lua portrayal emits them.
            Dashes = dashed ? [(0.0, gapLength)] : null,
            DashOnLength = dashed ? dashLength : 0,
            Plane = Plane,
            ViewingGroup = ViewingGroup,
            DrawingPriority = DrawingPriority,
            ScaleMinimum = ScaleMinimum,
            ScaleMaximum = ScaleMaximum,
        };
    }

    /// <summary>Builds a solid colour-fill area instruction.</summary>
    /// <param name="featureReference">The feature whose surface is filled.</param>
    /// <param name="fillColorToken">The fill's colour token.</param>
    /// <param name="transparency">
    /// The fill's transparency, 0 (opaque) to 1 (fully transparent), or
    /// <see langword="null"/> for opaque.
    /// </param>
    public AreaInstruction Area(string featureReference, string fillColorToken, double? transparency = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(featureReference);
        ArgumentException.ThrowIfNullOrEmpty(fillColorToken);
        if (transparency is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(transparency), transparency, "Transparency must be between 0 and 1.");

        return new AreaInstruction
        {
            FeatureReference = featureReference,
            FillColor = fillColorToken,
            Transparency = transparency,
            Plane = Plane,
            ViewingGroup = ViewingGroup,
            DrawingPriority = DrawingPriority,
            ScaleMinimum = ScaleMinimum,
            ScaleMaximum = ScaleMaximum,
        };
    }
}

/// <summary>
/// How an <see cref="InstructionBuilder"/> draws text: font, colour tokens,
/// halo, alignment and offset. The defaults match a
/// <see cref="TextInstruction"/>'s.
/// </summary>
public sealed record TextInstructionStyle
{
    /// <summary>The font size, which the mariner's text scale multiplies. Defaults to 10.</summary>
    public double FontSize { get; init; } = 10.0;

    /// <summary>The text's colour token. Defaults to <c>CHBLK</c>.</summary>
    public string FontColor { get; init; } = "CHBLK";

    /// <summary>The font weight.</summary>
    public TextFontWeight FontWeight { get; init; } = TextFontWeight.Medium;

    /// <summary>The font proportion.</summary>
    public TextFontProportion FontProportion { get; init; } = TextFontProportion.Proportional;

    /// <summary>The halo's colour token, or <see langword="null"/> (the default) for no halo.</summary>
    public string? HaloColor { get; init; }

    /// <summary>The halo's width in millimetres.</summary>
    public double HaloWidth { get; init; }

    /// <summary>Horizontal alignment relative to the anchor.</summary>
    public TextHorizontalAlignment HorizontalAlignment { get; init; } = TextHorizontalAlignment.Center;

    /// <summary>Vertical alignment relative to the anchor.</summary>
    public TextVerticalAlignment VerticalAlignment { get; init; } = TextVerticalAlignment.Center;

    /// <summary>Horizontal offset from the anchor in millimetres, or <see langword="null"/> for none.</summary>
    public double? OffsetX { get; init; }

    /// <summary>Vertical offset from the anchor in millimetres, or <see langword="null"/> for none.</summary>
    public double? OffsetY { get; init; }
}
