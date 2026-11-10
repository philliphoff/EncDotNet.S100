using System.Globalization;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.Aml.Families;

/// <summary>
/// The seabed classes of the AML Small Bottom Objects (SBO) and Large Bottom
/// Objects (LBO) products, in the <see cref="AmlFamily.Seabed"/> family (AML
/// design handoff frame B2c; decisions 17, 19 and 20). The AML 3.0 trials'
/// small bottom objects draw as SBO contacts.
/// </summary>
/// <remarks>
/// <list type="table">
/// <listheader><term>Class</term><description>Drawn as</description></listheader>
/// <item><term><c>contct</c> contact (SBO)</term><description>Framed open circle; framed mine when classified mine-like.</description></item>
/// <item><term><c>mindev</c> mine (SBO)</term><description>Framed filled circle with four spikes (MIL-STD-2525C sea mine).</description></item>
/// <item><term><c>smalbo</c> small bottom object (3.0)</term><description>As a contact, from its status.</description></item>
/// <item><term><c>senanm</c> sensor anomaly (LBO)</term><description>Framed dashed diamond.</description></item>
/// <item><term><c>iscour</c> impact scour (LBO)</term><description>Short parallel strokes along its cardinal orientation.</description></item>
/// <item><term><c>WRECKS</c>, <c>OBSTRN</c>, <c>UWTROC</c> (LBO)</term><description>Chart classes (decision 19): the ENC keeps the chart symbol; AML adds a dash-dot ring meaning "AML details on pick".</description></item>
/// </list>
/// <para>
/// SBO points cluster within 24 px into a counted bubble labelled such as
/// "38 contacts · 2 mine-like" (<see cref="SboClustering"/>); LBO points
/// cluster the same way into "bottom objects" (<see cref="LboClustering"/>),
/// since the LBO samples hold thousands of wrecks and obstructions.
/// Seabed points carry no labels: their details are on pick (decision 13).
/// </para>
/// </remarks>
public static class SeabedStyles
{
    // The status codes (stacon, staobj) of a mine-like contact: Classified (MILCO) and Identified (Mine).
    private static readonly string[] MineLikeStatus = ["502", "505"];

    /// <summary>SBO contacts, mines and small bottom objects: "38 contacts · 2 mine-like".</summary>
    public static AmlClustering SboClustering { get; } = new(
        "SBO contacts",
        AmlFamily.Seabed,
        SeabedGlyphs.Cluster,
        members => CountLabel(members.Count, "contact", "contacts") + (members.Count(IsMineLike) is var mines and > 0
            ? string.Create(CultureInfo.InvariantCulture, $" · {mines} mine-like")
            : string.Empty));

    /// <summary>LBO wrecks, obstructions, rocks, anomalies and scours: "40 bottom objects".</summary>
    public static AmlClustering LboClustering { get; } = new(
        "LBO bottom objects",
        AmlFamily.Seabed,
        SeabedGlyphs.Cluster,
        members => CountLabel(members.Count, "bottom object", "bottom objects"));

    /// <summary>Contacts (SBO): a mine glyph when classified mine-like.</summary>
    public static AmlClassStyle Contact { get; } = ContactLike("contct", "Contact");

    /// <summary>Mines (SBO).</summary>
    public static AmlClassStyle Mine { get; } = new("mindev", AmlFamily.Seabed, AmlClassKind.Point, "Mine or mine-like contact")
    {
        Glyph = SeabedGlyphs.Mine,
        Label = NoLabel,
        Clustering = SboClustering,
    };

    /// <summary>Small bottom objects (AML 3.0 SBO): drawn as contacts.</summary>
    public static AmlClassStyle SmallBottomObject { get; } = ContactLike("smalbo", "Small bottom object");

    /// <summary>Sensor anomalies (LBO).</summary>
    public static AmlClassStyle SensorAnomaly { get; } = new("senanm", AmlFamily.Seabed, AmlClassKind.Point, "Sensor anomaly")
    {
        Glyph = SeabedGlyphs.Anomaly,
        Label = NoLabel,
        Clustering = LboClustering,
    };

