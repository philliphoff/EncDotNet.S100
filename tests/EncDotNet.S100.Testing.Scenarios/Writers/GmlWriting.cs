using System.Globalization;
using System.Text;
using System.Xml;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Testing.Scenarios.Geometry;

namespace EncDotNet.S100.Testing.Scenarios.Writers;

/// <summary>
/// Shared pieces of the generator's S-100 Part 10b GML writers: fixed
/// writer settings, number and position formatting, and geometry.
/// </summary>
/// <remarks>
/// Output is UTF-8 without a byte-order mark, indented with two spaces,
/// with <c>\n</c> line endings. Positions are latitude first (EPSG:4326)
/// with seven decimals, the generator's coordinate resolution.
/// </remarks>
public static class GmlWriting
{
    /// <summary>The GML 3.2 namespace.</summary>
    public const string Gml = "http://www.opengis.net/gml/3.2";

    /// <summary>The XLink namespace.</summary>
    public const string Xlink = "http://www.w3.org/1999/xlink";

    /// <summary>The S-100 GML 5.0 namespace.</summary>
    public const string S100Gml5 = "http://www.iho.int/s100gml/5.0";

    /// <summary>The CRS name written on geometries and envelopes.</summary>
    public const string Srs = "http://www.opengis.net/def/crs/EPSG/0/4326";

    /// <summary>Writes a document to bytes with the generator's fixed settings.</summary>
    /// <param name="write">Writes the document's content.</param>
    /// <returns>The UTF-8 bytes.</returns>
    public static byte[] Document(Action<XmlWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            Indent = true,
            IndentChars = "  ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };
        using var stream = new MemoryStream();
        using (var xml = XmlWriter.Create(stream, settings))
        {
            xml.WriteStartDocument();
            write(xml);
            xml.WriteEndDocument();
        }

        return stream.ToArray();
    }

    /// <summary>A position rounded to the generator's resolution (10⁻⁷ degrees).</summary>
    /// <param name="position">The position.</param>
    /// <returns>The rounded position.</returns>
    public static GeoPosition Round(GeoPosition position) =>
        new(Math.Round(position.Latitude, 7), Math.Round(position.Longitude, 7));

    /// <summary>A number in the invariant culture, round-trippable.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The text.</returns>
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    /// <summary>A UTC time in ISO 8601 with a <c>Z</c>.</summary>
    /// <param name="utc">The time (UTC).</param>
    /// <returns>The text.</returns>
    public static string Time(DateTime utc) => utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A position as <c>lat lon</c>.</summary>
    /// <param name="position">The position.</param>
    /// <returns>The text.</returns>
    public static string Pos(GeoPosition position) =>
        position.Latitude.ToString("0.0######", CultureInfo.InvariantCulture) + " " + position.Longitude.ToString("0.0######", CultureInfo.InvariantCulture);

    /// <summary>A position list.</summary>
    /// <param name="positions">The positions.</param>
    /// <returns>The text.</returns>
    public static string PosList(IEnumerable<GeoPosition> positions) => string.Join(' ', positions.Select(Pos));

