namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// The classes of the Maritime Foundation &amp; Facilities (MFF) product that
/// matter to military planning in their own right: seabed infrastructure,
/// offshore installations, traffic and use areas, tides and magnetics. Drawn
/// in the AML style of the family each belongs to. MFF's chart background
/// (coast, land, lights, buoys, beacons) is in
/// <see cref="ChartBackgroundStyles"/>, its obstructions in the LBO styles.
/// </summary>
/// <remarks>
/// <para>
/// <b>Decision 19.</b> These are mostly IHO S-57 chart classes. The ENC
/// under the AML draws its own symbols for them; AML adds the dash-dot or
/// the ticked boundary and, at a point, a chart-like glyph in the AML frame
/// (<see cref="ChartLikeGlyphs"/>). The few AML-only classes (<c>msiare</c>,
/// <c>rdoare</c>, <c>seiare</c>, <c>tfcrte</c>) draw the same way.
/// </para>
/// <list type="table">
/// <listheader><term>Family</term><description>Classes</description></listheader>
/// <item><term>Seabed</term><description>Cables and pipelines, their areas.</description></item>
/// <item><term>Operational</term><description>Platforms, production and fishing areas, traffic separation, deep-water and ferry routes, anchorages, dumping grounds, MSI and radio areas.</description></item>
/// <item><term>Environmental</term><description>Tide and tidal-stream stations, magnetic anomalies and variation, weed, ice, currents, seismic areas.</description></item>
/// </list>
/// </remarks>
public static class MffStyles
{
    /// <summary>Seabed infrastructure: cables and pipelines.</summary>
    public static IReadOnlyList<AmlClassStyle> Seabed { get; } =
    [
        Chart("CBLSUB", AmlFamily.Seabed, AmlClassKind.Line, "Cable, submarine"),
        Chart("PIPSOL", AmlFamily.Seabed, AmlClassKind.Line, "Pipeline, submarine", ChartLikeGlyphs.Pipe),
        Chart("CBLARE", AmlFamily.Seabed, AmlClassKind.Area, "Cable area"),
        Chart("PIPARE", AmlFamily.Seabed, AmlClassKind.Area, "Pipeline area", ChartLikeGlyphs.Pipe),
    ];

    /// <summary>Installations, traffic and sea use.</summary>
    public static IReadOnlyList<AmlClassStyle> Operational { get; } =
    [
        Chart("OFSPLF", AmlFamily.Operational, AmlClassKind.Area, "Offshore platform", ChartLikeGlyphs.Platform),
        Chart("OSPARE", AmlFamily.Operational, AmlClassKind.Area, "Offshore production area"),
        Chart("PRDARE", AmlFamily.Operational, AmlClassKind.Area, "Production or storage area", ChartLikeGlyphs.Platform),
        Chart("MARCUL", AmlFamily.Operational, AmlClassKind.Area, "Marine farm", ChartLikeGlyphs.Fish),
        Chart("FSHFAC", AmlFamily.Operational, AmlClassKind.Area, "Fishing facility", ChartLikeGlyphs.Fish),
        Chart("FSHGRD", AmlFamily.Operational, AmlClassKind.Area, "Fishing ground"),
        Chart("TSSLPT", AmlFamily.Operational, AmlClassKind.Area, "Traffic separation lane"),
        Chart("TSEZNE", AmlFamily.Operational, AmlClassKind.Area, "Traffic separation zone"),
        Chart("TSSCRS", AmlFamily.Operational, AmlClassKind.Area, "Traffic separation crossing"),
        Chart("TSSRON", AmlFamily.Operational, AmlClassKind.Area, "Traffic separation roundabout"),
        Chart("TSSBND", AmlFamily.Operational, AmlClassKind.Line, "Traffic separation boundary"),
        Chart("TSELNE", AmlFamily.Operational, AmlClassKind.Line, "Traffic separation line"),
        Chart("ISTZNE", AmlFamily.Operational, AmlClassKind.Area, "Inshore traffic zone"),
        Chart("DWRTPT", AmlFamily.Operational, AmlClassKind.Area, "Deep-water route part"),
        Chart("DWRTCL", AmlFamily.Operational, AmlClassKind.Line, "Deep-water route centreline"),
        Chart("FERYRT", AmlFamily.Operational, AmlClassKind.Line, "Ferry route"),
        Chart("ACHARE", AmlFamily.Operational, AmlClassKind.Area, "Anchorage area"),
        Chart("DMPGRD", AmlFamily.Operational, AmlClassKind.Area, "Dumping ground"),
        Own("tfcrte", AmlFamily.Operational, AmlClassKind.Line, "Traffic route"),
        Own("msiare", AmlFamily.Operational, AmlClassKind.Area, "Maritime safety information area"),
        Own("rdoare", AmlFamily.Operational, AmlClassKind.Area, "Radio broadcast area"),
    ];

    /// <summary>Tides, magnetics and the physical environment.</summary>
    public static IReadOnlyList<AmlClassStyle> Environmental { get; } =
    [
        Chart("TS_FEB", AmlFamily.Environmental, AmlClassKind.Area, "Tidal stream, flood/ebb", ChartLikeGlyphs.Tide),
        Chart("TS_PAD", AmlFamily.Environmental, AmlClassKind.Area, "Tidal stream panel", ChartLikeGlyphs.Tide),
        Chart("TS_PNH", AmlFamily.Environmental, AmlClassKind.Area, "Tidal stream, non-harmonic", ChartLikeGlyphs.Tide),
        Chart("TS_PRH", AmlFamily.Environmental, AmlClassKind.Area, "Tidal stream, harmonic", ChartLikeGlyphs.Tide),
        Chart("TS_TIS", AmlFamily.Environmental, AmlClassKind.Area, "Tidal stream, time series", ChartLikeGlyphs.Tide),
        Chart("T_HMON", AmlFamily.Environmental, AmlClassKind.Area, "Tide, harmonic", ChartLikeGlyphs.Tide),
        Chart("T_NHMN", AmlFamily.Environmental, AmlClassKind.Area, "Tide, non-harmonic", ChartLikeGlyphs.Tide),
        Chart("T_TIMS", AmlFamily.Environmental, AmlClassKind.Area, "Tide, time series", ChartLikeGlyphs.Tide),
        Chart("LOCMAG", AmlFamily.Environmental, AmlClassKind.Area, "Local magnetic anomaly", ChartLikeGlyphs.Magnetic),
        Chart("MAGVAR", AmlFamily.Environmental, AmlClassKind.Area, "Magnetic variation", ChartLikeGlyphs.Magnetic),
        Chart("WEDKLP", AmlFamily.Environmental, AmlClassKind.Area, "Weed or kelp", ChartLikeGlyphs.Weed),
        Chart("ICEARE", AmlFamily.Environmental, AmlClassKind.Area, "Ice area"),
        Chart("CURENT", AmlFamily.Environmental, AmlClassKind.Line, "Current"),
        Own("seiare", AmlFamily.Environmental, AmlClassKind.Area, "Seismic activity area"),
    ];

    /// <summary>The MFF module, in legend order.</summary>
    public static IAmlStyleModule Module { get; } = new AmlStyleModule("MFF", [.. Seabed, .. Operational, .. Environmental]);

    private static AmlClassStyle Chart(string acronym, AmlFamily family, AmlClassKind kind, string legend, AmlGlyph? glyph = null) =>
        new(acronym, family, kind, legend) { Glyph = glyph, IsChartClass = true };

    private static AmlClassStyle Own(string acronym, AmlFamily family, AmlClassKind kind, string legend) =>
        new(acronym, family, kind, legend);
}
