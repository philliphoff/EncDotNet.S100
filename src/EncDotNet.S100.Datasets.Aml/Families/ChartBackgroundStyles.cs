using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals.Application;

namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// The chart and land background that AML carries: the AML 3.0 Nautical
/// Chart Background (NCD) and Land Background (LND) sub-layers, and the
/// classes of the Maritime Foundation &amp; Facilities (MFF) product and other
/// products that repeat the chart: coastline, land, lights, buoys, beacons,
/// landmarks and stations. Drawn in the <see cref="Background"/> family.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decision 19.</b> Nearly all of these are IHO S-57 chart classes, so
/// the ENC under the AML draws the chart symbol. AML adds its own signature
/// in a quiet neutral ink: navigation aids as a chart-like glyph in the AML
/// frame (a flare, a buoy, a beacon), without a name, since the chart names
/// them; lines as the dash-dot; areas as the ticked boundary and name. Land
/// areas have no boundary (the coastline is their edge) and sea areas only
/// their name, as on a chart.
/// </para>
/// <para>
/// The AML 3.0 LND classes (<c>aprhst</c>, <c>shedht</c>, <c>trpfac</c> and
/// so on) are AML's own; their points share the site glyph and their name.
/// </para>
/// </remarks>
public static class ChartBackgroundStyles
{
    /// <summary>
    /// The chart background family: a neutral slate ink, apart from the four
    /// coloured families of decision 16, for AML data that repeats the chart
    /// or the land. On its own sub-layer <c>aml.background</c>, above
    /// bathymetry and below the seabed family.
    /// </summary>
    public static AmlFamily Background { get; } = new(
        "background",
        "Chart background",
        "AMLBKG",
        PaletteColors.FromHex("#4F5B66", "#66717C", "#48525C"),
        order: 8,
        viewingGroup: 45008);

    /// <summary>The navigation aids, drawn as a chart-like glyph without a name.</summary>
    public static IReadOnlyList<AmlClassStyle> NavigationAids { get; } =
    [
        Aid("LIGHTS", "Light", ChartLikeGlyphs.Light),
        Aid("LITFLT", "Light float", ChartLikeGlyphs.LightVessel),
        Aid("LITVES", "Light vessel", ChartLikeGlyphs.LightVessel),
        Aid("BOYCAR", "Buoy, cardinal", ChartLikeGlyphs.Buoy),
        Aid("BOYINB", "Buoy, installation", ChartLikeGlyphs.Buoy),
        Aid("BOYISD", "Buoy, isolated danger", ChartLikeGlyphs.Buoy),
        Aid("BOYLAT", "Buoy, lateral", ChartLikeGlyphs.Buoy),
        Aid("BOYSAW", "Buoy, safe water", ChartLikeGlyphs.Buoy),
        Aid("BOYSPP", "Buoy, special purpose", ChartLikeGlyphs.Buoy),
        Aid("BCNCAR", "Beacon, cardinal", ChartLikeGlyphs.Beacon),
        Aid("BCNISD", "Beacon, isolated danger", ChartLikeGlyphs.Beacon),
        Aid("BCNLAT", "Beacon, lateral", ChartLikeGlyphs.Beacon),
        Aid("BCNSAW", "Beacon, safe water", ChartLikeGlyphs.Beacon),
        Aid("BCNSPP", "Beacon, special purpose", ChartLikeGlyphs.Beacon),
    ];

    /// <summary>Landmarks, stations and harbour facilities: a chart-like glyph with the name.</summary>
    public static IReadOnlyList<AmlClassStyle> Facilities { get; } =
    [
        Point("LNDMRK", "Landmark", ChartLikeGlyphs.Landmark),
        Point("RDOSTA", "Radio station", ChartLikeGlyphs.Radio),
        Point("CGUSTA", "Coastguard station", ChartLikeGlyphs.Flag),
        Point("RSCSTA", "Rescue station", ChartLikeGlyphs.Flag),
        Point("SISTAW", "Signal station, warning", ChartLikeGlyphs.Flag),
        Point("CTRPNT", "Control point", ChartLikeGlyphs.Site),
        Point("LNDELV", "Land elevation", ChartLikeGlyphs.Site),
        Point("MORFAC", "Mooring facility", ChartLikeGlyphs.Site),
        Area("HRBFAC", "Harbour facility", ChartLikeGlyphs.Harbour),
        Area("GATCON", "Gate", ChartLikeGlyphs.Site),
        Area("BUISGL", "Building", ChartLikeGlyphs.Site),
        Area("FORSTC", "Fortified structure", ChartLikeGlyphs.Site),
    ];

