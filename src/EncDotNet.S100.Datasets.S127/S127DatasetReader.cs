using System.Collections.Immutable;
using System.Xml.Linq;
using S100Diag = EncDotNet.S100.Datasets.S127.Diagnostics;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S127;

/// <summary>
/// Reads an S-127 GML encoded dataset (S-100 Part 10b) into an <see cref="S127Dataset"/>.
/// </summary>
/// <remarks>
/// S-127 Edition 2.0.0 declares its application schema in the namespace
/// <c>http://www.iho.int/S127/2.0</c> and uses the S-100 GML 5.0 base
/// (<c>http://www.iho.int/s100gml/5.0</c>). This reader also accepts the
/// older S-100 GML 1.0 namespace (<c>http://www.iho.int/S100/profile/s100gml/1.0</c>)
/// for parity with S-124-style encoders that may be retrofitted to S-127
/// pre-publication. Coordinate ordering is <c>lat lon</c> for EPSG:4326
/// per S-100 Part 10b §6.2.
/// </remarks>
internal static class S127DatasetReader
{

    // S-100 Part 10b GML namespaces — accept both 5.0 (current) and 1.0 (legacy).
    private static readonly XNamespace S100Ns5 = "http://www.iho.int/s100gml/5.0";
    private static readonly XNamespace S100Ns1 = "http://www.iho.int/S100/profile/s100gml/1.0";
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";

    public static S127Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-127");
        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-127 GML document has no root element.");

        var datasetNs = root.Name.Namespace;

        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;
        string? productId = ReadProductIdentifier(root);

        var features = ImmutableArray.CreateBuilder<S127Feature>();
        foreach (var member in EnumerateChildren(root, "member"))
        {
            var featureElement = member.Elements()
                .FirstOrDefault(e => IsApplicationSchema(e.Name, datasetNs));
            if (featureElement is null) continue;

            var feature = ParseFeature(featureElement);
            if (feature is not null) features.Add(feature);
        }

        var informationTypes = ImmutableArray.CreateBuilder<S127InformationType>();
        foreach (var imember in EnumerateChildren(root, "imember"))
        {
            var infoElement = imember.Elements()
                .FirstOrDefault(e => IsApplicationSchema(e.Name, datasetNs));
            if (infoElement is null) continue;

            var info = ParseInformationType(infoElement);
            if (info is not null) informationTypes.Add(info);
        }

