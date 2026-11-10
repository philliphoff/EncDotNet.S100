using System.Globalization;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// The environmental classes of the AML Environment, Seabed and Beach (ESB)
/// product and of the AML 3.0 SED cells, in the
/// <see cref="AmlFamily.Environmental"/> family (AML design handoff decisions
/// 17 and 21, frame B2d).
/// </summary>
/// <remarks>
/// <para>
/// Every class keeps the AML signature: areas are ticked unfilled boundaries
/// with their name, lines the dash-dot on a halo, points a glyph in the AML
/// frame. The exceptions are sediment (<see cref="EsbSediment"/>: a pattern
/// over a 7 % wash), trawl scours (short parallel strokes along the scour)
/// and beach exits (a solid arrow off the beach).
/// </para>
/// <list type="table">
/// <listheader><term>Class</term><description>Drawn as</description></listheader>
/// <item><term><c>sedlay</c> geological layer</term><description>Sediment pattern over a 7 % wash, thin dash-dot boundary, no label (<see cref="EsbSediment"/>). Points: framed sediment marks and the sediment's name.</description></item>
/// <item><term><c>botmft</c> bottom feature</term><description>Framed chevron; areas and lines in the signature.</description></item>
/// <item><term><c>twlscr</c> trawl scours</term><description>Short parallel strokes along a scour; areas ticked; points three framed strokes.</description></item>
/// <item><term><c>lndzne</c>, <c>lngare</c>, <c>lndste</c>, <c>lndstp</c>, <c>lndpnt</c>, <c>lndplc</c> landing zone, area, site, strip, point and place</term><description>Framed L.</description></item>
/// <item><term><c>drpzne</c> drop zone</term><description>Framed D.</description></item>
/// <item><term><c>bchext</c> beach exit</term><description>A solid line on a halo with chevrons along it, off the beach; points a framed arrow.</description></item>
/// <item><term><c>divloc</c> diving location</term><description>Framed arrow down through a wave.</description></item>
/// <item><term><c>viewpt</c> viewpoint</term><description>Framed eye.</description></item>
/// <item><term><c>resloc</c> resource location</term><description>Framed crate.</description></item>
/// <item><term><c>shlloc</c> shelter location</term><description>Framed house.</description></item>
/// <item><term><c>icebrg</c> iceberg</term><description>Framed iceberg over a waterline.</description></item>
/// <item><term><c>icemov</c> ice movement</term><description>An arrow in the drift direction (<c>icebdr</c>), labelled "drift NE"; with no direction, a framed ice crystal.</description></item>
/// <item><term><c>icelin</c> ice line, <c>bchprf</c> beach profile</term><description>Lines.</description></item>
/// <item><term>Ice leads, polynyas, sea ice, land ice, iceberg areas, and the ESB zones (beach survey, burial probability, bottom tactical data, environmentally sensitive, fishing and leisure activity, imagery coverage, MCM, performance and risk data, trafficability)</term><description>Ticked area and name.</description></item>
/// </list>
/// <para>
/// <b>Ice.</b> The viewer's sea-ice styling is the S-411 portrayal
/// catalogue's, loaded with S-411 data, not code this package can draw with;
/// and its egg-code concentration colours would read as chart data under
/// AML. So the ESB ice classes keep the AML signature in the environmental
/// ink, and the iceberg glyph follows the WMO sea-ice symbol's peaked
/// silhouette over a waterline.
/// </para>
/// <para>
/// <b>Standard classes.</b> ESB cells also carry IHO S-57 chart classes
/// (lights, landmarks, coastline, cables and the like). They are left to the
/// ENC under them and to slice S9's per-class choices, so this module styles
/// only the AML classes.
/// </para>
/// </remarks>
public static class EsbEnvironmentalStyles
{
    private static AmlFamily Env => AmlFamily.Environmental;

    // The resources' names.
    private const string ScourSymbol = "AML_ENVIRONMENTAL_SCOUR";
    private const string DriftSymbol = "AML_ENVIRONMENTAL_DRIFT";
    private const string ScoursStyle = "AML_ENVIRONMENTAL_SCOURS";
    private const string ExitStyle = "AML_ENVIRONMENTAL_EXIT";

    // The distance between scour strokes and between beach-exit chevrons.
    private const double ScourSpacing = 1.6;
    private const double ExitChevronSpacing = 5.0;

    // The beach exit's pen: 1.8 px, as in frame B2d.
    private const double ExitInkWidth = 1.8 * AmlSignature.PixelMm;

