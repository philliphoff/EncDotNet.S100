using System.Globalization;
using System.Xml;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Testing.Scenarios.Geometry;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>The kind of geometry a warning has.</summary>
public enum WarningGeometryKind
{
    /// <summary>A point, such as a buoy off station.</summary>
    Point,

    /// <summary>A line, such as a cable being laid.</summary>
    Line,

    /// <summary>An area, such as survey operations.</summary>
    Area,
}

/// <summary>A warning's geometry in local metres.</summary>
/// <param name="Kind">Point, line or area.</param>
/// <param name="Points">
/// One point, the line's points in order, or the area's corners clockwise
/// (not closed; the writer closes the ring).
/// </param>
public sealed record WarningShape(WarningGeometryKind Kind, IReadOnlyList<(double X, double Y)> Points)
{
    /// <summary>A point.</summary>
    /// <param name="x">Metres east.</param>
    /// <param name="y">Metres north.</param>
    /// <returns>The shape.</returns>
    public static WarningShape Point(double x, double y) => new(WarningGeometryKind.Point, [(x, y)]);

    /// <summary>A line.</summary>
    /// <param name="points">The points in order.</param>
    /// <returns>The shape.</returns>
    public static WarningShape Line(params (double X, double Y)[] points) => new(WarningGeometryKind.Line, points);

    /// <summary>A rectangular area, its corners clockwise from the south-west.</summary>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The shape.</returns>
    public static WarningShape Area(LocalRect rect) => new(
        WarningGeometryKind.Area,
        [(rect.West, rect.South), (rect.West, rect.North), (rect.East, rect.North), (rect.East, rect.South)]);
}

/// <summary>An S-124 navigational warning, one per dataset.</summary>
/// <param name="DatasetName">The file name without extension.</param>
/// <param name="WarningNumber">The warning number in the series (year from <paramref name="Published"/>).</param>
/// <param name="Title">The warning's title.</param>
/// <param name="Text">The warning text.</param>
/// <param name="TypeGeneral">The general warning type (<c>navwarnTypeGeneral</c>, e.g. 17 special operations).</param>
/// <param name="Shape">Where the hazard is.</param>
/// <param name="Published">When the warning was published (UTC).</param>
/// <param name="ValidFrom">When the hazard starts (UTC), or <c>null</c> when open.</param>
/// <param name="ValidTo">When the hazard ends (UTC), or <c>null</c> when open.</param>
public sealed record S124WarningSpec(
    string DatasetName,
    int WarningNumber,
    string Title,
    string Text,
    int TypeGeneral,
    WarningShape Shape,
    DateTime Published,
    DateTime? ValidFrom,
    DateTime? ValidTo)
{
    /// <summary>
    /// When the warning cancels itself (Edition 2.0.0 <c>cancellationDate</c>),
    /// or <c>null</c>.
    /// </summary>
    public DateTime? CancellationDate { get; init; }

    /// <summary>The numbers of earlier warnings (same series and year) this one cancels.</summary>
    public IReadOnlyList<int> Cancels { get; init; } = [];

    /// <summary>The file name, with the <c>.gml</c> extension.</summary>
    public string FileName => DatasetName + ".gml";

    /// <summary>The warning's year.</summary>
    public int Year => Published.Year;
}