    /// <summary>Writes <c>gml:boundedBy</c> around <paramref name="positions"/>.</summary>
    /// <param name="xml">The writer.</param>
    /// <param name="positions">The positions.</param>
    public static void BoundedBy(XmlWriter xml, IReadOnlyCollection<GeoPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(positions);
        xml.WriteStartElement("gml", "boundedBy", Gml);
        xml.WriteStartElement("gml", "Envelope", Gml);
        xml.WriteAttributeString("srsName", Srs);
        xml.WriteAttributeString("srsDimension", "2");
        xml.WriteElementString("gml", "lowerCorner", Gml, Pos(new GeoPosition(positions.Min(p => p.Latitude), positions.Min(p => p.Longitude))));
        xml.WriteElementString("gml", "upperCorner", Gml, Pos(new GeoPosition(positions.Max(p => p.Latitude), positions.Max(p => p.Longitude))));
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>Writes an S-100 point property.</summary>
    /// <param name="xml">The writer.</param>
    /// <param name="s100">The S-100 GML namespace.</param>
    /// <param name="id">The point's <c>gml:id</c>.</param>
    /// <param name="position">The position.</param>
    public static void PointProperty(XmlWriter xml, string s100, string id, GeoPosition position)
    {
        ArgumentNullException.ThrowIfNull(xml);
        xml.WriteStartElement("S100", "pointProperty", s100);
        xml.WriteStartElement("S100", "Point", s100);
        xml.WriteAttributeString("gml", "id", Gml, id);
        xml.WriteAttributeString("srsName", Srs);
        xml.WriteElementString("gml", "pos", Gml, Pos(position));
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>Writes an S-100 curve property.</summary>
    /// <param name="xml">The writer.</param>
    /// <param name="s100">The S-100 GML namespace.</param>
    /// <param name="id">The curve's <c>gml:id</c>.</param>
    /// <param name="positions">The positions in order.</param>
    public static void CurveProperty(XmlWriter xml, string s100, string id, IReadOnlyList<GeoPosition> positions)
    {
        ArgumentNullException.ThrowIfNull(xml);
        xml.WriteStartElement("S100", "curveProperty", s100);
        xml.WriteStartElement("S100", "Curve", s100);
        xml.WriteAttributeString("gml", "id", Gml, id);
        xml.WriteAttributeString("srsName", Srs);
        xml.WriteStartElement("gml", "segments", Gml);
        xml.WriteStartElement("gml", "LineStringSegment", Gml);
        xml.WriteElementString("gml", "posList", Gml, PosList(positions));
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>Writes an S-100 surface property.</summary>
    /// <param name="xml">The writer.</param>
    /// <param name="s100">The S-100 GML namespace.</param>
    /// <param name="id">The surface's <c>gml:id</c>.</param>
    /// <param name="polygon">The polygon (closed rings).</param>
    public static void SurfaceProperty(XmlWriter xml, string s100, string id, GeoPolygon polygon)
    {
        ArgumentNullException.ThrowIfNull(xml);
        ArgumentNullException.ThrowIfNull(polygon);
        xml.WriteStartElement("S100", "surfaceProperty", s100);
        xml.WriteStartElement("S100", "Surface", s100);
        xml.WriteAttributeString("gml", "id", Gml, id);
        xml.WriteAttributeString("srsName", Srs);
        xml.WriteStartElement("gml", "patches", Gml);
        xml.WriteStartElement("gml", "PolygonPatch", Gml);
        Ring(xml, "exterior", polygon.Exterior);
        foreach (var hole in polygon.Holes)
        {
            Ring(xml, "interior", hole);
        }

        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
        xml.WriteEndElement();
    }

    /// <summary>
    /// A closed rectangle over a local rectangle, its exterior clockwise on
    /// the map like every <see cref="GeoPolygon"/> (S-100 Part 7 §7-4.3.2).
    /// </summary>
    /// <param name="frame">The world frame.</param>
    /// <param name="rect">The rectangle.</param>
    /// <returns>The polygon.</returns>
    public static GeoPolygon Rectangle(World.WorldFrame frame, World.LocalRect rect)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return new GeoPolygon(
            [
                Round(frame.ToGeo(rect.West, rect.South)),
                Round(frame.ToGeo(rect.West, rect.North)),
                Round(frame.ToGeo(rect.East, rect.North)),
                Round(frame.ToGeo(rect.East, rect.South)),
                Round(frame.ToGeo(rect.West, rect.South)),
            ],
            []);
    }

    private static void Ring(XmlWriter xml, string role, IReadOnlyList<GeoPosition> ring)
    {
        xml.WriteStartElement("gml", role, Gml);
        xml.WriteStartElement("gml", "LinearRing", Gml);
        xml.WriteElementString("gml", "posList", Gml, PosList(ring));
        xml.WriteEndElement();
        xml.WriteEndElement();
    }
}
