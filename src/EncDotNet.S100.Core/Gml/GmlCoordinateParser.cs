using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.DataModel;

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
/// (seen in the US NWS and NIC S-411 sea-ice products) using the physical
/// latitude bound of ±90°: a position list with any first ordinate beyond ±90°
/// is read longitude first. A ring whose longitudes all lie within ±90° cannot
/// be told apart this way, so a reader can call <see cref="DetectAxisOrder"/>
/// once for the whole dataset and pass <see cref="GmlAxisOrder.LongitudeFirst"/>
/// to every parse.
/// </remarks>
public static class GmlCoordinateParser
{
    private static readonly char[] Separators = [' ', '\t', '\n', '\r', ','];

    /// <summary>
    /// Whether an ordinate pair is unambiguously longitude first. S-100 Part 10b
    /// §6.2 mandates latitude-first for <c>EPSG:4326</c>, but some real-world
    /// datasets (e.g. the US NWS and NIC S-411 sea-ice products) encode
    /// longitude first. Latitude is physically bounded to ±90°, so a pair whose
    /// first ordinate exceeds 90° in magnitude while the second does not cannot
    /// be latitude first. Conformant data never matches.
    /// </summary>
    private static bool IsLongitudeFirst(double first, double second)
        => Math.Abs(first) > 90.0 && Math.Abs(second) <= 90.0;

    /// <summary>
    /// Scans every <c>gml:pos</c> and <c>gml:posList</c> under
    /// <paramref name="root"/> and reports whether the dataset is encoded
    /// longitude first.
    /// </summary>
    /// <remarks>
    /// One longitude-first position anywhere in a dataset makes it
    /// non-conformant, so treating the whole dataset as longitude first is
    /// safe. Doing so matters for rings whose longitudes all lie within ±90°
    /// (e.g. NIC Arctic ice east of Greenwich), which per-list detection would
    /// read latitude first and so draw as wedges (issue #760).
    /// </remarks>
    /// <param name="root">The dataset root element.</param>
    /// <returns>
    /// <see cref="GmlAxisOrder.LongitudeFirst"/> when any position is
    /// unambiguously longitude first; otherwise <see cref="GmlAxisOrder.Auto"/>.
    /// </returns>
    public static GmlAxisOrder DetectAxisOrder(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var gmlNs = root.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;
        foreach (var element in root.Descendants())
        {
            if (element.Name.Namespace != gmlNs
                || element.Name.LocalName is not ("posList" or "pos"))
            {
                continue;
            }

            var parts = element.Value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                if (double.TryParse(parts[i], CultureInfo.InvariantCulture, out var first) &&
                    double.TryParse(parts[i + 1], CultureInfo.InvariantCulture, out var second) &&
                    IsLongitudeFirst(first, second))
                {
                    return GmlAxisOrder.LongitudeFirst;
                }
            }
        }