/// <summary>
/// Writes S-124 navigational warnings as S-100 Part 10b GML, in Edition
/// 2.0.0 (the S-124 2.0.0 feature catalogue's bindings and order) or the
/// Edition 1.0.0 form the reader also accepts.
/// </summary>
/// <remarks>
/// <para>
/// Each dataset holds one warning: a <c>NavwarnPreamble</c> (message series
/// identifier, title, general area, publication time and, when set, its
/// cancellation date), a <c>NavwarnPart</c> with the geometry, validity
/// (<c>fixedDateRange</c>) and text, and, for a warning that cancels
/// others, a <c>References</c> with <c>referenceCategory</c> 1 and one
/// <c>messageSeriesIdentifier</c> per cancelled warning (S-124 Ed 2.0.0
/// Table 8-1, "new dataset with cancellation").
/// </para>
/// <para>
/// Edition 2.0.0 gives validity as a date and a UTC time of day for each
/// end. Edition 1.0.0 has no time of day, publication time or cancellation
/// date: validity ends are written as date-times and a cancellation as a
/// <c>messageReference</c> naming the cancelled warning.
/// </para>
/// <para>
/// The series is <c>ZZ00</c>'s local warnings, nationality <c>ZZ</c>
/// (user-assigned, not a country), agency "Synthetic Hydrographic Office
/// (fictitious)".
/// </para>
/// </remarks>
public static class S124WarningWriter
{
    /// <summary>The S-124 Edition 2.0.0 GML namespace.</summary>
    public const string Namespace2 = "http://www.iho.int/S124/gml/2.0";

    /// <summary>The S-124 Edition 1.0.0 GML namespace.</summary>
    public const string Namespace1 = "http://www.iho.int/S124/gml/1.0";

    /// <summary>The S-100 GML 1.0 profile namespace that Edition 1.0.0 datasets use.</summary>
    public const string S100Profile1 = "http://www.iho.int/S100/profile/s100gml/1.0";

    /// <summary>The warning series' name.</summary>
    public const string Series = "ZZ00 LOCAL";

    /// <summary>The issuing agency.</summary>
    public const string Agency = "Synthetic Hydrographic Office (fictitious)";

    /// <summary>The <c>gml:id</c> prefix of a warning.</summary>
    /// <param name="number">The warning number.</param>
    /// <param name="year">The year.</param>
    /// <returns>The prefix, such as <c>ZZ00.S124.0012.26</c>.</returns>
    public static string IdPrefix(int number, int year) =>
        string.Create(CultureInfo.InvariantCulture, $"ZZ00.S124.{number:0000}.{year % 100:00}");

    /// <summary>The message reference text for Edition 1.0.0, such as <c>ZZ00 LOCAL 0012/2026</c>.</summary>
    /// <param name="number">The warning number.</param>
    /// <param name="year">The year.</param>
    /// <returns>The text.</returns>
    public static string MessageReference(int number, int year) =>
        string.Create(CultureInfo.InvariantCulture, $"{Series} {number:0000}/{year}");

    /// <summary>The warning's positions, rounded to 10⁻⁷ degrees; an area's ring is closed.</summary>
    /// <param name="world">The world.</param>
    /// <param name="shape">The shape.</param>
    /// <returns>The positions.</returns>
    public static IReadOnlyList<GeoPosition> Positions(SyntheticWorld world, WarningShape shape)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(shape);
        var positions = shape.Points.Select(p => GmlWriting.Round(world.Frame.ToGeo(p.X, p.Y))).ToList();
        if (shape.Kind == WarningGeometryKind.Area)
        {
            positions.Add(positions[0]);
        }