    /// <summary>A chevron: a bottom feature.</summary>
    public static AmlGlyph BottomFeatureGlyph { get; } = new(
        "CHEVRON",
        "A chevron: a bottom feature.",
        """<path class="s{ink} f0" stroke-width="0.5" stroke-linejoin="round" stroke-linecap="round" d="M -1.6 1.0 L 0 -1.4 L 1.6 1.0"/>""");

    /// <summary>Three short parallel strokes: trawl scours.</summary>
    public static AmlGlyph ScoursGlyph { get; } = new(
        "SCOURS",
        "Three short parallel strokes: trawl scours.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linecap="round" d="M -1.9 -0.5 L 0.7 -2.0 M -1.3 0.75 L 1.3 -0.75 M -0.7 2.0 L 1.9 0.5"/>""");

    /// <summary>The letter L: a landing zone, area, site, strip, point or place.</summary>
    public static AmlGlyph LandingGlyph { get; } = new(
        "LZ",
        "The letter L: a landing zone.",
        """<path class="s{ink} f0" stroke-width="0.6" stroke-linejoin="miter" d="M -0.9 -1.7 L -0.9 1.5 L 1.2 1.5"/>""");

    /// <summary>The letter D: a drop zone.</summary>
    public static AmlGlyph DropZoneGlyph { get; } = new(
        "DZ",
        "The letter D: a drop zone.",
        """<path class="s{ink} f0" stroke-width="0.6" stroke-linejoin="round" d="M -1.0 -1.6 L -1.0 1.6 L -0.1 1.6 Q 1.4 1.6 1.4 0 Q 1.4 -1.6 -0.1 -1.6 Z"/>""");

    /// <summary>An arrow up and to the right: a beach exit drawn at a point.</summary>
    public static AmlGlyph BeachExitGlyph { get; } = new(
        "EXIT",
        "An arrow: a beach exit.",
        """<path class="s{ink} f0" stroke-width="0.5" stroke-linecap="round" stroke-linejoin="round" d="M -1.7 1.7 L 1.5 -1.5 M -0.1 -1.6 L 1.5 -1.5 L 1.6 0.1"/>""");

    /// <summary>An arrow down through a wave: a diving location.</summary>
    public static AmlGlyph DivingGlyph { get; } = new(
        "DIVE",
        "An arrow down through a wave: a diving location.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linecap="round" stroke-linejoin="round" d="M -2.0 -1.2 Q -1.5 -1.9 -1.0 -1.2 T 0 -1.2 T 1.0 -1.2 T 2.0 -1.2 M 0 -0.5 L 0 1.9 M -0.8 1.1 L 0 1.9 L 0.8 1.1"/>""");

    /// <summary>An eye: a viewpoint.</summary>
    public static AmlGlyph ViewpointGlyph { get; } = new(
        "VIEW",
        "An eye: a viewpoint.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" d="M -2.1 0 Q 0 -1.9 2.1 0 Q 0 1.9 -2.1 0 Z"/><circle class="f{ink}" cx="0" cy="0" r="0.65"/>""");

    /// <summary>A crate: a resource location (drinking water, engineer resources or storage).</summary>
    public static AmlGlyph ResourceGlyph { get; } = new(
        "RES",
        "A crate: a resource location.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" d="M -1.6 -1.3 L 1.6 -1.3 L 1.6 1.5 L -1.6 1.5 Z M -1.6 -0.3 L 1.6 -0.3 M -0.5 -1.3 L -0.5 -0.3 M 0.5 -1.3 L 0.5 -0.3"/>""");

