using System.Globalization;
using System.Xml;
using EncDotNet.S100.Testing.Scenarios.Geometry;
using EncDotNet.S100.Testing.Scenarios.World;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>A day of the year, as a yearless <c>S100_TruncatedDate</c> (<c>--MM-DD</c>).</summary>
/// <param name="Month">The month, 1 to 12.</param>
/// <param name="Day">The day of the month.</param>
public readonly record struct DayOfYear(int Month, int Day)
{
    /// <summary>The truncated date text, such as <c>--03-01</c>.</summary>
    public string Text => string.Create(CultureInfo.InvariantCulture, $"--{Month:00}-{Day:00}");
}

/// <summary>
/// A season that repeats every year (<c>periodicDateRange</c>), from the
/// start of <paramref name="Start"/> to the end of <paramref name="End"/>.
/// </summary>
/// <param name="Start">The first day.</param>
/// <param name="End">The last day (on or after <paramref name="Start"/> in the same year).</param>
public sealed record Season(DayOfYear Start, DayOfYear End)
{
    /// <summary>Whether <paramref name="utc"/> falls in the season (by UTC date).</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns><c>true</c> when the date is in the season.</returns>
    public bool Contains(DateTime utc)
    {
        var day = (utc.Month, utc.Day);
        return day.CompareTo((Start.Month, Start.Day)) >= 0 && day.CompareTo((End.Month, End.Day)) <= 0;
    }
}

/// <summary>An S-122 marine protected area, one per dataset, with its managing authority.</summary>
/// <param name="DatasetName">The file name without extension.</param>
/// <param name="Name">The area's name.</param>
/// <param name="Area">The protected area.</param>
/// <param name="Coverage">The dataset's <c>DataCoverage</c>.</param>
/// <param name="Seasons">When the restriction applies each year; empty for all year.</param>
/// <param name="Authority">The managing authority's name.</param>
public sealed record S122ProtectedAreaSpec(
    string DatasetName,
    string Name,
    LocalRect Area,
    LocalRect Coverage,
    IReadOnlyList<Season> Seasons,
    string Authority)
{
    /// <summary>The IUCN category (<c>categoryOfMarineProtectedArea</c>); 5 is IUCN Category IV.</summary>
    public int CategoryOfMarineProtectedArea { get; init; } = 5;

    /// <summary>The <c>categoryOfRestrictedArea</c>; 7 is seal sanctuary.</summary>
    public int CategoryOfRestrictedArea { get; init; } = 7;

    /// <summary>The <c>restriction</c>; 27 is speed restricted.</summary>
    public int Restriction { get; init; } = 27;

    /// <summary>The <c>jurisdiction</c>; 2 is national.</summary>
    public int Jurisdiction { get; init; } = 2;

    /// <summary>The <c>categoryOfAuthority</c>; 12 is environmental.</summary>
    public int CategoryOfAuthority { get; init; } = 12;

    /// <summary>The <c>status</c>: 5 periodic/intermittent with seasons, else 1 permanent.</summary>
    public int Status => Seasons.Count > 0 ? 5 : 1;

    /// <summary>The display scale limits of the <c>DataCoverage</c> (largest and smallest scale denominators).</summary>
    public (int Maximum, int Minimum) DisplayScales { get; init; } = (3000, 90000);

    /// <summary>The file name, with the <c>.gml</c> extension.</summary>
    public string FileName => DatasetName + ".gml";

    /// <summary>The dataset's <c>gml:id</c>, also the prefix of its members' ids.</summary>
    public string Id => "ZZ00.S122." + DatasetName;

    /// <summary>Whether the restriction applies at <paramref name="utc"/>.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns><c>true</c> when in force.</returns>
    public bool InForce(DateTime utc) => Seasons.Count == 0 || Seasons.Any(s => s.Contains(utc));
}

/// <summary>
/// Writes S-122 Edition 2.0.0 marine protected area datasets as S-100
/// Part 10b GML (S-100 GML 5.0), with the bindings, attribute order and
/// value types of the bundled S-122 2.0.0 feature catalogue.
/// </summary>
/// <remarks>
/// <para>
/// Each dataset holds a <c>DataCoverage</c>, one <c>MarineProtectedArea</c>
/// and the <c>Authority</c> that manages it, bound by the area's
/// <c>responsibleAuthority</c> role (association
/// <c>ProtectedAreaAuthority</c>). Names are <c>featureName</c> complex
/// attributes (language, name, <c>nameUsage</c> 1); a seasonal restriction
/// is one <c>periodicDateRange</c> per season, its ends yearless
/// <c>S100_TruncatedDate</c>s in <c>S100:gMonthDay</c>.
/// </para>
/// <para>
/// Enumerations are written as their numeric codes, as the readers in this
/// repository expect. The S-122 2.0.0 schema isn't bundled; the namespace
/// follows the pattern of S-124 Edition 2.0.0's
/// (<c>http://www.iho.int/S124/gml/2.0</c>), and readers identify the
/// product from <c>productIdentifier</c>.
/// </para>
/// </remarks>
public static class S122ProtectedAreaWriter
{
    /// <summary>The S-122 GML namespace.</summary>
    public const string Namespace = "http://www.iho.int/S122/gml/2.0";

