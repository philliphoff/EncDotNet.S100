using System.Globalization;
using System.Xml;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>What a buoy marks, in the IALA A system.</summary>
public enum BuoyMark
{
    /// <summary>Port-hand lateral mark: red can.</summary>
    PortHand,

    /// <summary>Starboard-hand lateral mark: green cone.</summary>
    StarboardHand,

    /// <summary>Safe water mark: red and white vertical stripes, spherical.</summary>
    SafeWater,

    /// <summary>North cardinal mark: black over yellow pillar.</summary>
    NorthCardinal,
}

/// <summary>A light on a buoy (<c>LightAllAround</c>).</summary>
/// <param name="Colour">The light's colour code (1 white, 3 red, 4 green).</param>
/// <param name="Character">The <c>lightCharacteristic</c> code (2 flashing, 3 long-flashing, 5 very quick-flashing).</param>
/// <param name="SignalGroup">The signal group, such as <c>(1)</c>, or <c>null</c>.</param>
/// <param name="PeriodSeconds">The signal period in seconds, or <c>null</c> for a continuous character.</param>
/// <param name="RangeNauticalMiles">The nominal range in nautical miles.</param>
public sealed record BuoyLight(int Colour, int Character, string? SignalGroup, double? PeriodSeconds, double RangeNauticalMiles);

/// <summary>A buoy, with an optional light.</summary>
/// <param name="Number">The aid's number in the list of lights and buoys (<c>atoNNumber</c>).</param>
/// <param name="Name">The buoy's name.</param>
/// <param name="Mark">What it marks.</param>
/// <param name="X">Metres east.</param>
/// <param name="Y">Metres north.</param>
/// <param name="Light">Its light, or <c>null</c> for an unlit buoy.</param>
public sealed record BuoySpec(string Number, string Name, BuoyMark Mark, double X, double Y, BuoyLight? Light = null);

/// <summary>An S-125 dataset of buoys.</summary>
/// <param name="DatasetName">The file name without extension.</param>
/// <param name="Title">The dataset's title.</param>
/// <param name="Buoys">The buoys.</param>
public sealed record S125AidsSpec(string DatasetName, string Title, IReadOnlyList<BuoySpec> Buoys)
{
    /// <summary>The file name, with the <c>.gml</c> extension.</summary>
    public string FileName => DatasetName + ".gml";

    /// <summary>The dataset's <c>gml:id</c>, also the prefix of its members' ids.</summary>
    public string Id => "ZZ00.S125." + DatasetName;

    /// <summary>A buoy's <c>gml:id</c>.</summary>
    /// <param name="index">The buoy's index in <see cref="Buoys"/>.</param>
    /// <returns>The id.</returns>
    public string BuoyId(int index) => string.Create(CultureInfo.InvariantCulture, $"{Id}.{index + 1}");

    /// <summary>The <c>gml:id</c> of a buoy's light.</summary>
    /// <param name="index">The buoy's index in <see cref="Buoys"/>.</param>
    /// <returns>The id.</returns>
    public string LightId(int index) => BuoyId(index) + ".L";
}

/// <summary>
/// Writes S-125 Edition 1.0.0 aids to navigation as S-100 Part 10b GML
/// (S-100 GML 5.0 geometry), with the bindings and attribute order of the
/// bundled S-125 1.0.0 feature catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Each buoy is a <c>LateralBuoy</c>, <c>SafeWaterBuoy</c> or
/// <c>CardinalBuoy</c> with the mandatory <c>atoNNumber</c>,
/// <c>buoyShape</c>, <c>colour</c> and mark category, IALA A
/// (<c>marksNavigationalSystemOf</c> 1), and a <c>featureName</c>. A lit
/// buoy carries a <c>LightAllAround</c> at the same position, bound by the
/// <c>StructureEquipment</c> association: <c>child</c> on the buoy,
/// <c>parent</c> (1..1 on equipment) on the light. The light has its
/// colour, nominal range and <c>rhythmOfLight</c> (character, group,
/// period).
/// </para>
/// <para>
/// Features sit in one <c>member</c> each, enumerations are numeric codes,
/// and the namespace is <c>http://www.iho.int/S125/1.0</c>, as the S-125
/// reader and the samples in this repository have them.
/// </para>
/// </remarks>
public static class S125AidsWriter
{
    /// <summary>The S-125 Edition 1.0.0 namespace.</summary>
    public const string Namespace = "http://www.iho.int/S125/1.0";

