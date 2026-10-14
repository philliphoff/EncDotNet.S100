using System.Xml.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using S100Diag = EncDotNet.S100.Datasets.S122.Diagnostics;

namespace EncDotNet.S100.Datasets.S122;

/// <summary>
/// Reads an S-122 GML encoded dataset (S-100 Part 10b) into an <see cref="S122Dataset"/>.
/// </summary>
internal static class S122DatasetReader
{

    // The s100gml namespace varies between releases of S-100 Part 10b
    // (and between official S-122 sample releases). Accept any of the
    // commonly observed forms and fall back to scanning the root for
    // an explicit declaration.
    private static readonly XNamespace[] CandidateS100Namespaces =
    [
        "http://www.iho.int/s100gml/1.0",
        "http://www.iho.int/S100/profile/s100gml/1.0",
        "http://www.iho.int/s100gml/5.0",
    ];

    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    // S-122 feature type codes (per FC 2.0.0, S-122 § Feature Catalogue).
    private static readonly HashSet<string> FeatureTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DataCoverage",
        "InformationArea",
        "MarineProtectedArea",
        "RestrictedArea",
        "VesselTrafficServiceArea",
        "QualityOfNonBathymetricData",
        "TextPlacement",
    };

    // S-122 information type codes (per FC 2.0.0).
    private static readonly HashSet<string> InformationTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AbstractRxN",
        "Applicability",
        "Authority",
        "ContactDetails",
        "NauticalInformation",
        "NonStandardWorkingDay",
        "Recommendations",
        "Regulations",
        "Restrictions",
        "ServiceHours",
        "SpatialQuality",
    };

    public static S122Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-122");
        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-122 GML document has no root element.");

        // Decide the axis order once for the whole dataset (issue #650).
        // Some real-world S-122 datasets (e.g. the UK trial dataset
        // GBNPI12200002045) emit positions longitude first even though
        // S-100 Part 10b mandates latitude first for EPSG:4326; the shared
        // check catches that against the gml:Envelope.
        var (axisOrder, axisAssessment) = GmlCoordinateParser.AssessAxisOrder(root);

        // Detect the dataset namespace from the root element.
        var datasetNs = root.Name.Namespace;
        // Detect the s100gml namespace actually declared on this document.
        var s100Ns = DetectS100Namespace(root);

        // Parse dataset identification.
        string? productId = null;
        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;

        var dsInfo = root.Element(s100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(s100Ns + "productIdentifier")?.Value;
        }

        // Parse features and information types.
        // S-122 GML can use either repeated <member>/<imember> wrappers (each
        // containing one feature) or a single <members>/<imembers> container
        // holding all features as direct children. Walk descendants of the
        // root and collect anything whose local name matches a known type.
        var features = new List<S122Feature>();
        var informationTypes = new List<S122InformationType>();

        foreach (var element in root.Descendants())
        {
            var name = element.Name;
            if (IsFeatureType(name, datasetNs))
            {
                var feature = ParseFeature(element, s100Ns, axisOrder);
                if (feature is not null)
                    features.Add(feature);
            }
            else if (IsInformationType(name, datasetNs))
            {
                var info = ParseInformationType(element, s100Ns);
                if (info is not null)
                    informationTypes.Add(info);
            }
        }

        return new S122Dataset
        {
            ProductIdentifier = productId ?? "S-122",
            DeclaredEdition = GmlDatasetIdentification.ReadDeclaredEdition(root),
            DatasetIdentifier = datasetId,
            Features = features,
            InformationTypes = informationTypes,
            AxisOrder = axisAssessment,
        };
    }

    private static XNamespace DetectS100Namespace(XElement root)
    {
        // Look for any in-scope namespace declaration whose URI matches a
        // known s100gml release.
        foreach (var attr in root.Attributes())
        {
            if (!attr.IsNamespaceDeclaration) continue;
            foreach (var candidate in CandidateS100Namespaces)
            {
                if (string.Equals(attr.Value, candidate.NamespaceName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            // Heuristic: any "s100gml" URI declared on the document.
            if (attr.Value.Contains("s100gml", StringComparison.OrdinalIgnoreCase))
                return attr.Value;
        }

        // Fallback: search descendants for a known S-100 element.
        foreach (var candidate in CandidateS100Namespaces)
        {
            if (root.Descendants(candidate + "DatasetIdentificationInformation").Any())
                return candidate;
        }

        return CandidateS100Namespaces[0];
    }

    private static S122Feature? ParseFeature(XElement element, XNamespace s100Ns, GmlAxisOrder axisOrder)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var featureType = element.Name.LocalName;

        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element, s100Ns, axisOrder);
        var (simpleAttrs, complexAttrs, references) = ParseAttributes(element, s100Ns);

        return new S122Feature
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
            References = references,
        };
    }

    private static S122InformationType? ParseInformationType(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var typeCode = element.Name.LocalName;

        var (simpleAttrs, complexAttrs, references) = ParseAttributes(element, s100Ns);

        return new S122InformationType
        {
            Id = id,
            TypeCode = typeCode,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
            References = references,
        };
    }

    private static (S100GeometryType, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>) ParseGeometry(XElement featureElement, XNamespace s100Ns, GmlAxisOrder axisOrder)
    {
        IReadOnlyList<GeoPosition> points = [];
        IReadOnlyList<IReadOnlyList<GeoPosition>> curves = [];
        IReadOnlyList<GeoPosition> exteriorRing = [];
        IReadOnlyList<IReadOnlyList<GeoPosition>> interiorRings = [];
        var geometryType = S100GeometryType.None;

        // Look for geometry in the "geometry" child element or directly under the feature.
        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");

        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        // S-100 Part 10b point property.
        var pointProp = geometryContainer.Element(s100Ns + "pointProperty")
            ?? geometryContainer.Element(s100Ns + "Point");
        if (pointProp is not null)
        {
            var pointCoords = GmlCoordinateParser.ParsePointElement(pointProp, s100Ns, axisOrder);
            if (pointCoords is not null)
            {
                geometryType = S100GeometryType.Point;
                points = [pointCoords.Value];
            }
            else
            {
                var gmlPoint = pointProp.Descendants(GmlNamespaces.Gml + "Point").FirstOrDefault()
                    ?? pointProp.Descendants(GmlNamespaces.Gml + "pos").FirstOrDefault()?.Parent;
                if (gmlPoint is not null)
                {
                    var coord = GmlCoordinateParser.ParsePointElement(gmlPoint, null, axisOrder);
                    if (coord is not null)
                    {
                        geometryType = S100GeometryType.Point;
                        points = [coord.Value];
                    }
                }
            }
        }

        // S-100 Part 10b curve property.
        var curveProp = geometryContainer.Element(s100Ns + "curveProperty");
        if (curveProp is not null)
        {
            geometryType = S100GeometryType.Curve;
            var curveBuilder = new List<IReadOnlyList<GeoPosition>>();
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp, axisOrder);
            if (coords.Count > 0)
                curveBuilder.Add(coords);
            curves = curveBuilder;
        }

        // S-100 Part 10b surface property.
        var surfaceProp = geometryContainer.Element(s100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = S100GeometryType.Surface;
            var (ext, intRings) = GmlCoordinateParser.ParseSurfaceCoordinates(surfaceProp, axisOrder);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static (IReadOnlyDictionary<string, string>, IReadOnlyList<S122ComplexAttribute>, IReadOnlyList<GmlReference>) ParseAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = new Dictionary<string, string>();
        var complex = new List<S122ComplexAttribute>();
        var refs = new List<GmlReference>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip geometry, GML id, and S-100 infrastructure elements.
            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNamespaces.Gml ||
                child.Name.Namespace == s100Ns)
                continue;

            // xlink:href cross-reference — surface as a typed reference rather
            // than an attribute. The element's local name is the association
            // role (e.g. "theAuthority", "theContactDetails"; S-122 FC 2.0.0 §Roles).
            var href = child.Attribute(XLinkNs + "href")?.Value;
            if (href is not null)
            {
                refs.Add(new GmlReference
                {
                    Role = localName,
                    Href = href,
                    ArcRole = child.Attribute(XLinkNs + "arcrole")?.Value,
                });
                continue;
            }

            if (child.HasElements)
            {
                var subAttrs = new Dictionary<string, string>();
                foreach (var sub in child.Elements())
                {
                    if (!sub.HasElements && sub.Attribute(XLinkNs + "href") is null)
                    {
                        subAttrs[sub.Name.LocalName] = sub.Value;
                    }
                }
                if (subAttrs.Count > 0)
                {
                    complex.Add(new S122ComplexAttribute
                    {
                        Code = localName,
                        SubAttributes = subAttrs,
                    });
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(child.Value))
                    simple[localName] = child.Value;
            }
        }

        return (simple, complex, refs);
    }

    private static bool IsFeatureType(XName name, XNamespace datasetNs)
    {
        return (name.Namespace == datasetNs || name.Namespace == XNamespace.None) &&
               FeatureTypeCodes.Contains(name.LocalName);
    }

    private static bool IsInformationType(XName name, XNamespace datasetNs)
    {
        return (name.Namespace == datasetNs || name.Namespace == XNamespace.None) &&
               InformationTypeCodes.Contains(name.LocalName);
    }
}
