using System.Collections.Immutable;
using System.Xml.Linq;
using EncDotNet.S100.Gml;
using S100Diag = EncDotNet.S100.Datasets.S201.Diagnostics;

namespace EncDotNet.S100.Datasets.S201;

/// <summary>
/// Reads an S-201 Aids to Navigation Information GML encoded dataset
/// (S-100 Part 10b) into an <see cref="S201Dataset"/>.
/// </summary>
/// <remarks>
/// Real-world S-201 Edition 2.0.0 datasets appear in two shapes:
/// <list type="bullet">
///   <item>
///     The <b>XSD-canonical shape</b> declared by the bundled
///     application schema (Annex B): root <c>&lt;Dataset&gt;</c> in the
///     namespace <c>http://www.iho.int/S-201/gml/cs0/1.0</c> with
///     per-feature <c>&lt;member&gt;</c> wrappers and per-information
///     <c>&lt;imember&gt;</c> wrappers, mirroring S-125 / S-127.
///   </item>
///   <item>
///     The <b>real-world published shape</b> seen in IALA-IGO sample
///     and operational datasets: root <c>&lt;DataSet&gt;</c> in the
///     namespace <c>http://www.iho.int/S-201/gml/cs0/2.0</c> (or the
///     legacy <c>http://www.iho.int/201/gml/1.0</c>) with a single
///     unified <c>&lt;members&gt;</c> container holding both features
///     and information types as direct children. Features in this
///     shape are typically encoded in the empty/default namespace
///     rather than the application-schema prefix. This is the same
///     unified-container pattern S-131 uses.
///   </item>
/// </list>
/// The reader tolerates both shapes. Geometry uses the S-100 GML 5.0
/// profile (<c>http://www.iho.int/s100gml/5.0</c>) but the older
/// 1.0 / <c>S100/profile/s100gml/1.0</c> namespaces are also accepted
/// because operational encoders still emit them. Coordinate ordering
/// is <c>lat lon</c> for <c>EPSG:4326</c> per S-100 Part 10b §6.2.
///
/// <para>
/// Cross-references (<c>xlink:href</c>) are split into
/// <see cref="S201Feature.InformationReferences"/> (target is an
/// information type) and <see cref="S201Feature.FeatureReferences"/>
/// (target is a feature) after both have been parsed. The S-201
/// equipment-on-structure relationship surfaces in real datasets as
/// <c>&lt;child&gt;</c> / <c>&lt;parent&gt;</c> with
/// <c>xlink:title="StructureEquipment"</c>; this reader preserves
/// whichever role names the encoder used.
/// </para>
/// </remarks>
internal static class S201DatasetReader
{
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";

    private static readonly XNamespace S100Ns5 = "http://www.iho.int/s100gml/5.0";
    private static readonly XNamespace S100Ns1Profile = "http://www.iho.int/S100/profile/s100gml/1.0";
    private static readonly XNamespace S100Ns1Lower = "http://www.iho.int/s100gml/1.0";

