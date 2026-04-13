using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Datasets.S129;

/// <summary>
/// Reads an S-129 GML encoded dataset (S-100 Part 10b) into an <see cref="S129Dataset"/>.
/// </summary>
internal static class S129DatasetReader
{
    // S-100 Part 10b GML namespaces
    private static readonly XNamespace GmlNs = "http://www.opengis.net/gml/3.2";
    private static readonly XNamespace S100Ns1 = "http://www.iho.int/S100/profile/s100gml/1.0";
    private static readonly XNamespace S100Ns5 = "http://www.iho.int/s100gml/5.0";

    // Known S-129 feature type codes
    private static readonly HashSet<string> FeatureTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "UnderKeelClearancePlan",
        "UnderKeelClearancePlanArea",
        "UnderKeelClearanceNonNavigableArea",
        "UnderKeelClearanceAlmostNonNavigableArea",
        "UnderKeelClearanceControlPoint",
    };

    public static S129Dataset Read(Stream stream)
    {
        // Some GML files have leading whitespace before the XML declaration;
        // read as text and trim to handle this gracefully.
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd().TrimStart();
        var doc = XDocument.Parse(xml);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-129 GML document has no root element.");

        // Detect the dataset namespace from the root element
        var datasetNs = root.Name.Namespace;

        // Detect which S-100 namespace edition is used
        var s100Ns = DetectS100Namespace(root);

        // Parse dataset identification
        string? productId = null;
        string? datasetId = root.Attribute(GmlNs + "id")?.Value;

        var dsInfo = root.Element(s100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(s100Ns + "productIdentifier")?.Value;
        }

        // Parse features from <member> or <members> elements
        var features = ImmutableArray.CreateBuilder<S129Feature>();
        foreach (var featureElement in EnumerateFeatureElements(root, datasetNs))
        {
            if (IsFeatureType(featureElement.Name, datasetNs))
            {
                var feature = ParseFeature(featureElement, s100Ns);
                if (feature is not null)
                    features.Add(feature);
            }
        }

        return new S129Dataset
        {
            ProductIdentifier = productId ?? "S-129",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
        };
    }

    private static S129Feature? ParseFeature(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var featureType = element.Name.LocalName;

        // Parse geometry
        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element, s100Ns);

        // Parse attributes
        var (simpleAttrs, complexAttrs) = ParseAttributes(element, s100Ns);

        return new S129Feature
        {
            Id = id,
            FeatureType = featureType,
            GeometryType = geometryType,
            Points = points,
            Curves = curves,
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
        };
    }

    private static (S129GeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement, XNamespace s100Ns)
    {
        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var geometryType = S129GeometryType.None;

        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");

        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        // S-100 Part 10b point property
        var pointProp = geometryContainer.Element(s100Ns + "pointProperty")
            ?? geometryContainer.Element(s100Ns + "Point");
        if (pointProp is not null)
        {
            var pointCoords = ParsePointElement(pointProp, s100Ns);
            if (pointCoords is not null)
            {
                geometryType = S129GeometryType.Point;
                points = [pointCoords.Value];
            }
            else
            {
                var gmlPoint = pointProp.Descendants(GmlNs + "Point").FirstOrDefault()
                    ?? pointProp.Descendants(GmlNs + "pos").FirstOrDefault()?.Parent;
                if (gmlPoint is not null)
                {
                    var coord = ParseGmlPoint(gmlPoint);
                    if (coord is not null)
                    {
                        geometryType = S129GeometryType.Point;
                        points = [coord.Value];
                    }
                }
            }
        }

        // S-100 Part 10b curve property
        var curveProp = geometryContainer.Element(s100Ns + "curveProperty");
        if (curveProp is not null)
        {
            geometryType = S129GeometryType.Curve;
            var curveBuilder = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>();
            var coords = ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
                curveBuilder.Add(coords);
            curves = curveBuilder.ToImmutable();
        }

        // S-100 Part 10b surface property
        var surfaceProp = geometryContainer.Element(s100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = S129GeometryType.Surface;
            var (ext, intRings) = ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static (double Latitude, double Longitude)? ParsePointElement(XElement element, XNamespace s100Ns)
    {
        var pos = element.Element(GmlNs + "Point")?.Element(GmlNs + "pos")
            ?? element.Element(GmlNs + "pos");

        if (pos is not null)
            return ParsePos(pos.Value);

        var s100Point = element.Element(s100Ns + "Point");
        if (s100Point is not null)
        {
            pos = s100Point.Element(GmlNs + "pos");
            if (pos is not null)
                return ParsePos(pos.Value);
        }

        return null;
    }

    private static (double Latitude, double Longitude)? ParseGmlPoint(XElement pointElement)
    {
        var pos = pointElement.Element(GmlNs + "pos");
        if (pos is not null)
            return ParsePos(pos.Value);
        return null;
    }

    private static ImmutableArray<(double Latitude, double Longitude)> ParseCurveCoordinates(XElement curveContainer)
    {
        var coords = ImmutableArray.CreateBuilder<(double, double)>();

        foreach (var posList in curveContainer.Descendants(GmlNs + "posList"))
        {
            coords.AddRange(ParsePosList(posList.Value));
        }

        if (coords.Count == 0)
        {
            foreach (var pos in curveContainer.Descendants(GmlNs + "pos"))
            {
                var coord = ParsePos(pos.Value);
                if (coord is not null)
                    coords.Add(coord.Value);
            }
        }

        return coords.ToImmutable();
    }

    private static (ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseSurfaceCoordinates(XElement surfaceContainer)
    {
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>();

        var exterior = surfaceContainer.Descendants(GmlNs + "exterior").FirstOrDefault();
        if (exterior is not null)
        {
            var posList = exterior.Descendants(GmlNs + "posList").FirstOrDefault();
            if (posList is not null)
                exteriorRing = ParsePosList(posList.Value);
            else
            {
                var builder = ImmutableArray.CreateBuilder<(double, double)>();
                foreach (var pos in exterior.Descendants(GmlNs + "pos"))
                {
                    var coord = ParsePos(pos.Value);
                    if (coord is not null)
                        builder.Add(coord.Value);
                }
                exteriorRing = builder.ToImmutable();
            }
        }

        foreach (var interior in surfaceContainer.Descendants(GmlNs + "interior"))
        {
            var posList = interior.Descendants(GmlNs + "posList").FirstOrDefault();
            if (posList is not null)
            {
                interiorRings.Add(ParsePosList(posList.Value));
            }
            else
            {
                var builder = ImmutableArray.CreateBuilder<(double, double)>();
                foreach (var pos in interior.Descendants(GmlNs + "pos"))
                {
                    var coord = ParsePos(pos.Value);
                    if (coord is not null)
                        builder.Add(coord.Value);
                }
                if (builder.Count > 0)
                    interiorRings.Add(builder.ToImmutable());
            }
        }

        return (exteriorRing, interiorRings.ToImmutable());
    }

    private static (double Latitude, double Longitude)? ParsePos(string posValue)
    {
        var parts = posValue.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 &&
            double.TryParse(parts[0], CultureInfo.InvariantCulture, out var lat) &&
            double.TryParse(parts[1], CultureInfo.InvariantCulture, out var lon))
        {
            return (lat, lon);
        }
        return null;
    }

    private static ImmutableArray<(double Latitude, double Longitude)> ParsePosList(string posListValue)
    {
        var parts = posListValue.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var coords = ImmutableArray.CreateBuilder<(double, double)>();

        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (double.TryParse(parts[i], CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(parts[i + 1], CultureInfo.InvariantCulture, out var lon))
            {
                coords.Add((lat, lon));
            }
        }

        return coords.ToImmutable();
    }

    private static (ImmutableDictionary<string, string>, ImmutableArray<S129ComplexAttribute>) ParseAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S129ComplexAttribute>();
        var ns = element.Name.Namespace;

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNs ||
                child.Name.Namespace == s100Ns)
                continue;

            if (child.HasElements)
            {
                var subAttrs = ImmutableDictionary.CreateBuilder<string, string>();
                foreach (var sub in child.Elements())
                {
                    if (!sub.HasElements)
                    {
                        subAttrs[sub.Name.LocalName] = sub.Value;
                    }
                }
                if (subAttrs.Count > 0)
                {
                    complex.Add(new S129ComplexAttribute
                    {
                        Code = localName,
                        SubAttributes = subAttrs.ToImmutable(),
                    });
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(child.Value))
                    simple[localName] = child.Value;
            }
        }

        return (simple.ToImmutable(), complex.ToImmutable());
    }

    private static bool IsFeatureType(XName name, XNamespace datasetNs)
    {
        return (name.Namespace == datasetNs || name.Namespace == XNamespace.None) &&
               FeatureTypeCodes.Contains(name.LocalName);
    }

    /// <summary>
    /// Detects the S-100 GML namespace used in the document by checking
    /// for known namespace URIs among declared namespaces.
    /// </summary>
    private static XNamespace DetectS100Namespace(XElement root)
    {
        foreach (var attr in root.Attributes())
        {
            if (attr.IsNamespaceDeclaration)
            {
                if (attr.Value == S100Ns5.NamespaceName)
                    return S100Ns5;
                if (attr.Value == S100Ns1.NamespaceName)
                    return S100Ns1;
            }
        }

        // Default to the newer namespace
        return S100Ns5;
    }

    /// <summary>
    /// Enumerates feature elements from both &lt;member&gt; (individual) and
    /// &lt;members&gt; (container) patterns used in S-100 Part 10b GML.
    /// </summary>
    private static IEnumerable<XElement> EnumerateFeatureElements(XElement root, XNamespace datasetNs)
    {
        // Pattern 1: <members> container with features as direct children
        var membersContainer = root.Element(datasetNs + "members")
            ?? root.Element("members");
        if (membersContainer is not null)
        {
            foreach (var child in membersContainer.Elements())
                yield return child;
        }

        // Pattern 2: individual <member> elements
        foreach (var member in root.Elements(datasetNs + "member"))
        {
            foreach (var child in member.Elements())
                yield return child;
        }

        // Pattern 3: bare <member> (no namespace)
        foreach (var member in root.Elements("member"))
        {
            foreach (var child in member.Elements())
                yield return child;
        }
    }
}
