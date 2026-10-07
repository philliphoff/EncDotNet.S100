using System.Xml.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using S100Diag = EncDotNet.S100.Datasets.S124.Diagnostics;

namespace EncDotNet.S100.Datasets.S124;

/// <summary>
/// Reads an S-124 GML encoded dataset (S-100 Part 10b) into an <see cref="S124Dataset"/>.
/// </summary>
internal static class S124DatasetReader
{
    // S-100 Part 10b GML namespaces (S-124 uses the legacy 1.0 profile in real-world data,
    // but accept the 5.0 namespace as well for forward compatibility).
    private static readonly XNamespace S100Ns = "http://www.iho.int/S100/profile/s100gml/1.0";
    private static readonly XNamespace S100Ns50 = "http://www.iho.int/s100gml/5.0";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    // Known S-124 feature type codes
    private static readonly HashSet<string> FeatureTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "NavwarnPart", "NavwarnAreaAffected", "TextPlacement"
    };

    // Known S-124 information type codes
    private static readonly HashSet<string> InformationTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "NavwarnPreamble", "References", "SpatialQuality"
    };

    public static S124Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-124");
        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-124 GML document has no root element.");

        // Detect the dataset namespace from the root element
        var datasetNs = root.Name.Namespace;

        // Parse dataset identification
        string? productId = null;
        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;

        var dsInfo = S100Element(root, "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = S100Element(dsInfo, "productIdentifier")?.Value;
        }

        // Features and information types are wrapped one per <member> / <imember>
        // element, or (S-124 Ed 2.0, S-100 GML 5.0) gathered in a single
        // <members> (and <imembers>) container.
        var containers = root.Elements()
            .Where(e => e.Name.LocalName is "members" or "imembers")
            .SelectMany(c => c.Elements())
            .ToArray();

        var features = new List<S124Feature>();
        foreach (var featureElement in root.Elements(MemberName(root))
            .Select(m => m.Elements().FirstOrDefault(e => IsFeatureType(e.Name, datasetNs)))
            .Concat(containers.Where(e => IsFeatureType(e.Name, datasetNs)))
            .OfType<XElement>())
        {
            var feature = ParseFeature(featureElement);
            if (feature is not null)
                features.Add(feature);
        }

        var informationTypes = new List<S124InformationType>();
        foreach (var infoElement in root.Elements(IMemberName(root))
            .Select(m => m.Elements().FirstOrDefault(e => IsInformationType(e.Name, datasetNs)))
            .Concat(containers.Where(e => IsInformationType(e.Name, datasetNs)))
            .OfType<XElement>())
        {
            var info = ParseInformationType(infoElement);
            if (info is not null)
                informationTypes.Add(info);
        }
        return new S124Dataset
        {
            ProductIdentifier = productId ?? "S-124",
            DeclaredEdition = GmlDatasetIdentification.ReadDeclaredEdition(root),
            DatasetIdentifier = datasetId,
            Features = features,
            InformationTypes = informationTypes,
        };
    }

    private static S124Feature? ParseFeature(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var featureType = element.Name.LocalName;

        // Parse geometry
        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element);

        // Parse attributes
        var (simpleAttrs, complexAttrs, references) = ParseAttributes(element);

        return new S124Feature
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

    private static S124InformationType? ParseInformationType(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var typeCode = element.Name.LocalName;

        var (simpleAttrs, complexAttrs, references) = ParseAttributes(element);

        return new S124InformationType
        {
            Id = id,
            TypeCode = typeCode,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
            References = references,
        };
    }

    private static (S100GeometryType, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>) ParseGeometry(XElement featureElement)
    {
        // Look for geometry in the "geometry" child elements. A feature may repeat
        // it (S-124 Ed 2.0 producers write one point per element): points from
        // every point geometry are gathered; curves and surfaces use the first.
        var containers = featureElement.Elements(featureElement.Name.Namespace + "geometry")
            .Concat(featureElement.Elements("geometry"))
            .ToArray();
        if (containers.Length == 0)
            return (S100GeometryType.None, [], [], [], []);

        var first = ParseGeometryContainer(containers[0]);
        if (first.Item1 != S100GeometryType.Point || containers.Length == 1)
            return first;

        var points = new List<GeoPosition>(first.Item2);
        foreach (var container in containers.Skip(1))
        {
            var more = ParseGeometryContainer(container);
            if (more.Item1 == S100GeometryType.Point)
                points.AddRange(more.Item2);
        }

        return (S100GeometryType.Point, points, first.Item3, first.Item4, first.Item5);
    }

    private static (S100GeometryType, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>) ParseGeometryContainer(XElement geometryContainer)
    {
        IReadOnlyList<GeoPosition> points = [];
        IReadOnlyList<IReadOnlyList<GeoPosition>> curves = [];
        IReadOnlyList<GeoPosition> exteriorRing = [];
        IReadOnlyList<IReadOnlyList<GeoPosition>> interiorRings = [];
        var geometryType = S100GeometryType.None;

        // S-100 Part 10b point property
        var pointProp = S100Element(geometryContainer, "pointProperty")
            ?? S100Element(geometryContainer, "Point");
        if (pointProp is not null)
        {
            var pointCoords = GmlCoordinateParser.ParsePointElement(pointProp, pointProp.Name.Namespace);
            if (pointCoords is not null)
            {
                geometryType = S100GeometryType.Point;
                points = [pointCoords.Value];
            }
            else
            {
                // Try descendant gml:Point
                var gmlPoint = pointProp.Descendants(GmlNamespaces.Gml + "Point").FirstOrDefault()
                    ?? pointProp.Descendants(GmlNamespaces.Gml + "pos").FirstOrDefault()?.Parent;
                if (gmlPoint is not null)
                {
                    var coord = GmlCoordinateParser.ParsePointElement(gmlPoint);
                    if (coord is not null)
                    {
                        geometryType = S100GeometryType.Point;
                        points = [coord.Value];
                    }
                }
            }
        }

        // S-100 Part 10b curve property
        var curveProp = S100Element(geometryContainer, "curveProperty");
        if (curveProp is not null)
        {
            geometryType = S100GeometryType.Curve;
            var curveBuilder = new List<IReadOnlyList<GeoPosition>>();
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp);
            if (coords.Count > 0)
                curveBuilder.Add(coords);
            curves = curveBuilder;
        }

        // S-100 Part 10b surface property
        var surfaceProp = S100Element(geometryContainer, "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = S100GeometryType.Surface;
            var (ext, intRings) = GmlCoordinateParser.ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }
    private static (IReadOnlyDictionary<string, string>, IReadOnlyList<S124ComplexAttribute>, IReadOnlyList<GmlReference>) ParseAttributes(XElement element)
    {
        var simple = new Dictionary<string, string>();
        var complex = new List<S124ComplexAttribute>();
        var refs = new List<GmlReference>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip geometry, GML id, and S-100 infrastructure elements
            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNamespaces.Gml ||
                child.Name.Namespace == S100Ns ||
                child.Name.Namespace == S100Ns50)
                continue;

            // xlink:href reference — captured as a typed reference rather than an attribute.
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
                // Complex attribute — collect sub-attributes
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
                    complex.Add(new S124ComplexAttribute
                    {
                        Code = localName,
                        SubAttributes = subAttrs,
                    });
                }
            }
            else
            {
                // Simple attribute
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

    /// <summary>The S-100 GML child <paramref name="localName"/>, in the 1.0 or 5.0 profile namespace.</summary>
    private static XElement? S100Element(XElement parent, string localName) =>
        parent.Element(S100Ns + localName) ?? parent.Element(S100Ns50 + localName);

    /// <summary>
    /// Finds the "member" element name used in this document.
    /// S-100 Part 10b uses "member" in the dataset namespace.
    /// </summary>
    private static XName MemberName(XElement root)
    {
        var ns = root.Name.Namespace;
        // Try dataset-namespaced first, then unnamespaced
        if (root.Element(ns + "member") is not null)
            return ns + "member";
        if (root.Element("member") is not null)
            return (XName)"member";
        // GML-style
        return ns + "member";
    }

    /// <summary>
    /// Finds the "imember" element name used in this document.
    /// </summary>
    private static XName IMemberName(XElement root)
    {
        var ns = root.Name.Namespace;
        if (root.Element(ns + "imember") is not null)
            return ns + "imember";
        if (root.Element("imember") is not null)
            return (XName)"imember";
        return ns + "imember";
    }
}