        return new S127Dataset
        {
            ProductIdentifier = productId ?? "S-127",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
            InformationTypes = informationTypes.ToImmutable(),
        };
    }

    private static string? ReadProductIdentifier(XElement root)
    {
        // Look for <S100:DatasetIdentificationInformation>/<S100:productIdentifier>
        // under either S-100 GML 5.0 or 1.0 namespace.
        foreach (var ns in new[] { S100Ns5, S100Ns1 })
        {
            var dsInfo = root.Element(ns + "DatasetIdentificationInformation");
            var productId = dsInfo?.Element(ns + "productIdentifier")?.Value;
            if (!string.IsNullOrEmpty(productId)) return productId;
        }
        return null;
    }

    private static S127Feature ParseFeature(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element);
        var (simpleAttrs, complexAttrs, featureRefs) = ParseAttributes(element);

        return new S127Feature
        {
            Id = id,
            FeatureType = element.Name.LocalName,
            GeometryType = geometryType,
            Points = points,
            Curves = curves,
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
            FeatureReferences = featureRefs,
        };
    }

    private static S127InformationType ParseInformationType(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var (simpleAttrs, complexAttrs, _) = ParseAttributes(element);

        return new S127InformationType
        {
            Id = id,
            TypeCode = element.Name.LocalName,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
        };
    }

    private static (GmlGeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement)
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

        // pointProperty under either S-100 GML 5.0 or 1.0
        var pointProp = FindS100Element(geometryContainer, "pointProperty");
        if (pointProp is not null)
        {
            var coord = GmlCoordinateParser.ParsePointElement(pointProp);
            if (coord is not null)
            {
                geometryType = GmlGeometryType.Point;
                points = [coord.Value];
            }
        }

        // curveProperty
        var curveProp = FindS100Element(geometryContainer, "curveProperty");
        if (curveProp is not null)
        {
            geometryType = GmlGeometryType.Curve;
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
            {
                curves = [coords];
            }
        }

        // surfaceProperty
        var surfaceProp = FindS100Element(geometryContainer, "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = GmlGeometryType.Surface;
            var (ext, intRings) = GmlCoordinateParser.ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static XElement? FindS100Element(XElement container, string localName)
    {
        // S-100 Part 10b GML uses (5.0) http://www.iho.int/s100gml/5.0 and
        // older datasets use (1.0) either http://www.iho.int/S100/profile/s100gml/1.0
        // or http://www.iho.int/s100gml/1.0. Match on local name and any
        // namespace whose absolute URI ends in "s100gml/<version>" so all three
        // shapes (and any forward-compatible minor versions) are accepted.
        return container.Elements()
            .FirstOrDefault(e =>
                e.Name.LocalName == localName &&
                (e.Name.Namespace == S100Ns5 ||
                 e.Name.Namespace == S100Ns1 ||
                 e.Name.NamespaceName.Contains("s100gml/", StringComparison.OrdinalIgnoreCase)));
    }    private static (ImmutableDictionary<string, string>, ImmutableArray<S127ComplexAttribute>, ImmutableArray<S127FeatureReference>) ParseAttributes(XElement element)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S127ComplexAttribute>();
        var featureRefs = ImmutableArray.CreateBuilder<S127FeatureReference>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip geometry, GML id, and S-100 infrastructure elements.
            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNamespaces.Gml ||
                child.Name.Namespace == S100Ns5 ||
                child.Name.Namespace == S100Ns1)
                continue;

            // xlink:href-bearing children are feature-to-feature references
            // (e.g. <S127:theAuthority xlink:href="#auth1"/>). Capture them
            // as typed references so the strongly-typed projection can
            // resolve them via XlinkResolver.
            var hrefAttr = child.Attribute(XlinkNs + "href");
            if (hrefAttr is not null && !string.IsNullOrEmpty(hrefAttr.Value))
            {
                var href = hrefAttr.Value;
                if (href.StartsWith('#')) href = href[1..];
                if (!string.IsNullOrEmpty(href))
                {
                    featureRefs.Add(new S127FeatureReference
                    {
                        Role = localName,
                        FeatureRef = href,
                    });
                }
                continue;
            }

            if (child.HasElements)
            {
                var subAttrs = ImmutableDictionary.CreateBuilder<string, string>();
                foreach (var sub in child.Elements())
                {
                    if (!sub.HasElements)
                        subAttrs[sub.Name.LocalName] = sub.Value;
                }
                if (subAttrs.Count > 0)
                {
                    complex.Add(new S127ComplexAttribute
                    {
                        Code = localName,
                        SubAttributes = subAttrs.ToImmutable(),
                    });
                }
            }
            else if (!string.IsNullOrEmpty(child.Value))
            {
                simple[localName] = child.Value;
            }
        }

        return (simple.ToImmutable(), complex.ToImmutable(), featureRefs.ToImmutable());
    }

    /// <summary>
    /// True when the element name lives in an application schema (i.e. an
    /// S-127 feature or information type wrapper) rather than GML or S-100
    /// base infrastructure.
    /// </summary>
    /// <remarks>
    /// Some upstream samples mix namespaces — for example a dataset whose
    /// root is in <c>http://www.iho.int/S127/gml/1.0</c> but whose feature
    /// children are in <c>http://www.iho.int/S127/gml/cs0/1.0</c> via a
    /// different prefix declaration. The dataset namespace is therefore
    /// treated as informational only; any namespace that is not GML and is
    /// not an S-100 GML base namespace is accepted as application schema.
    /// </remarks>
    private static bool IsApplicationSchema(XName name, XNamespace datasetNs)
    {
        _ = datasetNs;
        if (name.Namespace == GmlNamespaces.Gml) return false;
        if (name.Namespace.NamespaceName.Contains("s100gml/", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>
    /// Returns root-level children whose local name is <paramref name="localName"/>,
    /// regardless of which namespace the encoder placed them in (dataset, GML, or none).
    /// </summary>
    private static IEnumerable<XElement> EnumerateChildren(XElement root, string localName)
    {
        foreach (var child in root.Elements())
        {
            if (string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal))
                yield return child;
        }
    }
}
