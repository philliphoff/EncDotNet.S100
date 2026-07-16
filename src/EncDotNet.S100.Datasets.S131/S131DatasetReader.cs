using System.Xml.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using S100Diag = EncDotNet.S100.Datasets.S131.Diagnostics;

namespace EncDotNet.S100.Datasets.S131;

/// <summary>
/// Reads an S-131 Marine Harbour Infrastructure GML encoded dataset
/// (S-100 Part 10b) into an <see cref="S131Dataset"/>.
/// </summary>
/// <remarks>
/// <para>
/// The S-131 1.0.0 application schema declares its target namespace as
/// <c>http://www.iho.int/S131/1.0</c> and imports geometry from the S-100
/// GML 5.0 profile (<c>http://www.iho.int/s100gml/5.0</c>). This reader
/// also accepts the older S-100 GML 1.0 profile namespace
/// (<c>http://www.iho.int/S100/profile/s100gml/1.0</c>) for compatibility.
/// </para>
/// <para>
/// Unlike other GML-encoded products that use individual <c>&lt;member&gt;</c>
/// and <c>&lt;imember&gt;</c> wrapper elements, S-131 wraps all features
/// <b>and</b> information types together in a single
/// <c>&lt;S131:members&gt;</c> container element. Feature vs. information
/// type discrimination uses namespace-driven recognition: any child element
/// in the application schema namespace is accepted, and the reader does not
/// maintain a hard-coded allow-list of feature type codes.
/// </para>
/// <para>
/// Coordinate ordering is <c>lat lon</c> for EPSG:4326 per S-100 Part 10b §6.2.
/// </para>
/// </remarks>
internal static class S131DatasetReader
{
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";

    // S-100 Part 10b GML namespaces — accept both 5.0 (current) and 1.0 (legacy).
    private static readonly XNamespace S100Ns5 = "http://www.iho.int/s100gml/5.0";
    private static readonly XNamespace S100Ns1 = "http://www.iho.int/S100/profile/s100gml/1.0";

