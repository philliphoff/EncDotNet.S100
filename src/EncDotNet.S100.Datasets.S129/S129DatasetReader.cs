using System.Collections.Immutable;
using System.Xml.Linq;
using S100Diag = EncDotNet.S100.Datasets.S129.Diagnostics;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S129;

/// <summary>
/// Reads an S-129 GML encoded dataset (S-100 Part 10b) into an <see cref="S129Dataset"/>.
/// </summary>
internal static class S129DatasetReader
{
    // S-100 Part 10b GML namespaces
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
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-129");
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
        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;

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
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
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

    private static (GmlGeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement, XNamespace s100Ns)
    {
        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var geometryType = GmlGeometryType.None;

        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");

        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        // S-100 Part 10b point property
        var pointProp = geometryContainer.Element(s100Ns + "pointProperty")
            ?? geometryContainer.Element(s100Ns + "Point");
        if (pointProp is not null)
        {
            var pointCoords = GmlCoordinateParser.ParsePointElement(pointProp, s100Ns);
            if (pointCoords is not null)
            {
                geometryType = GmlGeometryType.Point;
                points = [pointCoords.Value];
            }
            else
            {
                var gmlPoint = pointProp.Descendants(GmlNamespaces.Gml + "Point").FirstOrDefault()
                    ?? pointProp.Descendants(GmlNamespaces.Gml + "pos").FirstOrDefault()?.Parent;
                if (gmlPoint is not null)
                {
                    var coord = GmlCoordinateParser.ParsePointElement(gmlPoint);
                    if (coord is not null)
                    {
                        geometryType = GmlGeometryType.Point;
                        points = [coord.Value];
                    }
                }
            }
        }

        // S-100 Part 10b curve property
        var curveProp = geometryContainer.Element(s100Ns + "curveProperty");
        if (curveProp is not null)
        {
            geometryType = GmlGeometryType.Curve;
            var curveBuilder = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>();
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
                curveBuilder.Add(coords);
            curves = curveBuilder.ToImmutable();
        }

        // S-100 Part 10b surface property
        var surfaceProp = geometryContainer.Element(s100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = GmlGeometryType.Surface;
            var (ext, intRings) = GmlCoordinateParser.ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }    private static (ImmutableDictionary<string, string>, ImmutableArray<S129ComplexAttribute>) ParseAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S129ComplexAttribute>();
        var ns = element.Name.Namespace;

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNamespaces.Gml ||
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