    /// <summary>Coastline, land and the lines and areas on it.</summary>
    public static IReadOnlyList<AmlClassStyle> Land { get; } =
    [
        Line("COALNE", "Coastline"),
        Line("SLCONS", "Shoreline construction"),
        new("LNDARE", Background, AmlClassKind.Area, "Land area (name only)") { IsChartClass = true, Glyph = ChartLikeGlyphs.Islet, Build = BuildLand },
        new("SEAARE", Background, AmlClassKind.Area, "Sea area (name only)") { IsChartClass = true, Build = BuildNameOnly },
        Area("BUAARE", "Built-up area", ChartLikeGlyphs.Site),
        Area("ADMARE", "Administration area", ChartLikeGlyphs.Site),
        Area("HRBARE", "Harbour area", glyph: null),
        Area("LNDRGN", "Land region", glyph: null),
        Line("RIVERS", "River"),
        Line("ROADWY", "Road"),
        Line("BRIDGE", "Bridge"),
        Line("CBLOHD", "Cable, overhead"),
        Line("PIPOHD", "Pipeline, overhead"),
        Line("CONVYR", "Conveyor"),
    ];

    /// <summary>The AML 3.0 Land Background (LND) classes.</summary>
    public static IReadOnlyList<AmlClassStyle> LandBackground { get; } =
    [
        Own("aprhst", AmlClassKind.Point, "Apron or hardstand", ChartLikeGlyphs.Site),
        Own("shedht", AmlClassKind.Point, "Shed or hut", ChartLikeGlyphs.Site),
        Own("trpfac", AmlClassKind.Point, "Transport facility", ChartLikeGlyphs.Site),
        Own("watero", AmlClassKind.Point, "Water object", ChartLikeGlyphs.Site),
        Own("resfac", AmlClassKind.Point, "Research facility", ChartLikeGlyphs.Site),
        Own("treata", AmlClassKind.Area, "Treatment area", ChartLikeGlyphs.Site),
        Own("watste", AmlClassKind.Area, "Water treatment site", ChartLikeGlyphs.Site),
        Own("wstdsp", AmlClassKind.Area, "Waste disposal site", ChartLikeGlyphs.Site),
        Own("inlsho", AmlClassKind.Line, "Inland shoreline", glyph: null),
        Own("boundg", AmlClassKind.Line, "General boundary", glyph: null),
        Own("ferrys", AmlClassKind.Line, "Ferry site", glyph: null),
        Own("geophg", AmlClassKind.Line, "Geophysical prospection grid", glyph: null),
        new("txtbua", Background, AmlClassKind.Point, "Built-up area name") { Build = BuildNameOnly },
    ];

    /// <summary>The chart background module, in legend order.</summary>
    public static IAmlStyleModule Module { get; } = new AmlStyleModule(
        "Chart background (NCD, LND, MFF)",
        [.. NavigationAids, .. Facilities, .. Land, .. LandBackground]);

    private static AmlClassStyle Aid(string acronym, string legend, AmlGlyph glyph) =>
        new(acronym, Background, AmlClassKind.Point, legend) { Glyph = glyph, IsChartClass = true, Label = static _ => null };

    private static AmlClassStyle Point(string acronym, string legend, AmlGlyph glyph) =>
        new(acronym, Background, AmlClassKind.Point, legend) { Glyph = glyph, IsChartClass = true };

    private static AmlClassStyle Area(string acronym, string legend, AmlGlyph? glyph) =>
        new(acronym, Background, AmlClassKind.Area, legend) { Glyph = glyph, IsChartClass = true };

    private static AmlClassStyle Line(string acronym, string legend) =>
        new(acronym, Background, AmlClassKind.Line, legend) { IsChartClass = true };

    private static AmlClassStyle Own(string acronym, AmlClassKind kind, string legend, AmlGlyph? glyph) =>
        new(acronym, Background, kind, legend) { Glyph = glyph };

    // Land: the coastline is its edge, so an area draws only its name; a
    // line feature (a narrow spit) draws as a line, a point (an islet) as a dot.
    private static IEnumerable<DrawingInstruction> BuildLand(AmlFeature feature, AmlDrawingContext context)
    {
        var name = AmlLabels.Name(feature);
        switch (feature.GeometryType)
        {
            case Features.S100GeometryType.Curve:
                yield return context.Line();
                break;
            case Features.S100GeometryType.Point:
                yield return context.Symbol(ChartLikeGlyphs.Islet);
                if (name is not null)
                    yield return context.PointLabel(name);
                yield break;
        }

        if (name is not null)
            yield return context.At(AmlDrawingContext.LabelPriority).Text(feature.Id, name, context.Label);
    }

    // A sea area or a built-up area's name: the name alone, centred.
    private static IEnumerable<DrawingInstruction> BuildNameOnly(AmlFeature feature, AmlDrawingContext context)
    {
        if (feature.GeometryType != Features.S100GeometryType.None && AmlLabels.Name(feature) is { } name)
            yield return context.At(AmlDrawingContext.LabelPriority).Text(feature.Id, name, context.Label);
    }
}