    /// <summary>Impact scours (LBO): strokes turned to the scour's <c>orcard</c>.</summary>
    public static AmlClassStyle ImpactScour { get; } = new("iscour", AmlFamily.Seabed, AmlClassKind.Point, "Impact scour")
    {
        Glyph = SeabedGlyphs.Scour,
        Label = NoLabel,
        Clustering = LboClustering,
        Build = (feature, context) => feature.GeometryType == Features.S100GeometryType.Point
            ? [context.At(AmlDrawingContext.PointPriority).Point(feature.Id, AmlSignature.FramedSymbol(context.Family, SeabedGlyphs.Scour), ScourRotation(feature))]
            : [],
    };

    /// <summary>Wrecks (LBO): a chart class, ringed.</summary>
    public static AmlClassStyle Wreck { get; } = Ringed("WRECKS", "Wreck: AML details on pick");

    /// <summary>Obstructions (LBO): a chart class, ringed.</summary>
    public static AmlClassStyle Obstruction { get; } = Ringed("OBSTRN", "Obstruction: AML details on pick");

    /// <summary>Underwater and awash rocks (LBO): a chart class, ringed.</summary>
    public static AmlClassStyle Rock { get; } = Ringed("UWTROC", "Rock: AML details on pick");

    /// <summary>The module of the SBO classes, in legend order.</summary>
    public static IAmlStyleModule SboModule { get; } = new AmlStyleModule("SBO seabed", [Contact, Mine, SmallBottomObject]);

    /// <summary>The module of the LBO classes, in legend order.</summary>
    public static IAmlStyleModule LboModule { get; } = new AmlStyleModule("LBO seabed", [SensorAnomaly, ImpactScour, Wreck, Obstruction, Rock]);

    /// <summary>
    /// Whether <paramref name="feature"/> is a mine or mine-like: a mine
    /// (<c>mindev</c>), or a contact or small bottom object whose status
    /// (<c>stacon</c>, <c>staobj</c>) includes Classified (MILCO) or Identified (Mine).
    /// </summary>
    /// <param name="feature">The feature.</param>
    public static bool IsMineLike(AmlFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        if (feature.FeatureType == "mindev")
            return true;
        var status = feature.Attribute("stacon") ?? feature.Attribute("staobj");
        return status is not null && status.Split(',', StringSplitOptions.TrimEntries).Any(MineLikeStatus.Contains);
    }

    /// <summary>
    /// The rotation of a scour's strokes from its <c>orcard</c>: 0° north–south,
    /// 90° east–west, 45° northeast–southwest, 135° northwest–southeast; 0° when unknown.
    /// </summary>
    /// <param name="feature">The scour.</param>
    public static double ScourRotation(AmlFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        return feature.Attribute("orcard") switch
        {
            "502" => 90,
            "503" => 45,
            "504" => 135,
            _ => 0,
        };
    }

    private static string? NoLabel(AmlFeature feature) => null;

    private static string CountLabel(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");

    private static AmlClassStyle ContactLike(string acronym, string legendLabel) => new(acronym, AmlFamily.Seabed, AmlClassKind.Point, legendLabel)
    {
        Glyph = SeabedGlyphs.Contact,
        OtherGlyphs = [SeabedGlyphs.Mine],
        Label = NoLabel,
        Clustering = SboClustering,
        Build = (feature, context) => feature.GeometryType == Features.S100GeometryType.Point
            ? [context.Symbol(IsMineLike(feature) ? SeabedGlyphs.Mine : SeabedGlyphs.Contact)]
            : [],
    };

    private static AmlClassStyle Ringed(string acronym, string legendLabel) => new(acronym, AmlFamily.Seabed, AmlClassKind.Point, legendLabel)
    {
        Glyph = SeabedGlyphs.DetailsRing,
        IsChartClass = true,
        Label = NoLabel,
        Clustering = LboClustering,
    };
}
