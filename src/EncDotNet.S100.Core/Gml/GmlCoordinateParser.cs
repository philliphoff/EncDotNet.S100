using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Features;

/// <summary>
/// Shared GML coordinate parsing utilities for S-100 Part 10b encoded datasets.
/// </summary>
/// <remarks>
/// All methods assume <c>EPSG:4326</c> coordinate ordering (latitude first,
/// longitude second) as required by S-100 Part 10b §6.2. Separator handling
/// tolerates both standard whitespace and comma-separated tokens (a
/// producer-bug compensation seen in some real-world S-122 and S-128 datasets).
/// A second producer-bug compensation auto-corrects longitude-first axis order
/// (seen in the US NWS S-411 sea-ice product) using the physical latitude bound
/// of ±90° — see <see cref="NormalizeAxisOrder"/>.
/// </remarks>
public static class GmlCoordinateParser
{
    private static readonly char[] Separators = [' ', '\t', '\n', '\r', ','];

    /// <summary>
    /// Corrects a parsed ordinate pair for producer axis-order violations.
    /// S-100 Part 10b §6.2 mandates latitude-first for <c>EPSG:4326</c>, but some
    /// real-world datasets (e.g. the US NWS S-411 sea-ice product) encode
    /// longitude first. Latitude is physically bounded to ±90°, so when the
    /// first ordinate's magnitude exceeds 90° while the second's does not, the
    /// pair is unambiguously longitude-first and is swapped. This is a strict
    /// no-op for all conformant latitude-first data (which never has |lat| &gt; 90°).
    /// </summary>
    private static (double Latitude, double Longitude) NormalizeAxisOrder(double first, double second)
        => Math.Abs(first) > 90.0 && Math.Abs(second) <= 90.0 ? (second, first) : (first, second);