        return positions;
    }

    /// <summary>Writes a warning.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The warning.</param>
    /// <param name="edition">2 for Edition 2.0.0, 1 for Edition 1.0.0.</param>
    /// <param name="issued">The dataset's reference date (UTC).</param>
    /// <returns>The UTF-8 encoded dataset.</returns>
    public static byte[] Build(SyntheticWorld world, S124WarningSpec spec, int edition, DateTime issued)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(spec);
        return edition switch
        {
            2 => GmlWriting.Document(xml => WriteEdition2(xml, world, spec, issued)),
            1 => GmlWriting.Document(xml => WriteEdition1(xml, world, spec)),
            _ => throw new ArgumentOutOfRangeException(nameof(edition), edition, "S-124 edition 1 or 2."),
        };
    }

    private static void WriteEdition2(XmlWriter xml, SyntheticWorld world, S124WarningSpec spec, DateTime issued)
    {
        const string ns = Namespace2;
        var id = IdPrefix(spec.WarningNumber, spec.Year);
        var positions = Positions(world, spec.Shape);

        xml.WriteStartElement("S124", "Dataset", ns);
        xml.WriteAttributeString("xmlns", "S100", null, GmlWriting.S100Gml5);
        xml.WriteAttributeString("xmlns", "gml", null, GmlWriting.Gml);
        xml.WriteAttributeString("xmlns", "xlink", null, GmlWriting.Xlink);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id);
        GmlWriting.BoundedBy(xml, positions);

        xml.WriteStartElement("S100", "DatasetIdentificationInformation", GmlWriting.S100Gml5);
        S100(xml, "encodingSpecification", "S-100 Part 10b");
        S100(xml, "encodingSpecificationEdition", "1.0");
        S100(xml, "productIdentifier", "S-124");
        S100(xml, "productEdition", "2.0.0");
        S100(xml, "applicationProfile", "1");
        S100(xml, "datasetFileIdentifier", spec.FileName);
        S100(xml, "datasetTitle", $"{spec.Title} (SYNTHETIC)");
        S100(xml, "datasetReferenceDate", issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        S100(xml, "datasetLanguage", "eng");
        S100(xml, "datasetAbstract", $"SYNTHETIC navigational warning in {world.Name}, a fictitious area. Not for navigation.");
        S100(xml, "datasetTopicCategory", "oceans");
        S100(xml, "datasetPurpose", "base");
        S100(xml, "updateNumber", "0");
        xml.WriteEndElement();

        xml.WriteStartElement("S124", "members", ns);

        // NavwarnPreamble, attributes in feature catalogue order.
        xml.WriteStartElement("S124", "NavwarnPreamble", ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id + ".0");
        xml.WriteStartElement("S124", "generalArea", ns);
        xml.WriteStartElement("S124", "locationName", ns);
        xml.WriteElementString("S124", "language", ns, "eng");
        xml.WriteElementString("S124", "text", ns, $"{world.Name} (fictitious)");
        xml.WriteEndElement();
        xml.WriteEndElement();
        MessageSeriesIdentifier(xml, ns, spec.WarningNumber, spec.Year);
        xml.WriteStartElement("S124", "navwarnTitle", ns);
        xml.WriteElementString("S124", "language", ns, "eng");
        xml.WriteElementString("S124", "text", ns, spec.Title);
        xml.WriteEndElement();
        if (spec.CancellationDate is { } cancellation)
        {
            xml.WriteElementString("S124", "cancellationDate", ns, GmlWriting.Time(cancellation));
        }

        xml.WriteElementString("S124", "intService", ns, "false");
        xml.WriteStartElement("S124", "navwarnTypeGeneral", ns);
        xml.WriteAttributeString("code", spec.TypeGeneral.ToString(CultureInfo.InvariantCulture));
        xml.WriteString(TypeGeneralLabel(spec.TypeGeneral));
        xml.WriteEndElement();
        xml.WriteElementString("S124", "publicationTime", ns, GmlWriting.Time(spec.Published));
        if (spec.Cancels.Count > 0)
        {
            Reference(xml, ns, "theReferences", id + ".1");
        }

        xml.WriteEndElement();

        if (spec.Cancels.Count > 0)
        {
            xml.WriteStartElement("S124", "References", ns);
            xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id + ".1");
            foreach (var cancelled in spec.Cancels)
            {
                MessageSeriesIdentifier(xml, ns, cancelled, spec.Year);
            }

            xml.WriteElementString("S124", "noMessageOnHand", ns, "false");
            xml.WriteStartElement("S124", "referenceCategory", ns);
            xml.WriteAttributeString("code", "1");
            xml.WriteString("Warning Cancellation");
            xml.WriteEndElement();
            foreach (var cancelled in spec.Cancels)
            {
                Reference(xml, ns, "theWarning", IdPrefix(cancelled, spec.Year) + ".0");
            }

            xml.WriteEndElement();
        }

        xml.WriteStartElement("S124", "NavwarnPart", ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id + ".2");
        if (spec.ValidFrom is not null || spec.ValidTo is not null)
        {
            xml.WriteStartElement("S124", "fixedDateRange", ns);
            if (spec.ValidTo is { } to)
            {
                TruncatedDate(xml, ns, "dateEnd", to);
            }

            if (spec.ValidFrom is { } from)
            {
                TruncatedDate(xml, ns, "dateStart", from);
            }

            if (spec.ValidTo is { } toTime)
            {
                xml.WriteElementString("S124", "timeOfDayEnd", ns, toTime.ToString("HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            }

            if (spec.ValidFrom is { } fromTime)
            {
                xml.WriteElementString("S124", "timeOfDayStart", ns, fromTime.ToString("HH:mm:ss'Z'", CultureInfo.InvariantCulture));
            }

            xml.WriteEndElement();
        }

        xml.WriteStartElement("S124", "warningInformation", ns);
        xml.WriteStartElement("S124", "information", ns);
        xml.WriteElementString("S124", "language", ns, "eng");
        xml.WriteElementString("S124", "text", ns, spec.Text);
        xml.WriteEndElement();
        xml.WriteEndElement();
        Reference(xml, ns, "header", id + ".0");
        xml.WriteStartElement("S124", "geometry", ns);
        Geometry(xml, GmlWriting.S100Gml5, id + ".2.G", spec.Shape.Kind, positions);
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void WriteEdition1(XmlWriter xml, SyntheticWorld world, S124WarningSpec spec)
    {
        const string ns = Namespace1;
        var id = IdPrefix(spec.WarningNumber, spec.Year);
        var positions = Positions(world, spec.Shape);

        xml.WriteStartElement("S124", "DataSet", ns);
        xml.WriteAttributeString("xmlns", "S100", null, S100Profile1);
        xml.WriteAttributeString("xmlns", "gml", null, GmlWriting.Gml);
        xml.WriteAttributeString("xmlns", "xlink", null, GmlWriting.Xlink);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id);
        GmlWriting.BoundedBy(xml, positions);

        xml.WriteStartElement("S100", "DatasetIdentificationInformation", S100Profile1);
        xml.WriteElementString("S100", "productIdentifier", S100Profile1, "S-124");
        xml.WriteElementString("S100", "productEdition", S100Profile1, "1.0.0");
        xml.WriteEndElement();

        xml.WriteStartElement("S124", "imember", ns);
        xml.WriteStartElement("S124", "NavwarnPreamble", ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id + ".0");
        xml.WriteStartElement("S124", "messageSeriesIdentifier", ns);
        xml.WriteElementString("S124", "warningNumber", ns, spec.WarningNumber.ToString(CultureInfo.InvariantCulture));
        xml.WriteElementString("S124", "year", ns, spec.Year.ToString(CultureInfo.InvariantCulture));
        xml.WriteElementString("S124", "productionAgency", ns, Agency);
        xml.WriteElementString("S124", "nameOfSeries", ns, Series);
        xml.WriteElementString("S124", "country", ns, "ZZ");
        xml.WriteEndElement();
        xml.WriteElementString("S124", "generalArea", ns, $"{world.Name} (fictitious)");
        xml.WriteElementString("S124", "title", ns, spec.Title);
        xml.WriteEndElement();
        xml.WriteEndElement();

        foreach (var cancelled in spec.Cancels)
        {
            xml.WriteStartElement("S124", "imember", ns);
            xml.WriteStartElement("S124", "References", ns);
            xml.WriteAttributeString("gml", "id", GmlWriting.Gml, string.Create(CultureInfo.InvariantCulture, $"{id}.1.{cancelled}"));
            xml.WriteElementString("S124", "referenceCategory", ns, "1");
            xml.WriteElementString("S124", "messageReference", ns, MessageReference(cancelled, spec.Year));
            xml.WriteEndElement();
            xml.WriteEndElement();
        }

        xml.WriteStartElement("S124", "member", ns);
        xml.WriteStartElement("S124", "NavwarnPart", ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, id + ".2");
        xml.WriteStartElement("S124", "geometry", ns);
        Geometry(xml, S100Profile1, id + ".2.G", spec.Shape.Kind, positions);
        xml.WriteEndElement();
        if (spec.ValidFrom is not null || spec.ValidTo is not null)
        {
            xml.WriteStartElement("S124", "fixedDateRange", ns);
            if (spec.ValidFrom is { } from)
            {
                xml.WriteElementString("S124", "dateStart", ns, GmlWriting.Time(from));
            }

            if (spec.ValidTo is { } to)
            {
                xml.WriteElementString("S124", "dateEnd", ns, GmlWriting.Time(to));
            }

            xml.WriteEndElement();
        }

        xml.WriteStartElement("S124", "warningInformation", ns);
        xml.WriteElementString("S124", "information", ns, spec.Text);
        xml.WriteEndElement();
        Reference(xml, ns, "header", id + ".0");
        xml.WriteEndElement();
        xml.WriteEndElement();

        xml.WriteEndElement();
    }

    private static void Geometry(XmlWriter xml, string s100, string id, WarningGeometryKind kind, IReadOnlyList<GeoPosition> positions)
    {
        switch (kind)
        {
            case WarningGeometryKind.Point:
                GmlWriting.PointProperty(xml, s100, id, positions[0]);
                break;
            case WarningGeometryKind.Line:
                GmlWriting.CurveProperty(xml, s100, id, positions);
                break;
            default:
                GmlWriting.SurfaceProperty(xml, s100, id, new GeoPolygon(positions, []));
                break;
        }
    }

    private static void MessageSeriesIdentifier(XmlWriter xml, string ns, int number, int year)
    {
        xml.WriteStartElement("S124", "messageSeriesIdentifier", ns);
        xml.WriteElementString("S124", "agencyResponsibleForProduction", ns, Agency);
        xml.WriteElementString("S124", "interoperabilityIdentifier", ns, "urn:mrn:" + IdPrefix(number, year));
        xml.WriteElementString("S124", "nameOfSeries", ns, Series);
        xml.WriteElementString("S124", "nationality", ns, "ZZ");
        xml.WriteElementString("S124", "warningNumber", ns, number.ToString(CultureInfo.InvariantCulture));
        xml.WriteStartElement("S124", "warningType", ns);
        xml.WriteAttributeString("code", "1");
        xml.WriteString("Local Navigational Warning");
        xml.WriteEndElement();
        xml.WriteElementString("S124", "year", ns, year.ToString(CultureInfo.InvariantCulture));
        xml.WriteEndElement();
    }

    private static void TruncatedDate(XmlWriter xml, string ns, string name, DateTime utc)
    {
        xml.WriteStartElement("S124", name, ns);
        xml.WriteElementString("S100", "date", GmlWriting.S100Gml5, utc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        xml.WriteEndElement();
    }

    private static void Reference(XmlWriter xml, string ns, string role, string targetId)
    {
        xml.WriteStartElement("S124", role, ns);
        xml.WriteAttributeString("xlink", "href", GmlWriting.Xlink, "#" + targetId);
        xml.WriteEndElement();
    }

    private static void S100(XmlWriter xml, string name, string value) =>
        xml.WriteElementString("S100", name, GmlWriting.S100Gml5, value);

    private static string TypeGeneralLabel(int code) => code switch
    {
        1 => "Aids to Navigation Changes",
        3 => "Drifting Hazards",
        5 => "Other Hazards",
        10 => "Newly Discovered Dangers",
        14 => "Routeing Change",
        17 => "Special Operations",
        18 => "Towing Operations",
        19 => "Works",
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Add the label from the S-124 feature catalogue."),
    };
}