        return GmlAxisOrder.Auto;
    }

    /// <summary>
    /// Parses a <c>gml:pos</c> value into a single coordinate pair.
    /// </summary>
    /// <returns>The parsed (latitude, longitude) pair, or <c>null</c> if parsing fails.</returns>
    public static GeoPosition? ParsePos(string posValue) => ParsePos(posValue, GmlAxisOrder.Auto);

    /// <summary>
    /// Parses a <c>gml:pos</c> value into a single coordinate pair, reading it
    /// in the given <paramref name="axisOrder"/>.
    /// </summary>
    /// <returns>The parsed (latitude, longitude) pair, or <c>null</c> if parsing fails.</returns>
    public static GeoPosition? ParsePos(string posValue, GmlAxisOrder axisOrder)
    {
        var parts = posValue.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            double.TryParse(parts[0], CultureInfo.InvariantCulture, out var first) &&
            double.TryParse(parts[1], CultureInfo.InvariantCulture, out var second))
        {
            return axisOrder == GmlAxisOrder.LongitudeFirst || IsLongitudeFirst(first, second)
                ? new GeoPosition(second, first)
                : new GeoPosition(first, second);
        }
        return null;
    }

    /// <summary>
    /// Parses a <c>gml:posList</c> value into a sequence of coordinate pairs.
    /// The whole list is read longitude first when any of its pairs is.
    /// </summary>
    public static IReadOnlyList<GeoPosition> ParsePosList(string posListValue) =>
        ParsePosList(posListValue, GmlAxisOrder.Auto);

    /// <summary>
    /// Parses a <c>gml:posList</c> value into a sequence of coordinate pairs,
    /// reading it in the given <paramref name="axisOrder"/>. With
    /// <see cref="GmlAxisOrder.Auto"/> the whole list is read longitude first
    /// when any of its pairs is.
    /// </summary>
    public static IReadOnlyList<GeoPosition> ParsePosList(string posListValue, GmlAxisOrder axisOrder)
    {
        var parts = posListValue.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        var pairs = new List<(double First, double Second)>(parts.Length / 2);
        bool longitudeFirst = axisOrder == GmlAxisOrder.LongitudeFirst;

        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (double.TryParse(parts[i], CultureInfo.InvariantCulture, out var first) &&
                double.TryParse(parts[i + 1], CultureInfo.InvariantCulture, out var second))
            {
                pairs.Add((first, second));
                longitudeFirst |= IsLongitudeFirst(first, second);
            }
        }

        var coords = new List<GeoPosition>(pairs.Count);
        foreach (var (first, second) in pairs)
            coords.Add(longitudeFirst ? new GeoPosition(second, first) : new GeoPosition(first, second));
        return coords;
    }

    /// <summary>
    /// Extracts a point coordinate from a GML point property element by
    /// searching for <c>gml:pos</c> across common nesting patterns.
    /// </summary>
    public static GeoPosition? ParsePointElement(XElement element, XNamespace? s100Ns = null) =>
        ParsePointElement(element, s100Ns, GmlAxisOrder.Auto);

    /// <summary>
    /// Extracts a point coordinate from a GML point property element, reading
    /// it in the given <paramref name="axisOrder"/>.
    /// </summary>
    public static GeoPosition? ParsePointElement(XElement element, XNamespace? s100Ns, GmlAxisOrder axisOrder)
    {
        var gmlNs = element.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;

        // Direct gml:pos child
        var pos = element.Element(gmlNs + "pos");
        if (pos is not null)
            return ParsePos(pos.Value, axisOrder);

        // S-100 GML profile: <S100:pointProperty><gml:Point><gml:pos>
        if (s100Ns is not null)
        {
            var pointProp = element.Element(s100Ns + "pointProperty");
            if (pointProp is not null)
            {
                pos = pointProp.Descendants(gmlNs + "pos").FirstOrDefault();
                if (pos is not null) return ParsePos(pos.Value, axisOrder);
            }
        }

        // Nested gml:Point/gml:pos
        pos = element.Descendants(gmlNs + "pos").FirstOrDefault();
        if (pos is not null)
            return ParsePos(pos.Value, axisOrder);

        return null;
    }

    /// <summary>
    /// Parses curve coordinates from a GML curve property element by
    /// extracting <c>gml:posList</c> and <c>gml:pos</c> children.
    /// </summary>
    public static IReadOnlyList<GeoPosition> ParseCurveCoordinates(XElement curveContainer) =>
        ParseCurveCoordinates(curveContainer, GmlAxisOrder.Auto);

    /// <summary>
    /// Parses curve coordinates from a GML curve property element, reading them
    /// in the given <paramref name="axisOrder"/>.
    /// </summary>
    public static IReadOnlyList<GeoPosition> ParseCurveCoordinates(XElement curveContainer, GmlAxisOrder axisOrder)
    {
        var gmlNs = curveContainer.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;
        var coords = new List<GeoPosition>();

        foreach (var posList in curveContainer.Descendants(gmlNs + "posList"))
        {
            coords.AddRange(ParsePosList(posList.Value, axisOrder));
        }

        if (coords.Count == 0)
        {
            foreach (var pos in curveContainer.Descendants(gmlNs + "pos"))
            {
                var coord = ParsePos(pos.Value, axisOrder);
                if (coord is not null) coords.Add(coord.Value);
            }
        }

        return coords;
    }

    /// <summary>
    /// Parses surface coordinates (exterior ring and optional interior rings)
    /// from a GML surface property element.
    /// </summary>
    public static (IReadOnlyList<GeoPosition> ExteriorRing,
                    IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings)
        ParseSurfaceCoordinates(XElement surfaceContainer) =>
        ParseSurfaceCoordinates(surfaceContainer, GmlAxisOrder.Auto);

    /// <summary>
    /// Parses surface coordinates (exterior ring and optional interior rings)
    /// from a GML surface property element, reading them in the given
    /// <paramref name="axisOrder"/>.
    /// </summary>
    public static (IReadOnlyList<GeoPosition> ExteriorRing,
                    IReadOnlyList<IReadOnlyList<GeoPosition>> InteriorRings)
        ParseSurfaceCoordinates(XElement surfaceContainer, GmlAxisOrder axisOrder)
    {
        var gmlNs = surfaceContainer.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;

        IReadOnlyList<GeoPosition> exteriorRing = [];
        var interiorRings = new List<IReadOnlyList<GeoPosition>>();

        var exterior = surfaceContainer.Descendants(gmlNs + "exterior").FirstOrDefault();
        if (exterior is not null)
        {
            exteriorRing = ParseRingCoordinates(exterior, gmlNs, axisOrder);
        }

        // Additive producer-bug fallback: only when the standard parse above
        // yielded no exterior vertices. Some datasets (e.g. S-128 GML 1.0
        // IC-ENC/DK catalogues) emit <gml:Polygon><gml:posList> directly,
        // omitting the <gml:exterior>/<gml:LinearRing> wrapper. Conformant
        // surfaces never reach this branch.
        if (exteriorRing.Count == 0)
        {
            exteriorRing = ParseRingCoordinates(surfaceContainer, gmlNs, axisOrder);
        }

        foreach (var interior in surfaceContainer.Descendants(gmlNs + "interior"))
        {
            interiorRings.Add(ParseRingCoordinates(interior, gmlNs, axisOrder));
        }

        return (exteriorRing, interiorRings);
    }

    private static IReadOnlyList<GeoPosition> ParseRingCoordinates(XElement ringContainer, XNamespace gmlNs, GmlAxisOrder axisOrder)
    {
        var posList = ringContainer.Descendants(gmlNs + "posList").FirstOrDefault();
        if (posList is not null)
            return ParsePosList(posList.Value, axisOrder);

        return ParsePosSequence(ringContainer.Descendants(gmlNs + "pos"), axisOrder);
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
    private static IReadOnlyList<GeoPosition> ParsePosSequence(IEnumerable<XElement> posElements, GmlAxisOrder axisOrder)
    {
        var elements = posElements as IReadOnlyList<XElement> ?? posElements.ToArray();
        if (elements.Count == 0)
            return [];

        // Standard path (unchanged for conformant data): each gml:pos is a
        // full position.
        var coords = new List<GeoPosition>();
        foreach (var pos in elements)
        {
            var coord = ParsePos(pos.Value, axisOrder);
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
            return ParsePosList(flattened, axisOrder);
        }

        return [];
    }
}
