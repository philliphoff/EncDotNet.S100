using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Aml;

/// <summary>How an AML class is drawn, and shown in the legend.</summary>
public enum AmlClassKind
{
    /// <summary>An area: an unfilled ticked boundary and its name inside. Line features of the class draw as lines.</summary>
    Area,

    /// <summary>A line: the dash-dot signature and its name at its middle.</summary>
    Line,

    /// <summary>A route leg: the heavier dash-dot with chevrons in the direction of the leg.</summary>
    Route,

    /// <summary>A point: the class's glyph in the AML frame, and its name to the right.</summary>
    Point,
}

/// <summary>
/// The style of one AML object class: its colour family, how it's drawn, its
/// label and its legend entry. The portrayal looks each feature's class up by
/// acronym in an <see cref="AmlStyleCatalog"/>; a class without a style is not
/// drawn, but is still pickable.
/// </summary>
/// <remarks>
/// <para>
/// A style is data: most classes need only a family, a <see cref="Kind"/> and,
/// for points, a <see cref="Glyph"/>. The drawing is the shared AML overlay
/// signature (<see cref="AmlSignature"/>), so every class reads as AML.
/// <see cref="Build"/> is the escape hatch for a class that needs more, such
/// as a pattern fill; it replaces the shared drawing for that class.
/// </para>
/// <para>
/// <b>Standard classes (decision 19).</b> A class that is also an IHO S-57
/// chart class, such as <c>MIPARE</c>, is marked <see cref="IsChartClass"/>.
/// The AML portrayal draws only its AML signature (the ticked boundary and the
/// label) and never the class's chart symbol: the chart symbol comes from the
/// ENC under it, when the ENC has the feature. AML cells are never drawn with
/// S-101 portrayal (decision 27).
/// </para>
/// </remarks>
/// <param name="Acronym">The class's acronym in the AML object catalogue, such as <c>airres</c> or <c>MIPARE</c>.</param>
/// <param name="Family">The class's colour family.</param>
/// <param name="Kind">How the class is drawn.</param>
/// <param name="LegendLabel">The class's name in the legend, such as "Airspace restriction".</param>
public sealed record AmlClassStyle(string Acronym, AmlFamily Family, AmlClassKind Kind, string LegendLabel)
{
    /// <summary>The glyph of a <see cref="AmlClassKind.Point"/> class, drawn in the AML frame.</summary>
    public AmlGlyph? Glyph { get; init; }

    /// <summary>Whether an area shows its vertical limits as a stacked fraction under its name (decision 18).</summary>
    public bool ShowsLimits { get; init; }

    /// <summary>Whether the class is also an IHO S-57 chart class (decision 19).</summary>
    public bool IsChartClass { get; init; }

    /// <summary>The text of a feature's label; <see cref="AmlLabels.Name"/> by default. <see langword="null"/> text draws no label.</summary>
    public Func<AmlFeature, string?> Label { get; init; } = AmlLabels.Name;

    /// <summary>
    /// Draws a feature of the class in place of the shared AML drawing, or
    /// <see langword="null"/> for the shared drawing. Use
    /// <see cref="AmlDrawingContext"/>'s builders so the drawing keeps the
    /// family's viewing group and the feature's <c>SCAMIN</c>.
    /// </summary>
    public Func<AmlFeature, AmlDrawingContext, IEnumerable<DrawingInstruction>>? Build { get; init; }

    /// <summary>
    /// The class's own legend entries in a palette, in place of the one entry
    /// its <see cref="Kind"/> gives, or <see langword="null"/> for that entry.
    /// For a class whose <see cref="Build"/> draws something the kinds don't,
    /// such as CLB's thin contours or sounding figures, or whose drawing needs
    /// several rows, such as sediment's patterns.
    /// </summary>
    public Func<AmlStyleCatalog, PaletteType, IReadOnlyList<LegendEntry>>? Legend { get; init; }

    /// <summary>Checks the style is complete: a point class needs a glyph.</summary>
    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Acronym);
        ArgumentNullException.ThrowIfNull(Family);
        ArgumentException.ThrowIfNullOrWhiteSpace(LegendLabel);
        if (Kind == AmlClassKind.Point && Glyph is null && Build is null)
            throw new ArgumentException($"AML point class '{Acronym}' needs a glyph.");
        Glyph?.Validate();
    }
}

/// <summary>
/// What a class style draws one feature with: builders that carry the
/// family's viewing group and the feature's <c>SCAMIN</c>, and the shared
/// drawing pieces.
/// </summary>
public sealed class AmlDrawingContext
{
    /// <summary>The drawing priority of area boundaries within a family's sub-layer.</summary>
    public const int AreaPriority = 10;

    /// <summary>The drawing priority of lines.</summary>
    public const int LinePriority = 20;

    /// <summary>The drawing priority of route legs.</summary>
    public const int RoutePriority = 22;

    /// <summary>The drawing priority of area and line labels.</summary>
    public const int LabelPriority = 30;

    /// <summary>The drawing priority of point symbols and their labels.</summary>
    public const int PointPriority = 40;

    /// <summary>The label's font size, in S-100 points.</summary>
    public const double FontSize = 10.5;

    /// <summary>The halo round a label, in millimetres.</summary>
    public const double LabelHaloWidth = 0.6;

