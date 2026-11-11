using System.Globalization;
using System.Security;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// The Contour Line Bathymetry (CLB) product: planning bathymetry with many
/// more contours than an ENC, drawn so it never competes with the chart
/// (AML design handoff decision 22, frame B2e). The AML 3.0 CLB trials use
/// the same classes.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Class</term><description>Drawn as</description></listheader>
/// <item><term><c>DEPCNT</c> depth contour</term><description>A solid 0.7 px line in seabed brown at 55 %, its depth in small italic brown at its middle.</description></item>
/// <item><term><c>DEPARE</c> depth area</term><description>Nothing: no fill, and its edges are the contours. Still pickable.</description></item>
/// <item><term><c>SOUNDG</c> sounding</term><description>Each sounding as a small italic brown figure.</description></item>
/// </list>
/// <para>
/// The three are chart classes (decision 19), but CLB is the one product
/// drawn in its own style rather than the chart's: drawn with S-101 portrayal,
/// its depth areas would look exactly like the chart and bring a second safety
/// contour (§7 Q4). They draw on their own sub-layer, <see cref="Bathymetry"/>,
/// at the bottom of the AML plane.
/// </para>
/// <para>
/// <b>Not for depth checks (decision 23).</b> Nothing here feeds depth
/// answers or route checks. While CLB is drawn over an ENC, the host keeps
/// the ENC's safety contour dominant with a halo (<c>RenderContext.SafetyContourHalo</c>),
/// and shows the corner chip <see cref="NotForDepthChecksChip"/>.
/// </para>
/// </remarks>
public static class ClbStyles
{
    /// <summary>
    /// The text of the corner chip a host shows while CLB is drawn (decision
    /// 23): "AML · CLB not used for depth checks". The chip is viewer chrome
    /// (slice S6, #1374).
    /// </summary>
    public const string NotForDepthChecksChip = "AML · CLB not used for depth checks";

    /// <summary>The pen of a contour: 0.7 px.</summary>
    public const double ContourWidth = 0.7 * AmlSignature.PixelMm;

    /// <summary>The size of a contour's depth label, in S-100 points.</summary>
    public const double ContourLabelSize = 8.5;

    /// <summary>The size of a sounding's figure, in S-100 points.</summary>
    public const double SoundingSize = 8.0;

    /// <summary>The drawing priority of contours within the sub-layer.</summary>
    public const int ContourPriority = AmlDrawingContext.LinePriority;

    /// <summary>The drawing priority of soundings within the sub-layer.</summary>
    public const int SoundingPriority = AmlDrawingContext.LabelPriority;

    /// <summary>
    /// The family CLB draws in: seabed brown (decision 16) at 55 % for the
    /// contours, on its own sub-layer <c>aml.bathymetry</c> at the bottom of
    /// the AML plane. Its labels and soundings are in the full
    /// <see cref="AmlFamily.Seabed"/> ink.
    /// </summary>
    public static AmlFamily Bathymetry { get; } = new(
        "bathymetry",
        "Bathymetry",
        "AMLBATHY",
        PaletteColors.FromHex("#7A4B1F8C", "#8A5A2E8C", "#6B4A2B8C"),
        order: 5,
        viewingGroup: 45005);

    /// <summary>Depth contours: thin brown lines at 55 %, italic depth labels.</summary>
    public static AmlClassStyle DepthContour { get; } = new("DEPCNT", Bathymetry, AmlClassKind.Line, "Depth contour")
    {
        IsChartClass = true,
        Label = feature => Depth(feature.Attribute("VALDCO")),
        Build = BuildContour,
        Legend = (catalog, palette) =>
        [
            new LineLegendEntry(
                new LegendLineSample { Color = catalog.Palette.Resolve(Bathymetry.ColorToken, palette).ToHex(), Width = ContourWidth },
                "Depth contour"),
        ],
    };

    /// <summary>Depth areas: no fill and no boundary, since their edges are the contours.</summary>
    public static AmlClassStyle DepthArea { get; } = new("DEPARE", Bathymetry, AmlClassKind.Area, "Depth area (no fill)")
    {
        IsChartClass = true,
        Build = static (_, _) => [],
        Legend = static (_, _) => [new SwatchLegendEntry("#00000000", "Depth area (no fill)")],
    };

    /// <summary>Soundings: small italic brown figures.</summary>
    public static AmlClassStyle Sounding { get; } = new("SOUNDG", Bathymetry, AmlClassKind.Point, "Sounding")
    {
        IsChartClass = true,
        Build = BuildSoundings,
        Legend = (catalog, palette) => [new SymbolLegendEntry(SoundingSample(catalog.Palette.Resolve(AmlFamily.Seabed.ColorToken, palette)), "Sounding")],
    };

