using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Features;

/// <summary>
/// Shared GML coordinate parsing utilities for S-100 Part 10b encoded datasets.
/// </summary>
/// <remarks>
/// Positions are read latitude first, the <c>EPSG:4326</c> order S-100 Part
/// 10b §6.2 requires, unless the caller passes
/// <see cref="GmlAxisOrder.LongitudeFirst"/>. Readers decide the order once
/// per dataset with <see cref="AssessAxisOrder"/>, the shared axis-order check
/// (<see cref="AxisOrderCheck"/>, issue #650), and pass it to every parse, so
/// a dataset is never split between orders. Separator handling tolerates both
/// standard whitespace and comma-separated tokens (a producer-bug compensation
/// seen in some real-world S-122 and S-128 datasets).
/// </remarks>
public static class GmlCoordinateParser
{
    private static readonly char[] Separators = [' ', '\t', '\n', '\r', ','];

    /// <summary>
    /// Runs the shared axis-order check (<see cref="AxisOrderCheck"/>) over
    /// every <c>gml:pos</c> and <c>gml:posList</c> under
    /// <paramref name="root"/>, anchored on the dataset's
    /// <c>gml:boundedBy/gml:Envelope</c>, and returns the order to read the
    /// dataset in.
    /// </summary>
    /// <param name="root">The dataset root element.</param>
    /// <returns>
    /// <see cref="GmlAxisOrder.LongitudeFirst"/> when the check finds the
    /// dataset swapped, otherwise <see cref="GmlAxisOrder.LatitudeFirst"/>;
    /// and the assessment (or <c>null</c> when the dataset has no positions),
    /// for the reader to attach to its dataset model.
    /// </returns>
    public static (GmlAxisOrder Order, AxisOrderAssessment? Assessment) AssessAxisOrder(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var assessment = AxisOrderCheck.Assess(
            EnumerateEncodedPositions(root),
            ReadEnvelope(root),
            "gml:pos/gml:posList",
            extentIsOrdinal: true);
        var order = assessment?.Outcome == AxisOrderOutcome.Swapped
            ? GmlAxisOrder.LongitudeFirst
            : GmlAxisOrder.LatitudeFirst;
        return (order, assessment);
    }

    /// <summary>
    /// Reads the dataset's declared <c>gml:Envelope</c> (the root
    /// <c>gml:boundedBy</c>, else the first envelope in the document) latitude
    /// first, or returns <c>null</c> when there is none.
    /// </summary>
    public static BoundingBox? ReadEnvelope(XElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var gmlNs = root.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;
        var envelope = root.Element(gmlNs + "boundedBy")?.Element(gmlNs + "Envelope")
            ?? root.Descendants(gmlNs + "Envelope").FirstOrDefault();
        if (envelope is null)
            return null;

        var lower = envelope.Element(gmlNs + "lowerCorner")?.Value;
        var upper = envelope.Element(gmlNs + "upperCorner")?.Value;
        if (lower is null || upper is null)
            return null;

        var lo = ParsePos(lower);
        var hi = ParsePos(upper);
        if (lo is null || hi is null)
            return null;

        // AxisOrderCheck reads reversed west/east edges back.
        return new BoundingBox(
            Math.Min(lo.Value.Latitude, hi.Value.Latitude),
            lo.Value.Longitude,
            Math.Max(lo.Value.Latitude, hi.Value.Latitude),
            hi.Value.Longitude);
    }

    /// <summary>
    /// Yields every position under <paramref name="root"/> as encoded (first
    /// ordinate as latitude), including the split single-ordinate
    /// <c>gml:pos</c> runs <see cref="ParsePosSequence"/> tolerates.
    /// </summary>
    private static IEnumerable<GeoPosition> EnumerateEncodedPositions(XElement root)
    {
        var gmlNs = root.GetNamespaceOfPrefix("gml") ?? GmlNamespaces.Gml;
        double? pendingOrdinate = null;
        foreach (var element in root.Descendants())
        {
            if (element.Name.Namespace != gmlNs
                || element.Name.LocalName is not ("posList" or "pos"))
            {
                continue;
            }

            var parts = element.Value.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            if (element.Name.LocalName == "pos" && parts.Length == 1)
            {
                if (!double.TryParse(parts[0], CultureInfo.InvariantCulture, out var ordinate))
                {
                    pendingOrdinate = null;
                }
                else if (pendingOrdinate is { } first)
                {
                    yield return new GeoPosition(first, ordinate);
                    pendingOrdinate = null;
                }
                else
                {
                    pendingOrdinate = ordinate;
                }

                continue;
            }

            pendingOrdinate = null;
            for (int i = 0; i + 1 < parts.Length; i += 2)
            {
                if (double.TryParse(parts[i], CultureInfo.InvariantCulture, out var first) &&
                    double.TryParse(parts[i + 1], CultureInfo.InvariantCulture, out var second))
                {
                    yield return new GeoPosition(first, second);
                }
            }
        }
    }

    /// <summary>
    /// Parses a <c>gml:pos</c> value into a single coordinate pair.
    /// </summary>
    /// <returns>The parsed (latitude, longitude) pair, or <c>null</c> if parsing fails.</returns>
    public static GeoPosition? ParsePos(string posValue) => ParsePos(posValue, GmlAxisOrder.LatitudeFirst);

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
            return axisOrder == GmlAxisOrder.LongitudeFirst
                ? new GeoPosition(second, first)
                : new GeoPosition(first, second);
        }
        return null;
    }

    /// <summary>
    /// Parses a <c>gml:posList</c> value into a sequence of coordinate pairs,
    /// latitude first.
    /// </summary>
    public static IReadOnlyList<GeoPosition> ParsePosList(string posListValue) =>
        ParsePosList(posListValue, GmlAxisOrder.LatitudeFirst);

    /// <summary>
    /// Parses a <c>gml:posList</c> value into a sequence of coordinate pairs,
    /// reading it in the given <paramref name="axisOrder"/>.
    /// </summary>
    public static IReadOnlyList<GeoPosition> ParsePosList(string posListValue, GmlAxisOrder axisOrder)
    {
        var parts = posListValue.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        bool longitudeFirst = axisOrder == GmlAxisOrder.LongitudeFirst;
        var coords = new List<GeoPosition>(parts.Length / 2);

        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (double.TryParse(parts[i], CultureInfo.InvariantCulture, out var first) &&
                double.TryParse(parts[i + 1], CultureInfo.InvariantCulture, out var second))
            {
                coords.Add(longitudeFirst ? new GeoPosition(second, first) : new GeoPosition(first, second));
            }
        }

        return coords;
    }

    /// <summary>
    /// Extracts a point coordinate from a GML point property element by
    /// searching for <c>gml:pos</c> across common nesting patterns.
    /// </summary>
    public static GeoPosition? ParsePointElement(XElement element, XNamespace? s100Ns = null) =>
        ParsePointElement(element, s100Ns, GmlAxisOrder.LatitudeFirst);

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
        ParseCurveCoordinates(curveContainer, GmlAxisOrder.LatitudeFirst);

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
        ParseSurfaceCoordinates(surfaceContainer, GmlAxisOrder.LatitudeFirst);

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