    /// <summary>A house: a shelter location.</summary>
    public static AmlGlyph ShelterGlyph { get; } = new(
        "SHELTER",
        "A house: a shelter location.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" stroke-linecap="round" d="M -1.9 0 L 0 -1.8 L 1.9 0 M -1.3 -0.5 L -1.3 1.7 L 1.3 1.7 L 1.3 -0.5"/>""");

    /// <summary>An iceberg's peaks over a waterline, after the WMO sea-ice symbol: an iceberg.</summary>
    public static AmlGlyph IcebergGlyph { get; } = new(
        "ICEBERG",
        "Peaks over a waterline: an iceberg.",
        """<path class="s{ink} f0" stroke-width="0.45" stroke-linejoin="round" stroke-linecap="round" d="M -1.5 1.0 L -0.6 -0.9 L 0.1 -0.2 L 0.7 -1.6 L 1.5 1.0 M -2.1 1.0 L 2.1 1.0"/>""");

    /// <summary>A six-armed ice crystal: ice movement with no direction.</summary>
    public static AmlGlyph IceGlyph { get; } = new(
        "ICE",
        "A six-armed ice crystal: ice movement with no direction.",
        """<path class="s{ink} f0" stroke-width="0.4" stroke-linecap="round" d="M 0 -1.9 L 0 1.9 M -1.65 -0.95 L 1.65 0.95 M -1.65 0.95 L 1.65 -0.95"/>""");

    /// <summary>A dot, a dash and a ring: a sediment sample at a point.</summary>
    public static AmlGlyph SedimentGlyph { get; } = new(
        "SED",
        "A ring, a dash and a dot: a sediment sample.",
        """<circle class="s{ink} f0" stroke-width="0.4" cx="-0.9" cy="-0.8" r="0.75"/><path class="s{ink} f0" stroke-width="0.45" d="M 0.4 -0.8 L 1.8 -0.8"/><circle class="f{ink}" cx="0" cy="1.1" r="0.45"/>""");

    /// <summary>Geological layers: sediment patterns (<see cref="EsbSediment"/>).</summary>
    public static AmlClassStyle GeologicalLayer { get; } = new(EsbSediment.Acronym, Env, AmlClassKind.Area, "Geological layer (sediment)")
    {
        Glyph = SedimentGlyph,
        Build = EsbSediment.Build,
        Legend = EsbSediment.BuildLegend,
    };

    /// <summary>Bottom features.</summary>
    public static AmlClassStyle BottomFeature { get; } = Area("botmft", "Bottom feature", BottomFeatureGlyph);

    /// <summary>Trawl scours: short parallel strokes along a scour.</summary>
    public static AmlClassStyle TrawlScours { get; } = Area("twlscr", "Trawl scours", ScoursGlyph) with
    {
        Build = (feature, context) => feature.GeometryType == S100GeometryType.Curve
            ? [context.At(AmlDrawingContext.LinePriority).ComplexLine(feature.Id, ScoursStyle)]
            : AmlPortrayal.Build(feature, Area("twlscr", "Trawl scours", ScoursGlyph)),
        Legend = (catalog, palette) => [new LineLegendEntry(ScoursSample(catalog, palette), "Trawl scours")],
    };

    /// <summary>Landing zones.</summary>
    public static AmlClassStyle LandingZone { get; } = Area("lndzne", "Landing zone", LandingGlyph);

    /// <summary>Landing areas.</summary>
    public static AmlClassStyle LandingArea { get; } = Area("lngare", "Landing area", LandingGlyph);

    /// <summary>Landing sites.</summary>
    public static AmlClassStyle LandingSite { get; } = Area("lndste", "Landing site", LandingGlyph);

    /// <summary>Landing strips.</summary>
    public static AmlClassStyle LandingStrip { get; } = Area("lndstp", "Landing strip", LandingGlyph);

    /// <summary>Landing points.</summary>
    public static AmlClassStyle LandingPoint { get; } = Point("lndpnt", "Landing point", LandingGlyph);

    /// <summary>Landing places.</summary>
    public static AmlClassStyle LandingPlace { get; } = Point("lndplc", "Landing place", LandingGlyph);

    /// <summary>Drop zones.</summary>
    public static AmlClassStyle DropZone { get; } = Area("drpzne", "Drop zone", DropZoneGlyph);

    /// <summary>Beach exits: a solid arrow off the beach.</summary>
    public static AmlClassStyle BeachExit { get; } = new("bchext", Env, AmlClassKind.Line, "Beach exit")
    {
        Glyph = BeachExitGlyph,
        Build = (feature, context) => feature.GeometryType == S100GeometryType.Curve
            ? [context.At(AmlDrawingContext.LinePriority).ComplexLine(feature.Id, ExitStyle)]
            : AmlPortrayal.Build(feature, new AmlClassStyle("bchext", Env, AmlClassKind.Point, "Beach exit") { Glyph = BeachExitGlyph }),
        Legend = (catalog, palette) => [new LineLegendEntry(ExitSample(catalog, palette), "Beach exit")],
    };

    /// <summary>Diving locations.</summary>
    public static AmlClassStyle DivingLocation { get; } = Area("divloc", "Diving location", DivingGlyph);

    /// <summary>Viewpoints.</summary>
    public static AmlClassStyle Viewpoint { get; } = Point("viewpt", "Viewpoint", ViewpointGlyph);

    /// <summary>Resource locations.</summary>
    public static AmlClassStyle ResourceLocation { get; } = Area("resloc", "Resource location", ResourceGlyph);

    /// <summary>Shelter locations.</summary>
    public static AmlClassStyle ShelterLocation { get; } = Point("shlloc", "Shelter location", ShelterGlyph);

    /// <summary>Icebergs.</summary>
    public static AmlClassStyle Iceberg { get; } = Area("icebrg", "Iceberg", IcebergGlyph);

    /// <summary>Ice movement: an arrow in the drift direction.</summary>
    public static AmlClassStyle IceMovement { get; } = Area("icemov", "Ice movement", IceGlyph) with
    {
        Label = IceMovementLabel,
        Build = BuildIceMovement,
    };

    /// <summary>The module of the environmental classes, in legend order.</summary>
    public static IAmlStyleModule Module { get; } = new AmlStyleModule(
        "ESB environmental",
        [
            GeologicalLayer,
            BottomFeature,
            TrawlScours,
            LandingZone,
            LandingArea,
            LandingSite,
            LandingStrip,
            LandingPoint,
            LandingPlace,
            DropZone,
            BeachExit,
            DivingLocation,
            Viewpoint,
            ResourceLocation,
            ShelterLocation,
            Iceberg,
            IceMovement,
            new("icelin", Env, AmlClassKind.Line, "Ice line"),
            Area("icelea", "Ice lead"),
            Area("icepol", "Ice polynya"),
            Area("seaice", "Sea ice"),
            Area("lndice", "Land ice"),
            Area("brgare", "Iceberg area"),
            new("bchprf", Env, AmlClassKind.Line, "Beach profile"),
            Area("bchare", "Beach survey"),
            Area("trfare", "Trafficability area"),
            Area("envare", "Environmentally sensitive area"),
            Area("fshare", "Fishing activity area"),
            Area("lsrare", "Leisure activity area"),
            Area("imgare", "Imagery coverage"),
            Area("bprare", "Burial probability area"),
            Area("btdare", "Bottom tactical data area"),
            Area("mcmare", "MCM area"),
            Area("pfdare", "Performance data area"),
            Area("rkdare", "Risk data area"),
        ])
    {
        Resources = BuildResources(),
    };

    /// <summary>
    /// The compass direction ice drifts to, in degrees clockwise from north,
    /// from <c>icebdr</c>; <see langword="null"/> for no motion, variable or
    /// unknown.
    /// </summary>
    /// <param name="feature">The feature.</param>
    public static double? DriftDirection(AmlFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return int.TryParse(feature.Attribute("icebdr"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code) && code is >= 502 and <= 509
            ? (code - 502 + 1) % 8 * 45.0
            : null;
    }

    private static AmlClassStyle Area(string acronym, string label, AmlGlyph? glyph = null) =>
        new(acronym, Env, AmlClassKind.Area, label) { Glyph = glyph };

    private static AmlClassStyle Point(string acronym, string label, AmlGlyph glyph) =>
        new(acronym, Env, AmlClassKind.Point, label) { Glyph = glyph };

    private static string? IceMovementLabel(AmlFeature feature)
    {
        var name = AmlLabels.Name(feature) is { Length: > 0 } n ? n : null;
        string? drift = DriftDirection(feature) is { } degrees ? "drift " + Compass(degrees) : null;
        return (name, drift) switch
        {
            (null, null) => null,
            (null, _) => drift,
            (_, null) => name,
            _ => $"{name} · {drift}",
        };
    }

    private static string Compass(double degrees) => ((int)Math.Round(degrees / 45) % 8) switch
    {
        0 => "N",
        1 => "NE",
        2 => "E",
        3 => "SE",
        4 => "S",
        5 => "SW",
        6 => "W",
        _ => "NW",
    };

    // A point with a drift direction is the arrow, turned to it on the chart,
    // and its label; anything else is drawn as the class's area and framed
    // crystal.
    private static IEnumerable<DrawingInstruction> BuildIceMovement(AmlFeature feature, AmlDrawingContext context)
    {
        if (feature.GeometryType == S100GeometryType.Point && DriftDirection(feature) is { } degrees)
        {
            yield return context.At(AmlDrawingContext.PointPriority).Point(feature.Id, DriftSymbol, degrees, SymbolRotationCrs.Geographic);
            if (IceMovementLabel(feature) is { } label)
                yield return context.PointLabel(label);
            yield break;
        }

        foreach (var instruction in AmlPortrayal.Build(feature, Area("icemov", "Ice movement", IceGlyph) with { Label = IceMovementLabel }))
            yield return instruction;
    }

    private static AmlStyleResources BuildResources()
    {
        var ink = Env.ColorToken;
        var halo = AmlPalette.HaloToken;
        var scour = Svg(
            ScourSymbol,
            "A trawl scour stroke, environmental family: across the line at 60°.",
            1.2,
            $"""<path class="s{halo} f0" stroke-width="1.0" stroke-linecap="round" d="M -0.45 0.8 L 0.45 -0.8"/><path class="s{ink} f0" stroke-width="0.45" stroke-linecap="round" d="M -0.45 0.8 L 0.45 -0.8"/>""");
        var drift = Svg(
            DriftSymbol,
            "An ice drift arrow, environmental family: pointing where the ice drifts.",
            3.0,
            $"""<path class="s{halo} f0" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round" d="M 0 2.4 L 0 -2.2 M -1.0 -1.2 L 0 -2.2 L 1.0 -1.2"/><path class="s{ink} f0" stroke-width="0.58" stroke-linecap="round" stroke-linejoin="round" d="M 0 2.4 L 0 -2.2 M -1.0 -1.2 L 0 -2.2 L 1.0 -1.2"/><circle class="f{ink}" cx="0" cy="2.4" r="0.45"/>""");