    // S-201 Edition 2.0.0 information type codes (FC Annex C2).
    private static readonly HashSet<string> InformationTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AtoNFixingMethod",
        "AtonStatusInformation",
        "PositioningInformation",
        "SpatialQuality",
    };

    public static S201Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-201");

        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd().TrimStart();
        var doc = XDocument.Parse(xml);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-201 GML document has no root element.");

        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;
        string? productId = ReadProductIdentifier(root);

        var features = ImmutableArray.CreateBuilder<S201Feature>();
        var informationTypes = ImmutableArray.CreateBuilder<S201InformationType>();
        var informationTypeIds = new HashSet<string>(StringComparer.Ordinal);

        var pendingFeatureRefs = new List<(PendingFeature Pending, List<(string Role, string Target)> Refs)>();

        // Shape A: unified <members> container (S-131-style; what real
        // S-201 datasets emit). Discriminate by FC-known info-type codes.
        var unifiedContainer = FindLocalElement(root, "members");
        if (unifiedContainer is not null)
        {
            foreach (var element in unifiedContainer.Elements())
            {
                if (!IsApplicationSchema(element.Name)) continue;

                if (InformationTypeCodes.Contains(element.Name.LocalName))
                {
                    var info = ParseInformationType(element);
                    informationTypes.Add(info);
                    if (!string.IsNullOrEmpty(info.Id))
                        informationTypeIds.Add(info.Id);
                }
                else
                {
                    var (pending, refs) = ParseFeaturePending(element);
                    pendingFeatureRefs.Add((pending, refs));
                }
            }
        }

        // Shape B: XSD-canonical <imember> + <member> wrappers.
        foreach (var imember in EnumerateLocalChildren(root, "imember"))
        {
            var infoElement = imember.Elements().FirstOrDefault(e => IsApplicationSchema(e.Name));
            if (infoElement is null) continue;
            var info = ParseInformationType(infoElement);
            informationTypes.Add(info);
            if (!string.IsNullOrEmpty(info.Id))
                informationTypeIds.Add(info.Id);
        }
        foreach (var member in EnumerateLocalChildren(root, "member"))
        {
            var featureElement = member.Elements().FirstOrDefault(e => IsApplicationSchema(e.Name));
            if (featureElement is null) continue;
            var (pending, refs) = ParseFeaturePending(featureElement);
            pendingFeatureRefs.Add((pending, refs));
        }

        // Pass 2: split xlink references using the now-complete info-type id set.
        // Unknown targets default to feature references.
        foreach (var (pending, refs) in pendingFeatureRefs)
        {
            var infoRefs = ImmutableArray.CreateBuilder<S201InformationReference>();
            var featureRefs = ImmutableArray.CreateBuilder<S201FeatureReference>();
            foreach (var (role, target) in refs)
            {
                if (informationTypeIds.Contains(target))
                {
                    infoRefs.Add(new S201InformationReference { Role = role, InformationRef = target });
                }
                else
                {
                    featureRefs.Add(new S201FeatureReference { Role = role, TargetRef = target });
                }
            }
            features.Add(new S201Feature
            {
                Id = pending.Id,
                FeatureType = pending.FeatureType,
                GeometryType = pending.GeometryType,
                Points = pending.Points,
                Curves = pending.Curves,
                ExteriorRing = pending.ExteriorRing,
                InteriorRings = pending.InteriorRings,
                Attributes = pending.Attributes,
                ComplexAttributes = pending.ComplexAttributes,
                InformationReferences = infoRefs.ToImmutable(),
                FeatureReferences = featureRefs.ToImmutable(),
            });
        }

        return new S201Dataset
        {
            ProductIdentifier = productId ?? "S-201",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
            InformationTypes = informationTypes.ToImmutable(),
        };
    }

    private static string? ReadProductIdentifier(XElement root)
    {
        foreach (var ns in new[] { S100Ns5, S100Ns1Profile, S100Ns1Lower })
        {
            var dsInfo = root.Element(ns + "DatasetIdentificationInformation");
            var productId = dsInfo?.Element(ns + "productIdentifier")?.Value;
            if (!string.IsNullOrEmpty(productId)) return productId;
        }
        return null;
    }

    private readonly record struct PendingFeature(
        string Id,
        string FeatureType,
        GmlGeometryType GeometryType,
        ImmutableArray<(double, double)> Points,
        ImmutableArray<ImmutableArray<(double, double)>> Curves,
        ImmutableArray<(double, double)> ExteriorRing,
        ImmutableArray<ImmutableArray<(double, double)>> InteriorRings,
        ImmutableDictionary<string, string> Attributes,
        ImmutableArray<S201ComplexAttribute> ComplexAttributes);

    private static (PendingFeature, List<(string, string)>) ParseFeaturePending(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value
            ?? element.Attribute("id")?.Value
            ?? "";
        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element);
        var (simpleAttrs, complexAttrs, xlinkRefs) = ParseAttributes(element);

        var pending = new PendingFeature(
            id, element.Name.LocalName, geometryType,
            points, curves, exteriorRing, interiorRings,
            simpleAttrs, complexAttrs);
        return (pending, xlinkRefs);
    }

    private static S201InformationType ParseInformationType(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value
            ?? element.Attribute("id")?.Value
            ?? "";
        var (simpleAttrs, complexAttrs, _) = ParseAttributes(element);

        return new S201InformationType
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

        var geometryContainer = featureElement.Elements()
            .FirstOrDefault(e => e.Name.LocalName == "geometry");
        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

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

        var curveProp = FindS100Element(geometryContainer, "curveProperty");
        if (curveProp is not null)
        {
            geometryType = GmlGeometryType.Curve;
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
                curves = [coords];
        }

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
        return container.Elements()
            .FirstOrDefault(e =>
                e.Name.LocalName == localName &&
                (e.Name.Namespace == S100Ns5 ||
                 e.Name.Namespace == S100Ns1Profile ||
                 e.Name.Namespace == S100Ns1Lower ||
                 e.Name.NamespaceName.Contains("s100gml/", StringComparison.OrdinalIgnoreCase)));
    }

    private static (ImmutableDictionary<string, string>, ImmutableArray<S201ComplexAttribute>, List<(string, string)>) ParseAttributes(XElement element)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S201ComplexAttribute>();
        var xlinkRefs = new List<(string, string)>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNamespaces.Gml ||
                child.Name.Namespace == S100Ns5 ||
                child.Name.Namespace == S100Ns1Profile ||
                child.Name.Namespace == S100Ns1Lower)
            {
                continue;
            }

            var href = child.Attribute(XlinkNs + "href")?.Value;
            if (!string.IsNullOrEmpty(href) && !child.HasElements && string.IsNullOrEmpty(child.Value))
            {
                xlinkRefs.Add((localName, href.TrimStart('#')));
                continue;
            }

            if (child.HasElements)
            {
                var subAttrs = ImmutableDictionary.CreateBuilder<string, string>();
                foreach (var sub in child.Elements())
                {
                    if (!sub.HasElements && !string.IsNullOrEmpty(sub.Value))
                        subAttrs[sub.Name.LocalName] = sub.Value;
                }
                if (subAttrs.Count > 0)
                {
                    complex.Add(new S201ComplexAttribute
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

        return (simple.ToImmutable(), complex.ToImmutable(), xlinkRefs);
    }

    /// <summary>
    /// True when the element name lives in an application schema (i.e. an
    /// S-201 feature or information type wrapper) rather than GML or
    /// S-100 base infrastructure. In real-world S-201 datasets features
    /// often appear in the empty/default namespace, which is also
    /// considered application schema for this purpose.
    /// </summary>
    private static bool IsApplicationSchema(XName name)
    {
        if (name.Namespace == GmlNamespaces.Gml) return false;
        if (name.Namespace.NamespaceName.Contains("s100gml/", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    private static XElement? FindLocalElement(XElement root, string localName)
    {
        foreach (var child in root.Elements())
        {
            if (string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal))
                return child;
        }
        return null;
    }

    private static IEnumerable<XElement> EnumerateLocalChildren(XElement root, string localName)
    {
        foreach (var child in root.Elements())
        {
            if (string.Equals(child.Name.LocalName, localName, StringComparison.Ordinal))
                yield return child;
        }
    }
}