    // S-131 Feature Catalogue Edition 1.0.0 — concrete information type codes.
    // Information types lack geometry and are referenced via xlink:href from features.
    // Abstract base types (InformationType, AbstractRxN) are included for completeness
    // but will not appear in real GML datasets.
    private static readonly HashSet<string> InformationTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        // Abstract base types
        "InformationType", "AbstractRxN",
        // Concrete information types (FC §B.1)
        "Applicability", "Authority", "AvailablePortServices",
        "ContactDetails", "Entrance", "NauticalInformation",
        "NonStandardWorkingDay", "Recommendations", "Regulations",
        "Restrictions", "ServiceHours", "SpatialQuality",
    };

    public static S131Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-131");

        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-131 GML document has no root element.");

        var datasetNs = root.Name.Namespace;

        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;
        string? productId = ReadProductIdentifier(root);

        var features = new List<S131Feature>();
        var informationTypes = new List<S131InformationType>();

        // S-131 uses a single <S131:members> container (not individual <member> elements).
        // Fall back to root-level children for compatibility with other envelope shapes.
        var membersContainer = FindElement(root, datasetNs, "members") ?? root;

        foreach (var element in membersContainer.Elements())
        {
            if (!IsApplicationSchema(element.Name, datasetNs)) continue;

            var localName = element.Name.LocalName;
            if (IsInformationType(localName))
            {
                informationTypes.Add(ParseInformationType(element));
            }
            else
            {
                features.Add(ParseFeature(element));
            }
        }

        // Also check for <imember> elements at the root level (forward compatibility).
        foreach (var imember in EnumerateChildren(root, "imember"))
        {
            foreach (var element in imember.Elements())
            {
                if (!IsApplicationSchema(element.Name, datasetNs)) continue;
                informationTypes.Add(ParseInformationType(element));
            }
        }

        return new S131Dataset
        {
            ProductIdentifier = productId ?? "S-131",
            DeclaredEdition = GmlDatasetIdentification.ReadDeclaredEdition(root),
            DatasetIdentifier = datasetId,
            Features = features,
            InformationTypes = informationTypes,
        };
    }

    private static string? ReadProductIdentifier(XElement root)
    {
        foreach (var ns in new[] { S100Ns5, S100Ns1 })
        {
            var dsInfo = root.Element(ns + "DatasetIdentificationInformation");
            var productId = dsInfo?.Element(ns + "productIdentifier")?.Value;
            if (!string.IsNullOrEmpty(productId)) return productId;
        }
        return null;
    }

    private static S131Feature ParseFeature(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value
            ?? element.Attribute("id")?.Value
            ?? "";
        var featureType = element.Name.LocalName;

        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element);
        var (simpleAttrs, complexAttrs, refs) = ParseAttributes(element);

        return new S131Feature
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
            References = refs,
        };
    }

    private static S131InformationType ParseInformationType(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value
            ?? element.Attribute("id")?.Value
            ?? "";
        var typeCode = element.Name.LocalName;

        var (simpleAttrs, complexAttrs, refs) = ParseAttributes(element);

        return new S131InformationType
        {
            Id = id,
            TypeCode = typeCode,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
            References = refs,
        };
    }

    // ── Geometry ───────────────────────────────────────────────────────

    private static (S100GeometryType, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>) ParseGeometry(XElement featureElement)
    {
        IReadOnlyList<GeoPosition> points = [];
        IReadOnlyList<IReadOnlyList<GeoPosition>> curves = [];
        IReadOnlyList<GeoPosition> exteriorRing = [];
        IReadOnlyList<IReadOnlyList<GeoPosition>> interiorRings = [];
        var geometryType = S100GeometryType.None;

        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");

        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        // pointProperty under either S-100 GML 5.0 or 1.0
        var pointProp = FindS100Element(geometryContainer, "pointProperty");
        if (pointProp is not null)
        {
            var coord = GmlCoordinateParser.ParsePointElement(pointProp);
            if (coord is not null)
            {
                geometryType = S100GeometryType.Point;
                points = [coord.Value];
            }
        }

        // curveProperty
        var curveProp = FindS100Element(geometryContainer, "curveProperty");
        if (curveProp is not null)
        {
            geometryType = S100GeometryType.Curve;
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp);
            if (coords.Count > 0)
            {
                curves = [coords];
            }
        }

        // surfaceProperty
        var surfaceProp = FindS100Element(geometryContainer, "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = S100GeometryType.Surface;
            var (ext, intRings) = GmlCoordinateParser.ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static XElement? FindS100Element(XElement container, string localName)
    {
        return container.Elements()
            .FirstOrDefault(e =>
                e.Name.LocalName == localName &&
                (e.Name.Namespace == S100Ns5 ||
                 e.Name.Namespace == S100Ns1 ||
                 e.Name.NamespaceName.Contains("s100gml/", StringComparison.OrdinalIgnoreCase)));
    }

    // ── Attributes ─────────────────────────────────────────────────────

    private static (IReadOnlyDictionary<string, string>, IReadOnlyList<S131ComplexAttribute>, IReadOnlyList<S131Reference>) ParseAttributes(XElement element)
    {
        var simple = new Dictionary<string, string>();
        var complex = new List<S131ComplexAttribute>();
        var refs = new List<S131Reference>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip geometry, GML id, and S-100 infrastructure elements.
            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNamespaces.Gml ||
                child.Name.Namespace == S100Ns5 ||
                child.Name.Namespace == S100Ns1)
                continue;

            // xlink:href reference (feature-to-feature or feature-to-info association)
            var href = child.Attribute(XlinkNs + "href")?.Value;
            if (!string.IsNullOrEmpty(href) && !child.HasElements && string.IsNullOrEmpty(child.Value))
            {
                refs.Add(new S131Reference
                {
                    Role = localName,
                    TargetRef = href.TrimStart('#'),
                });
                continue;
            }

            if (child.HasElements)
            {
                var subAttrs = new Dictionary<string, string>();
                foreach (var sub in child.Elements())
                {
                    if (!sub.HasElements)
                        subAttrs[sub.Name.LocalName] = sub.Value;
                }
                if (subAttrs.Count > 0)
                {
                    complex.Add(new S131ComplexAttribute
                    {
                        Code = localName,
                        SubAttributes = subAttrs,
                    });
                }
            }
            else if (!string.IsNullOrEmpty(child.Value))
            {
                simple[localName] = child.Value;
            }
        }

        return (simple, complex, refs);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// True when the element name lives in the S-131 application schema
    /// namespace rather than GML or S-100 base infrastructure.
    /// </summary>
    private static bool IsApplicationSchema(XName name, XNamespace datasetNs)
    {
        _ = datasetNs;
        if (name.Namespace == GmlNamespaces.Gml) return false;
        if (name.Namespace.NamespaceName.Contains("s100gml/", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static bool IsInformationType(string localName)
    {
        return InformationTypeCodes.Contains(localName);
    }

    private static XElement? FindElement(XElement parent, XNamespace ns, string localName)
    {
        var el = parent.Element(ns + localName);
        if (el is not null) return el;

        // Try local-name-only match
        return parent.Elements()
            .FirstOrDefault(e => string.Equals(e.Name.LocalName, localName, StringComparison.Ordinal));
    }

    private static IEnumerable<XElement> EnumerateChildren(XElement root, string localName)
    {
        foreach (var child in root.Elements())
        {
            if (string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal))
                yield return child;
        }
    }
}