        return EsbSediment.Resources with
        {
            Symbols = new Dictionary<string, string>(StringComparer.Ordinal) { [ScourSymbol] = scour, [DriftSymbol] = drift },
            LineStyles =
            [
                new LineStyle
                {
                    Name = ScoursStyle,
                    Width = 0,
                    Color = ink,
                    HasPen = false,
                    IntervalLength = (float)ScourSpacing,
                    Symbols = [new LineStyleSymbol(ScourSymbol, (float)(ScourSpacing / 2))],
                },
                new LineStyle
                {
                    Name = ExitStyle,
                    Width = (float)ExitInkWidth,
                    Color = ink,
                    HasPen = false,
                    Components =
                    [
                        new LineStyle { Name = ExitStyle + "_HALO", Width = (float)AmlSignature.HaloWidth, Color = halo },
                        new LineStyle { Name = ExitStyle + "_INK", Width = (float)ExitInkWidth, Color = ink },
                        new LineStyle
                        {
                            Name = ExitStyle + "_CHEVRONS",
                            Width = 0,
                            Color = ink,
                            HasPen = false,
                            IntervalLength = (float)ExitChevronSpacing,
                            Symbols = [new LineStyleSymbol(AmlSignature.ChevronSymbol(Env), (float)(ExitChevronSpacing / 2))],
                        },
                    ],
                },
            ],
        };
    }

    private static LegendLineSample ScoursSample(AmlStyleCatalog catalog, PaletteType palette) => new()
    {
        Color = catalog.Palette.Resolve(Env.ColorToken, palette).ToHex(),
        Width = 0,
        IntervalLength = ScourSpacing,
        Symbols = [LegendSymbol(catalog, palette, ScourSymbol, ScourSpacing / 2)],
    };

    private static LegendLineSample ExitSample(AmlStyleCatalog catalog, PaletteType palette) => new()
    {
        Color = catalog.Palette.Resolve(Env.ColorToken, palette).ToHex(),
        Width = ExitInkWidth,
        IntervalLength = ExitChevronSpacing,
        Symbols = [LegendSymbol(catalog, palette, AmlSignature.ChevronSymbol(Env), ExitChevronSpacing / 2)],
    };

    private static LegendLineSymbol LegendSymbol(AmlStyleCatalog catalog, PaletteType palette, string name, double position)
    {
        var raw = catalog.Symbols.GetSymbol(name)!;
        var pivot = SvgPivotMetrics.TryParse(raw)?.RelativeOffset ?? (0.0, 0.0);
        return new LegendLineSymbol(SvgProcessor.Process(raw, catalog.Palette.GetColorPalette(palette)), position, 0, pivot.X, pivot.Y);
    }

    private static string Svg(string name, string description, double half, string content)
    {
        var size = (2 * half).ToString("0.###", CultureInfo.InvariantCulture);
        var min = (-half).ToString("0.###", CultureInfo.InvariantCulture);
        return $"""<svg xmlns="http://www.w3.org/2000/svg" version="1.2" width="{size}mm" height="{size}mm" viewBox="{min} {min} {size} {size}"><title>{name}</title><desc>{description}</desc><rect class="symbolBox layout" fill="none" x="{min}" y="{min}" width="{size}" height="{size}"/>{content}<circle class="pivotPoint layout" fill="none" cx="0" cy="0" r="0.5"/></svg>""";
    }
}