    /// <summary>
    /// Parses a <c>gml:pos</c> value into a single coordinate pair.
    /// </summary>
    /// <returns>The parsed (latitude, longitude) pair, or <c>null</c> if parsing fails.</returns>
    public static (double Latitude, double Longitude)? ParsePos(string posValue)
    {
        var parts = posValue.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            double.TryParse(parts[0], CultureInfo.InvariantCulture, out var first) &&
            double.TryParse(parts[1], CultureInfo.InvariantCulture, out var second))
        {
            return NormalizeAxisOrder(first, second);
        }
        return null;
    }

    /// <summary>
    /// Parses a <c>gml:posList</c> value into a sequence of coordinate pairs.
    /// </summary>
    public static IReadOnlyList<(double Latitude, double Longitude)> ParsePosList(string posListValue)
    {
        var parts = posListValue.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var coords = new List<(double, double)>();

        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (double.TryParse(parts[i], CultureInfo.InvariantCulture, out var first) &&
                double.TryParse(parts[i + 1], CultureInfo.InvariantCulture, out var second))
            {
                coords.Add(NormalizeAxisOrder(first, second));
            }
        }

        return coords;
    }

    /// <summary>
    /// Extracts a point coordinate from a GML point property element by
    /// searching for <c>gml:pos</c> across common nesting patterns.
    /// </summary>
    public static (double Latitude, double Longitude)? ParsePointElement(XElement element, XNamespace? s100Ns = null)
    {
        var gmlNs = element.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;

        // Direct gml:pos child
        var pos = element.Element(gmlNs + "pos");
        if (pos is not null)
            return ParsePos(pos.Value);

        // S-100 GML profile: <S100:pointProperty><gml:Point><gml:pos>
        if (s100Ns is not null)
        {
            var pointProp = element.Element(s100Ns + "pointProperty");
            if (pointProp is not null)
            {
                pos = pointProp.Descendants(gmlNs + "pos").FirstOrDefault();
                if (pos is not null) return ParsePos(pos.Value);
            }
        }

        // Nested gml:Point/gml:pos
        pos = element.Descendants(gmlNs + "pos").FirstOrDefault();
        if (pos is not null)
            return ParsePos(pos.Value);

        return null;
    }

    /// <summary>
    /// Parses curve coordinates from a GML curve property element by
    /// extracting <c>gml:posList</c> and <c>gml:pos</c> children.
    /// </summary>
    public static IReadOnlyList<(double Latitude, double Longitude)> ParseCurveCoordinates(XElement curveContainer)
    {
        var gmlNs = curveContainer.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;
        var coords = new List<(double, double)>();

        foreach (var posList in curveContainer.Descendants(gmlNs + "posList"))
        {
            coords.AddRange(ParsePosList(posList.Value));
        }

        if (coords.Count == 0)
        {
            foreach (var pos in curveContainer.Descendants(gmlNs + "pos"))
            {
                var coord = ParsePos(pos.Value);
                if (coord is not null) coords.Add(coord.Value);
            }
        }

        return coords;
    }

    /// <summary>
    /// Parses surface coordinates (exterior ring and optional interior rings)
    /// from a GML surface property element.
    /// </summary>
    public static (IReadOnlyList<(double Latitude, double Longitude)> ExteriorRing,
                    IReadOnlyList<IReadOnlyList<(double Latitude, double Longitude)>> InteriorRings)
        ParseSurfaceCoordinates(XElement surfaceContainer)
    {
        var gmlNs = surfaceContainer.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;

        IReadOnlyList<(double, double)> exteriorRing = [];
        var interiorRings = new List<IReadOnlyList<(double, double)>>();

        var exterior = surfaceContainer.Descendants(gmlNs + "exterior").FirstOrDefault();
        if (exterior is not null)
        {
            exteriorRing = ParseRingCoordinates(exterior, gmlNs);
        }

        // Additive producer-bug fallback: only when the standard parse above
        // yielded no exterior vertices. Some datasets (e.g. S-128 GML 1.0
        // IC-ENC/DK catalogues) emit <gml:Polygon><gml:posList> directly,
        // omitting the <gml:exterior>/<gml:LinearRing> wrapper. Conformant
        // surfaces never reach this branch.
        if (exteriorRing.Count == 0)
        {
            exteriorRing = ParseRingCoordinates(surfaceContainer, gmlNs);
        }

        foreach (var interior in surfaceContainer.Descendants(gmlNs + "interior"))
        {
            interiorRings.Add(ParseRingCoordinates(interior, gmlNs));
        }

        return (exteriorRing, interiorRings);
    }

    private static IReadOnlyList<(double, double)> ParseRingCoordinates(XElement ringContainer, XNamespace gmlNs)
    {
        var posList = ringContainer.Descendants(gmlNs + "posList").FirstOrDefault();
        if (posList is not null)
            return ParsePosList(posList.Value);

        return ParsePosSequence(ringContainer.Descendants(gmlNs + "pos"));
    }

    /// <summary>
    /// Parses a sequence of <c>gml:pos</c> elements into coordinate pairs.
    /// </summary>
    /// <remarks>
    /// The conformant interpretation — each <c>gml:pos</c> carries a full
    /// position (≥ 2 ordinates) and yields one coordinate — is attempted
    /// first and is the only path taken by standard data. <em>Only</em> when
    /// that yields zero vertices is an additive producer-bug fallback tried:
    /// some S-128 GML 1.0 IC-ENC datasets split each coordinate's ordinates
    /// across consecutive single-value <c>gml:pos</c> elements, so the
    /// ordinates are flattened and paired up (lat, lon).
    /// </remarks>
    private static IReadOnlyList<(double, double)> ParsePosSequence(IEnumerable<XElement> posElements)
    {
        var elements = posElements as IReadOnlyList<XElement> ?? posElements.ToArray();
        if (elements.Count == 0)
            return [];

        // Standard path (unchanged for conformant data): each gml:pos is a
        // full position.
        var coords = new List<(double, double)>();
        foreach (var pos in elements)
        {
            var coord = ParsePos(pos.Value);
            if (coord is not null) coords.Add(coord.Value);
        }
        if (coords.Count > 0)
            return coords;

        // Additive fallback: the standard parse produced no vertices. If every
        // gml:pos holds exactly one ordinate, re-interpret them as a flat
        // ordinate stream and pair the values into (lat, lon) coordinates.
        bool allSingleOrdinate = elements.All(e =>
            e.Value.Split(Separators, StringSplitOptions.RemoveEmptyEntries).Length == 1);
        if (allSingleOrdinate)
        {
            var flattened = string.Join(' ', elements.Select(e => e.Value));
            return ParsePosList(flattened);
        }

        return [];
    }
}
