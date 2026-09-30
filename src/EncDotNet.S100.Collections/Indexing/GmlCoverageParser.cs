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
/// <c>gml:Surface</c> / <c>gml:PolygonPatch</c> encodings are accepted too, as
/// is a bare <c>gml:LineString</c> or <c>gml:LinearRing</c> outline (which
/// several producers write), read as a polygon without holes. Elements are
/// matched by local name so GML 3.1 and 3.2 both work.
/// </para>
/// <para>
/// Coordinates follow the axis order of the declared CRS: EPSG:4326 (the
/// default) is latitude, longitude; OGC CRS84 is longitude, latitude. Some
/// producers declare EPSG:4326 but write longitude first, so when the
/// declared order contradicts the dataset's bounding box (or yields
/// latitudes beyond ±90°) and the swapped order does not, the swapped order
/// is used. Malformed polygons are skipped rather than failing the whole
/// coverage.
/// </para>
/// </remarks>
internal static class GmlCoverageParser
{
    /// <summary>
    /// Parses every polygon in <paramref name="xml"/>. Returns an empty list
    /// when the fragment is empty, malformed, or holds no valid polygon.
    /// </summary>
    /// <param name="xml">The <c>boundingPolygon</c> element's XML.</param>
    /// <param name="expected">
    /// The dataset's bounding box, when known, used to detect coordinates
    /// written in the other axis order than the CRS declares.
    /// </param>
    public static IReadOnlyList<GeoPolygon> Parse(string? xml, GeoBounds? expected = null)
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
            List<(double A, double B)>? exterior;
            List<List<(double A, double B)>> holes = [];
            if (name is "Polygon" or "PolygonPatch")
            {
                var boundary = element.Elements()
                    .FirstOrDefault(e => e.Name.LocalName is "exterior" or "outerBoundaryIs");
                exterior = boundary?.Descendants().FirstOrDefault(e => e.Name.LocalName == "LinearRing") is { } ring
                    ? ReadPairs(ring)
                    : null;
                holes = element.Elements()
                    .Where(e => e.Name.LocalName is "interior" or "innerBoundaryIs")
                    .Select(e => e.Descendants().FirstOrDefault(d => d.Name.LocalName == "LinearRing") is { } r ? ReadPairs(r) : null)
                    .OfType<List<(double A, double B)>>()
                    .ToList();
            }
            else if (name is "LineString" || (name is "LinearRing" && !IsInsidePolygon(element)))
            {
                // A bare outline: the polygon's exterior, without holes.
                exterior = ReadPairs(element);
            }
            else
            {
                continue;
            }

            if (exterior is null)
                continue;

            var lonLat = ResolveLongitudeFirst(IsLongitudeFirst(element), exterior, expected);
            var outer = ToRing(exterior, lonLat);
            if (outer is null)
                continue;

            polygons.Add(new GeoPolygon(
                outer,
                holes.Select(h => ToRing(h, lonLat)).OfType<IReadOnlyList<GeoPosition>>().ToArray()));
        }

        return polygons;
    }

    private static bool IsInsidePolygon(XElement element) =>
        element.Ancestors().Any(a => a.Name.LocalName is "Polygon" or "PolygonPatch" or "LineString");

    /// <summary>
    /// Reads the coordinate pairs (in document order, before any axis
    /// interpretation) of a ring or line, or <see langword="null"/> when it
    /// has fewer than three vertices or unparsable coordinates.
    /// </summary>
    private static List<(double A, double B)>? ReadPairs(XElement ringElement)
    {
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

        var pairs = new List<(double A, double B)>(values.Count / stride);
        for (var i = 0; i < values.Count; i += stride)
            pairs.Add((values[i], values[i + 1]));
        return pairs;
    }

    private static IReadOnlyList<GeoPosition>? ToRing(List<(double A, double B)> pairs, bool lonLat)
    {
        var ring = new GeoPosition[pairs.Count];
        for (var i = 0; i < ring.Length; i++)
        {
            var (a, b) = pairs[i];
            ring[i] = lonLat ? new GeoPosition(b, a) : new GeoPosition(a, b);
            if (!IsValid(ring[i]))
                return null;
        }

        return ring;
    }

    /// <summary>
    /// The axis order to read <paramref name="pairs"/> in: the declared one,
    /// unless it gives impossible latitudes or falls outside
    /// <paramref name="expected"/> while the swapped order does not.
    /// </summary>
    private static bool ResolveLongitudeFirst(bool declared, List<(double A, double B)> pairs, GeoBounds? expected)
    {
        var declaredScore = Fit(declared);
        var swappedScore = Fit(!declared);
        return swappedScore > declaredScore ? !declared : declared;

        // 2: every vertex valid and inside the expected box; 1: valid; 0: invalid.
        int Fit(bool lonLat)
        {
            var inside = expected is not null;
            foreach (var (a, b) in pairs)
            {
                var p = lonLat ? new GeoPosition(b, a) : new GeoPosition(a, b);
                if (!IsValid(p))
                    return 0;
                if (inside && !Contains(expected!.Value, p))
                    inside = false;
            }

            return inside ? 2 : 1;
        }
    }

    /// <summary>True when <paramref name="p"/> lies in <paramref name="box"/>, with a small tolerance.</summary>
    private static bool Contains(GeoBounds box, GeoPosition p)
    {
        const double tolerance = 0.01;
        if (p.Latitude < box.South - tolerance || p.Latitude > box.North + tolerance)
            return false;

        return box.CrossesAntimeridian
            ? p.Longitude >= box.West - tolerance || p.Longitude <= box.East + tolerance
            : p.Longitude >= box.West - tolerance && p.Longitude <= box.East + tolerance;
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
    private static bool IsLongitudeFirst(XElement element)
    {
        for (var e = element; e is not null; e = e.Parent)
        {
            var srs = e.Attribute("srsName")?.Value;
            if (srs is not null)
                return srs.Contains("CRS84", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