    /// <summary>The area's polygon, rounded to 10⁻⁷ degrees, clockwise.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The area.</param>
    /// <returns>The polygon.</returns>
    public static GeoPolygon Polygon(SyntheticWorld world, S122ProtectedAreaSpec spec)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(spec);
        return GmlWriting.Rectangle(world.Frame, spec.Area);
    }

    /// <summary>The coverage polygon, rounded to 10⁻⁷ degrees, clockwise.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The area.</param>
    /// <returns>The polygon.</returns>
    public static GeoPolygon CoveragePolygon(SyntheticWorld world, S122ProtectedAreaSpec spec)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(spec);
        return GmlWriting.Rectangle(world.Frame, spec.Coverage);
    }

    /// <summary>Writes a dataset.</summary>
    /// <param name="world">The world.</param>
    /// <param name="spec">The area.</param>
    /// <param name="issued">The dataset's reference date (UTC).</param>
    /// <returns>The UTF-8 encoded dataset.</returns>
    public static byte[] Build(SyntheticWorld world, S122ProtectedAreaSpec spec, DateTime issued)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(spec);
        return GmlWriting.Document(xml => Write(xml, world, spec, issued));
    }

    private static void Write(XmlWriter xml, SyntheticWorld world, S122ProtectedAreaSpec spec, DateTime issued)
    {
        const string ns = Namespace;
        var coverage = CoveragePolygon(world, spec);
        var area = Polygon(world, spec);

        xml.WriteStartElement("S122", "Dataset", ns);
        xml.WriteAttributeString("xmlns", "S100", null, GmlWriting.S100Gml5);
        xml.WriteAttributeString("xmlns", "gml", null, GmlWriting.Gml);
        xml.WriteAttributeString("xmlns", "xlink", null, GmlWriting.Xlink);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, spec.Id);
        GmlWriting.BoundedBy(xml, coverage.Exterior.Concat(area.Exterior).ToList());

        xml.WriteStartElement("S100", "DatasetIdentificationInformation", GmlWriting.S100Gml5);
        S100(xml, "encodingSpecification", "S-100 Part 10b");
        S100(xml, "encodingSpecificationEdition", "1.0");
        S100(xml, "productIdentifier", "S-122");
        S100(xml, "productEdition", "2.0.0");
        S100(xml, "applicationProfile", "1");
        S100(xml, "datasetFileIdentifier", spec.FileName);
        S100(xml, "datasetTitle", $"{spec.Name} (SYNTHETIC)");
        S100(xml, "datasetReferenceDate", issued.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        S100(xml, "datasetLanguage", "eng");
        S100(xml, "datasetAbstract", $"SYNTHETIC marine protected area in {world.Name}, a fictitious area. Not for navigation.");
        S100(xml, "datasetTopicCategory", "environment");
        S100(xml, "datasetPurpose", "base");
        S100(xml, "updateNumber", "0");
        xml.WriteEndElement();

        xml.WriteStartElement("S122", "members", ns);

        // DataCoverage: display scales, then geometry.
        xml.WriteStartElement("S122", "DataCoverage", ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, spec.Id + ".0");
        Element(xml, "maximumDisplayScale", spec.DisplayScales.Maximum);
        Element(xml, "minimumDisplayScale", spec.DisplayScales.Minimum);
        xml.WriteStartElement("S122", "geometry", ns);
        GmlWriting.SurfaceProperty(xml, GmlWriting.S100Gml5, spec.Id + ".0.G", coverage);
        xml.WriteEndElement();
        xml.WriteEndElement();

        // MarineProtectedArea, attributes in feature catalogue order:
        // inherited FeatureType attributes, then its own.
        xml.WriteStartElement("S122", "MarineProtectedArea", ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, spec.Id + ".1");
        FeatureName(xml, spec.Name);
        foreach (var season in spec.Seasons)
        {
            xml.WriteStartElement("S122", "periodicDateRange", ns);
            TruncatedDate(xml, "dateStart", season.Start);
            TruncatedDate(xml, "dateEnd", season.End);
            xml.WriteEndElement();
        }

        Element(xml, "categoryOfMarineProtectedArea", spec.CategoryOfMarineProtectedArea);
        Element(xml, "categoryOfRestrictedArea", spec.CategoryOfRestrictedArea);
        Element(xml, "jurisdiction", spec.Jurisdiction);
        Element(xml, "restriction", spec.Restriction);
        Element(xml, "status", spec.Status);
        xml.WriteStartElement("S122", "responsibleAuthority", ns);
        xml.WriteAttributeString("xlink", "href", GmlWriting.Xlink, "#" + spec.Id + ".2");
        xml.WriteEndElement();
        xml.WriteStartElement("S122", "geometry", ns);
        GmlWriting.SurfaceProperty(xml, GmlWriting.S100Gml5, spec.Id + ".1.G", area);
        xml.WriteEndElement();
        xml.WriteEndElement();

        // Authority (information type).
        xml.WriteStartElement("S122", "Authority", ns);
        xml.WriteAttributeString("gml", "id", GmlWriting.Gml, spec.Id + ".2");
        FeatureName(xml, spec.Authority);
        Element(xml, "categoryOfAuthority", spec.CategoryOfAuthority);
        xml.WriteEndElement();

        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    private static void FeatureName(XmlWriter xml, string name)
    {
        xml.WriteStartElement("S122", "featureName", Namespace);
        xml.WriteElementString("S122", "language", Namespace, "eng");
        xml.WriteElementString("S122", "name", Namespace, name);
        Element(xml, "nameUsage", 1);
        xml.WriteEndElement();
    }

    private static void TruncatedDate(XmlWriter xml, string name, DayOfYear day)
    {
        xml.WriteStartElement("S122", name, Namespace);
        xml.WriteElementString("S100", "gMonthDay", GmlWriting.S100Gml5, day.Text);
        xml.WriteEndElement();
    }

    private static void Element(XmlWriter xml, string name, int value) =>
        xml.WriteElementString("S122", name, Namespace, value.ToString(CultureInfo.InvariantCulture));

    private static void S100(XmlWriter xml, string name, string value) =>
        xml.WriteElementString("S100", name, GmlWriting.S100Gml5, value);
}
