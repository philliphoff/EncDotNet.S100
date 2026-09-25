using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Collections.Indexing;

/// <summary>
/// Parses the GML polygons of an S-100 exchange-catalogue
/// <c>dataCoverage/boundingPolygon</c> (S-100 Part 17) into
/// <see cref="GeoPolygon"/>s.
/// </summary>
/// <remarks>
/// <para>
/// Catalogues wrap one or more <c>gml:Polygon</c> elements (optionally inside
/// <c>gex:EX_BoundingPolygon/gex:polygon</c>), each with a
/// <c>gml:exterior</c> and optional <c>gml:interior</c> rings whose vertices
/// are given as a <c>gml:posList</c> or a sequence of <c>gml:pos</c>.
/// <c>gml:Surface</c> / <c>gml:PolygonPatch</c> encodings are accepted too.
/// Elements are matched by local name so GML 3.1 and 3.2 both work.
/// </para>
/// <para>
/// Coordinates follow the axis order of the declared CRS: EPSG:4326 (the
/// default) is latitude, longitude; OGC CRS84 is longitude, latitude.
/// Malformed polygons are skipped rather than failing the whole coverage.
/// </para>
/// </remarks>
internal static class GmlCoverageParser
{
    /// <summary>
    /// Parses every polygon in <paramref name="xml"/>. Returns an empty list
    /// when the fragment is empty, malformed, or holds no valid polygon.
    /// </summary>
    public static IReadOnlyList<GeoPolygon> Parse(string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            return [];

        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (XmlException)
        {
            return [];
        }

        var polygons = new List<GeoPolygon>();
        foreach (var element in root.DescendantsAndSelf())
        {
            var name = element.Name.LocalName;
            if (name is not ("Polygon" or "PolygonPatch"))
                continue;

            var lonLat = IsLongitudeFirst(element);
            var exterior = element.Elements()
                .FirstOrDefault(e => e.Name.LocalName is "exterior" or "outerBoundaryIs");
            var ring = exterior is null ? null : ReadRing(exterior, lonLat);
            if (ring is null)
                continue;

            var holes = element.Elements()
                .Where(e => e.Name.LocalName is "interior" or "innerBoundaryIs")
                .Select(e => ReadRing(e, lonLat))
                .OfType<IReadOnlyList<GeoPosition>>()
                .ToArray();

            polygons.Add(new GeoPolygon(ring, holes));
        }

        return polygons;
    }

    /// <summary>
    /// Reads the ring under a <c>gml:exterior</c> / <c>gml:interior</c>
    /// element, or <see langword="null"/> when it has fewer than three
    /// vertices or unparsable coordinates.
    /// </summary>
    private static IReadOnlyList<GeoPosition>? ReadRing(XElement boundary, bool lonLat)
    {
        var ringElement = boundary.Descendants().FirstOrDefault(e => e.Name.LocalName == "LinearRing");
        if (ringElement is null)
            return null;

        var dimension = ReadDimension(ringElement);
        var values = new List<double>();

        var posList = ringElement.Elements().FirstOrDefault(e => e.Name.LocalName == "posList");
        if (posList is not null)
        {
            dimension = ReadDimension(posList) ?? dimension;
            if (!TryParseNumbers(posList.Value, values))
                return null;
        }
        else
        {
            foreach (var pos in ringElement.Elements().Where(e => e.Name.LocalName == "pos"))
            {
                dimension ??= ReadDimension(pos);
                if (!TryParseNumbers(pos.Value, values))
                    return null;
            }
        }

        var stride = dimension is >= 2 ? dimension.Value : 2;
        if (values.Count < 3 * stride || values.Count % stride != 0)
            return null;

        var ring = new GeoPosition[values.Count / stride];
        for (var i = 0; i < ring.Length; i++)
        {
            var a = values[i * stride];
            var b = values[i * stride + 1];
            ring[i] = lonLat ? new GeoPosition(b, a) : new GeoPosition(a, b);
            if (!IsValid(ring[i]))
                return null;
        }

        return ring;
    }

    private static bool IsValid(GeoPosition p) =>
        p.Latitude is >= -90 and <= 90 && p.Longitude is >= -360 and <= 360;

    private static int? ReadDimension(XElement element)
    {
        var attr = element.Attribute("srsDimension")?.Value;
        return int.TryParse(attr, NumberStyles.None, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static bool TryParseNumbers(string text, List<double> values)
    {
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                return false;
            values.Add(value);
        }

        return true;
    }

    /// <summary>
    /// True when the nearest declared <c>srsName</c> is an OGC CRS84 variant
    /// (longitude, latitude). EPSG:4326 and an undeclared CRS are latitude,
    /// longitude.
    /// </summary>
    private static bool IsLongitudeFirst(XElement polygon)
    {
        for (var e = polygon; e is not null; e = e.Parent)
        {
            var srs = e.Attribute("srsName")?.Value;
            if (srs is not null)
                return srs.Contains("CRS84", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