    /// <summary>The CLB module, in legend order.</summary>
    public static IAmlStyleModule Module { get; } = new AmlStyleModule("CLB", [DepthContour, DepthArea, Sounding]);

    /// <summary>
    /// A depth as CLB labels it: the value as encoded, without trailing zeros
    /// (<c>34</c>, <c>2.5</c>); <see langword="null"/> when it isn't a number.
    /// </summary>
    /// <param name="value">The encoded value, such as a <c>VALDCO</c>.</param>
    public static string? Depth(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var depth)
            ? depth.ToString("0.##", CultureInfo.InvariantCulture)
            : null;

    /// <summary>
    /// A sounding's figure: tenths below 31 (<c>12.4</c>), whole units from 31
    /// (<c>48</c>), truncated as charts do, so a figure never shows more water
    /// than there is. A drying height is shown as its magnitude, underlined
    /// on charts; here it is prefixed with a minus.
    /// </summary>
    /// <param name="depth">The depth, positive down.</param>
    public static string SoundingFigure(double depth)
    {
        if (depth < 0)
            return "-" + SoundingFigure(-depth);
        // A hair over, so a depth read as 23.4 isn't floored to 23.3 by binary rounding.
        const double epsilon = 1e-9;
        return depth < 31
            ? (Math.Floor((depth * 10) + epsilon) / 10).ToString("0.#", CultureInfo.InvariantCulture)
            : Math.Floor(depth + epsilon).ToString("0", CultureInfo.InvariantCulture);
    }

    private static TextInstructionStyle Text(AmlDrawingContext context, double size) => context.Label with
    {
        FontSize = size,
        FontColor = AmlFamily.Seabed.ColorToken,
    };

    private static IEnumerable<DrawingInstruction> BuildContour(AmlFeature feature, AmlDrawingContext context)
    {
        if (feature.Curves.Count == 0)
            yield break;

        yield return context.At(ContourPriority).Line(feature.Id, Bathymetry.ColorToken, ContourWidth);
        if (DepthContour.Label(feature) is not { } label)
            yield break;

        var text = context.At(AmlDrawingContext.LabelPriority).Text(feature.Id, label, Text(context, ContourLabelSize));
        yield return new TextInstruction
        {
            FeatureReference = text.FeatureReference,
            Text = text.Text,
            FontSize = text.FontSize,
            FontColor = text.FontColor,
            FontSlant = text.FontSlant,
            HaloColor = text.HaloColor,
            HaloWidth = text.HaloWidth,
            HorizontalAlignment = text.HorizontalAlignment,
            VerticalAlignment = text.VerticalAlignment,
            LinePlacementPosition = 0.5,
            ViewingGroup = text.ViewingGroup,
            DrawingPriority = text.DrawingPriority,
            ScaleMinimum = text.ScaleMinimum,
        };
    }

    private static IEnumerable<DrawingInstruction> BuildSoundings(AmlFeature feature, AmlDrawingContext context)
    {
        var style = Text(context, SoundingSize);
        var builder = context.At(SoundingPriority);
        foreach (var sounding in feature.Soundings)
        {
            var text = builder.Text(feature.Id, SoundingFigure(sounding.Depth), style);
            yield return new TextInstruction
            {
                FeatureReference = text.FeatureReference,
                Text = text.Text,
                FontSize = text.FontSize,
                FontColor = text.FontColor,
                FontSlant = text.FontSlant,
                HaloColor = text.HaloColor,
                HaloWidth = text.HaloWidth,
                HorizontalAlignment = text.HorizontalAlignment,
                VerticalAlignment = text.VerticalAlignment,
                CoordinateOverride = sounding.Position,
                ViewingGroup = text.ViewingGroup,
                DrawingPriority = text.DrawingPriority,
                ScaleMinimum = text.ScaleMinimum,
            };
        }
    }

    // A legend sample: "12" in small italic brown.
    private static string SoundingSample(RgbaColor ink) =>
        $"""<svg xmlns="http://www.w3.org/2000/svg" width="8mm" height="5mm" viewBox="-4 -2.5 8 5"><text x="0" y="1.1" text-anchor="middle" font-family="sans-serif" font-style="italic" font-size="3.2" fill="{SecurityElement.Escape(ink.ToHex())}">12</text></svg>""";
}