    /// <summary>A buoy's position, rounded to 10⁻⁷ degrees.</summary>
    /// <param name="world">The world.</param>
    /// <param name="buoy">The buoy.</param>
    /// <returns>The position.</returns>
    public static GeoPosition Position(SyntheticWorld world, BuoySpec buoy)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(buoy);
        return GmlWriting.Round(world.Frame.ToGeo(buoy.X, buoy.Y));
    }

    /// <summary>The S-125 feature type of a mark.</summary>
    /// <param name="mark">The mark.</param>
    /// <returns>The feature type code.</returns>
    public static string FeatureType(BuoyMark mark) => mark switch
    {
        BuoyMark.PortHand or BuoyMark.StarboardHand => "LateralBuoy",
        BuoyMark.SafeWater => "SafeWaterBuoy",
        _ => "CardinalBuoy",
    };

    /// <summary>Writes a dataset.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The buoys.</param>
    /// <param name="issued">The dataset's reference date (UTC).</param>
    /// <returns>The UTF-8 encoded dataset.</returns>
    public static byte[] Build(SyntheticWorld world, S125AidsSpec spec, DateTime issued)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.Buoys.Count == 0)
        {
            throw new ArgumentException("An S-125 dataset needs at least one aid.", nameof(spec));
        }

        return GmlWriting.Document(xml => Write(xml, world, spec, issued));
    }

    private static void Write(XmlWriter xml, SyntheticWorld world, S125AidsSpec spec, DateTime issued)
    {
        const string ns = Namespace;
        var positions = spec.Buoys.Select(b => Position(world, b)).ToList();

        xml.WriteStartElement("S125", "Dataset", ns);
        xml.WriteAttributeString("xmlns", "S100", null, GmlWriting.S100Gml5);
        xml.WriteAttributeString("xmlns", "gml", null, GmlWriting.Gml);
        xml.WriteAttributeString("xmlns", "xlink", null, GmlWriting.Xlink);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, spec.Id);
        GmlWriting.BoundedBy(xml, positions);

        xml.WriteStartElement("S100", "DatasetIdentificationInformation", GmlWriting.S100Gml5);
        S100(xml, "encodingSpecification", "S-100 Part 10b");
        S100(xml, "encodingSpecificationEdition", "1.0");
        S100(xml, "productIdentifier", "S-125");
        S100(xml, "productEdition", "1.0.0");
        S100(xml, "applicationProfile", "1");
        S100(xml, "datasetFileIdentifier", spec.FileName);
        S100(xml, "datasetTitle", $"{spec.Title} (SYNTHETIC)");
        S100(xml, "datasetReferenceDate", issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        S100(xml, "datasetLanguage", "eng");
        S100(xml, "datasetAbstract", $"SYNTHETIC aids to navigation in {world.Name}, a fictitious area. Not for navigation.");
        S100(xml, "datasetTopicCategory", "transportation");
        S100(xml, "datasetPurpose", "base");
        S100(xml, "updateNumber", "0");
        xml.WriteEndElement();

        for (var i = 0; i < spec.Buoys.Count; i++)
        {
            WriteBuoy(xml, spec, i, positions[i]);
            if (spec.Buoys[i].Light is { } light)
            {
                WriteLight(xml, spec, i, light, positions[i]);
            }
        }

        xml.WriteEndElement();
    }

    // Attributes in feature catalogue order: AidsToNavigation (featureName),
    // StructureObject (atoNNumber), GenericBuoy (buoyShape, colour,
    // colourPattern, marksNavigationalSystemOf), then the mark category.
    private static void WriteBuoy(XmlWriter xml, S125AidsSpec spec, int index, GeoPosition position)
    {
        const string ns = Namespace;
        var buoy = spec.Buoys[index];
        var id = spec.BuoyId(index);
        var (shape, colours, pattern) = Appearance(buoy.Mark);

        xml.WriteStartElement("S125", "member", ns);
        xml.WriteStartElement("S125", FeatureType(buoy.Mark), ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id);
        xml.WriteStartElement("S125", "featureName", ns);
        xml.WriteElementString("S125", "language", ns, "eng");
        xml.WriteElementString("S125", "name", ns, buoy.Name);
        xml.WriteEndElement();
        xml.WriteElementString("S125", "atoNNumber", ns, buoy.Number);
        Element(xml, "buoyShape", shape);
        foreach (var colour in colours)
        {
            Element(xml, "colour", colour);
        }

        if (pattern is { } p)
        {
            Element(xml, "colourPattern", p);
        }

        Element(xml, "marksNavigationalSystemOf", 1);
        switch (buoy.Mark)
        {
            case BuoyMark.PortHand:
                Element(xml, "categoryOfLateralMark", 1);
                break;
            case BuoyMark.StarboardHand:
                Element(xml, "categoryOfLateralMark", 2);
                break;
            case BuoyMark.NorthCardinal:
                Element(xml, "categoryOfCardinalMark", 1);
                break;
        }

        if (buoy.Light is not null)
        {
            Reference(xml, "child", spec.LightId(index));
        }

        xml.WriteStartElement("S125", "geometry", ns);
        GmlWriting.PointProperty(xml, GmlWriting.S100Gml5, id + ".G", position);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    // Attributes in feature catalogue order: LightAllAround's colour,
    // marksNavigationalSystemOf, valueOfNominalRange and rhythmOfLight.
    private static void WriteLight(XmlWriter xml, S125AidsSpec spec, int index, BuoyLight light, GeoPosition position)
    {
        const string ns = Namespace;
        var id = spec.LightId(index);

        xml.WriteStartElement("S125", "member", ns);
        xml.WriteStartElement("S125", "LightAllAround", ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id);
        Element(xml, "colour", light.Colour);
        Element(xml, "marksNavigationalSystemOf", 1);
        xml.WriteElementString("S125", "valueOfNominalRange", ns, GmlWriting.Number(light.RangeNauticalMiles));
        xml.WriteStartElement("S125", "rhythmOfLight", ns);
        Element(xml, "lightCharacteristic", light.Character);
        if (light.SignalGroup is { } group)
        {
            xml.WriteElementString("S125", "signalGroup", ns, group);
        }

        if (light.PeriodSeconds is { } period)
        {
            xml.WriteElementString("S125", "signalPeriod", ns, GmlWriting.Number(period));
        }

        xml.WriteEndElement();
        Reference(xml, "parent", spec.BuoyId(index));
        xml.WriteStartElement("S125", "geometry", ns);
        GmlWriting.PointProperty(xml, GmlWriting.S100Gml5, id + ".G", position);
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    // buoyShape, colours (top to bottom) and colourPattern of an IALA A mark.
    private static (int Shape, int[] Colours, int? Pattern) Appearance(BuoyMark mark) => mark switch
    {
        BuoyMark.PortHand => (2, [3], null),
        BuoyMark.StarboardHand => (1, [4], null),
        BuoyMark.SafeWater => (3, [3, 1], 2),
        BuoyMark.NorthCardinal => (4, [2, 6], 1),
        _ => throw new ArgumentOutOfRangeException(nameof(mark), mark, null),
    };

    private static void Reference(XmlWriter xml, string role, string targetId)
    {
        xml.WriteStartElement("S125", role, Namespace);
        xml.WriteAttributeString("xlink", "href", GmlWriting.Xlink, "#" + targetId);
        xml.WriteEndElement();
    }

    private static void Element(XmlWriter xml, string name, int value) =>
        xml.WriteElementString("S125", name, Namespace, value.ToString(CultureInfo.InvariantCulture));

    private static void S100(XmlWriter xml, string name, string value) =>
        xml.WriteElementString("S100", name, GmlWriting.S100Gml5, value);
}