    // The height of a line of label text, in millimetres, for stacking.
    private const double LineHeight = 3.9;

    internal AmlDrawingContext(AmlFeature feature, AmlFamily family)
    {
        Feature = feature;
        Family = family;
        ScaleMinimum = double.TryParse(feature.Attribute("SCAMIN"), NumberStyles.Float, CultureInfo.InvariantCulture, out var scamin) && scamin > 0
            ? scamin
            : null;
        Label = new TextInstructionStyle
        {
            FontSize = FontSize,
            FontColor = family.ColorToken,
            FontSlant = TextFontSlant.Italics,
            HaloColor = AmlPalette.HaloToken,
            HaloWidth = LabelHaloWidth,
        };
    }

    /// <summary>The feature being drawn.</summary>
    public AmlFeature Feature { get; }

    /// <summary>The family it is drawn in.</summary>
    public AmlFamily Family { get; }

    /// <summary>The feature's <c>SCAMIN</c>: the largest scale denominator it is drawn at; <see langword="null"/> for all.</summary>
    public double? ScaleMinimum { get; }

    /// <summary>The family's label style: italic, in its ink, with a halo (decision 16), centred on the anchor.</summary>
    public TextInstructionStyle Label { get; }

    /// <summary>A builder at <paramref name="drawingPriority"/>, with the family's viewing group and the feature's <c>SCAMIN</c>.</summary>
    /// <param name="drawingPriority">The drawing priority within the family's sub-layer.</param>
    public InstructionBuilder At(int drawingPriority) => new()
    {
        ViewingGroup = Family.ViewingGroup,
        DrawingPriority = drawingPriority,
        ScaleMinimum = ScaleMinimum,
    };

    /// <summary>The ticked area boundary.</summary>
    public DrawingInstruction Boundary() => At(AreaPriority).ComplexLine(Feature.Id, AmlSignature.AreaStyleName(Family));

    /// <summary>The dash-dot line on its halo.</summary>
    public DrawingInstruction Line() => At(LinePriority).ComplexLine(Feature.Id, AmlSignature.LineStyleName(Family));

    /// <summary>The heavier route leg, with chevrons.</summary>
    public DrawingInstruction Route() => At(RoutePriority).ComplexLine(Feature.Id, AmlSignature.RouteStyleName(Family));

    /// <summary>The glyph in the AML frame.</summary>
    /// <param name="glyph">The glyph.</param>
    public DrawingInstruction Symbol(AmlGlyph glyph) => At(PointPriority).Point(Feature.Id, AmlSignature.FramedSymbol(Family, glyph));

    /// <summary>A point's label, to the right of its frame.</summary>
    /// <param name="text">The text.</param>
    public DrawingInstruction PointLabel(string text) => At(PointPriority).Text(
        Feature.Id,
        text,
        Label with { HorizontalAlignment = TextHorizontalAlignment.Start, OffsetX = 3.2 });

    /// <summary>A line's label, above its middle.</summary>
    /// <param name="text">The text.</param>
    public DrawingInstruction LineLabel(string text)
    {
        var instruction = At(LabelPriority).Text(
            Feature.Id,
            text,
            Label with { VerticalAlignment = TextVerticalAlignment.Bottom, OffsetY = -1.2 });
        return new TextInstruction
        {
            FeatureReference = instruction.FeatureReference,
            Text = instruction.Text,
            FontSize = instruction.FontSize,
            FontColor = instruction.FontColor,
            FontSlant = instruction.FontSlant,
            HaloColor = instruction.HaloColor,
            HaloWidth = instruction.HaloWidth,
            HorizontalAlignment = instruction.HorizontalAlignment,
            VerticalAlignment = instruction.VerticalAlignment,
            OffsetY = instruction.OffsetY,
            LinePlacementPosition = 0.5,
            ViewingGroup = instruction.ViewingGroup,
            DrawingPriority = instruction.DrawingPriority,
            ScaleMinimum = instruction.ScaleMinimum,
        };
    }

    /// <summary>
    /// An area's label inside it: the name, and under it the limits as a
    /// stacked fraction (upper, a bar, lower) when <paramref name="limits"/> is
    /// given.
    /// </summary>
    /// <param name="name">The name, or <see langword="null"/> for none.</param>
    /// <param name="limits">The limits, or <see langword="null"/> for none.</param>
    public IEnumerable<DrawingInstruction> AreaLabel(string? name, (string Upper, string Lower)? limits)
    {
        var builder = At(LabelPriority);
        if (limits is not { } fraction)
        {
            if (name is not null)
                yield return builder.Text(Feature.Id, name, Label);
            yield break;
        }

        // The bar sits on the anchor, the limits either side of it, the name above.
        yield return builder.Point(Feature.Id, AmlSignature.BarSymbol(Family));
        yield return builder.Text(Feature.Id, fraction.Upper, Label with { VerticalAlignment = TextVerticalAlignment.Bottom, OffsetY = -0.5 });
        yield return builder.Text(Feature.Id, fraction.Lower, Label with { VerticalAlignment = TextVerticalAlignment.Top, OffsetY = 0.5 });
        if (name is not null)
            yield return builder.Text(Feature.Id, name, Label with { VerticalAlignment = TextVerticalAlignment.Bottom, OffsetY = -0.5 - LineHeight });
    }
}
