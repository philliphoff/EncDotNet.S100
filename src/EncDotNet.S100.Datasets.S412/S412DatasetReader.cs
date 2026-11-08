using System.Xml.Linq;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Features;
using S100Diag = EncDotNet.S100.Datasets.S412.Diagnostics;

namespace EncDotNet.S100.Datasets.S412;

/// <summary>
/// Reads an S-412 GML encoded dataset (S-100 Part 10b) into an <see cref="S412Dataset"/>.
/// </summary>
internal static class S412DatasetReader
{
    // The upstream S-412 sample and schema use the S-100 5.0 GML namespace;
    // the 1.0 profile namespace is accepted too, as for the other GML products.
    private static readonly XNamespace[] CandidateS100Namespaces =
    [
        "http://www.iho.int/s100gml/5.0",
        "http://www.iho.int/s100gml/1.0",
        "http://www.iho.int/S100/profile/s100gml/1.0",
    ];

    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    // S-412 feature type codes (FC 1.1.0 draft v009). WeatherWarning is
    // abstract and never encoded.
    private static readonly HashSet<string> FeatureTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "IceAccretionWarning",
        "RestrictedVisibilityWarning",
        "SignificantWaveHeightWarning",
        "ThunderstormWarning",
        "WindWarning",
    };

    // S-412 information type codes (FC 1.1.0 draft v009).
    private static readonly HashSet<string> InformationTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DataProvider",
    };

    public static S412Dataset Read(Stream stream)
    {
        using var activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        activity?.SetTag("s100.product", "S-412");
        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-412 GML document has no root element.");

        // Decide the axis order once for the whole dataset (issue #650).
        var (axisOrder, axisAssessment) = GmlCoordinateParser.AssessAxisOrder(root);

        var datasetNs = root.Name.Namespace;
        var s100Ns = DetectS100Namespace(root);

        var productId = root.Element(s100Ns + "DatasetIdentificationInformation")
            ?.Element(s100Ns + "productIdentifier")?.Value;

        // Members may be wrapped one per <member>/<imember> or all in one
        // <members>/<imembers> container (as in the upstream sample), so walk
        // the descendants for known type names.
        var features = new List<S412Feature>();
        var informationTypes = new List<S412InformationType>();

        foreach (var element in root.Descendants())
        {
            if (!IsDatasetName(element.Name, datasetNs))
                continue;

            if (FeatureTypeCodes.Contains(element.Name.LocalName))
                features.Add(ParseFeature(element, s100Ns, axisOrder));
            else if (InformationTypeCodes.Contains(element.Name.LocalName))
                informationTypes.Add(ParseInformationType(element, s100Ns));
        }

        return new S412Dataset
        {
            ProductIdentifier = productId ?? "S-412",
            DeclaredEdition = GmlDatasetIdentification.ReadDeclaredEdition(root),
            DatasetIdentifier = root.Attribute(GmlNamespaces.Gml + "id")?.Value,
            Features = features,
            InformationTypes = informationTypes,
            AxisOrder = axisAssessment,
        };
    }

    private static XNamespace DetectS100Namespace(XElement root)
    {
        foreach (var attr in root.Attributes())
        {
            if (!attr.IsNamespaceDeclaration) continue;
            foreach (var candidate in CandidateS100Namespaces)
            {
                if (string.Equals(attr.Value, candidate.NamespaceName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            if (attr.Value.Contains("s100gml", StringComparison.OrdinalIgnoreCase))
                return attr.Value;
        }

        foreach (var candidate in CandidateS100Namespaces)
        {
            if (root.Descendants(candidate + "DatasetIdentificationInformation").Any())
                return candidate;
        }

        return CandidateS100Namespaces[0];
    }

    private static S412Feature ParseFeature(XElement element, XNamespace s100Ns, GmlAxisOrder axisOrder)
    {
        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element, s100Ns, axisOrder);
        var (simple, complex, references) = ParseAttributes(element, s100Ns);

        return new S412Feature
        {
            Id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "",
            FeatureType = element.Name.LocalName,
            GeometryType = geometryType,
            Points = points,
            Curves = curves,
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
            Attributes = simple,
            ComplexAttributes = complex,
            References = references,
        };
    }

    private static S412InformationType ParseInformationType(XElement element, XNamespace s100Ns)
    {
        var (simple, complex, references) = ParseAttributes(element, s100Ns);

        return new S412InformationType
        {
            Id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "",
            TypeCode = element.Name.LocalName,
            Attributes = simple,
            ComplexAttributes = complex,
            References = references,
        };
    }

    private static (S100GeometryType, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>, IReadOnlyList<GeoPosition>, IReadOnlyList<IReadOnlyList<GeoPosition>>) ParseGeometry(
        XElement featureElement, XNamespace s100Ns, GmlAxisOrder axisOrder)
    {
        IReadOnlyList<GeoPosition> points = [];
        IReadOnlyList<IReadOnlyList<GeoPosition>> curves = [];
        IReadOnlyList<GeoPosition> exteriorRing = [];
        IReadOnlyList<IReadOnlyList<GeoPosition>> interiorRings = [];
        var geometryType = S100GeometryType.None;

        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry");
        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        var pointProp = geometryContainer.Element(s100Ns + "pointProperty");
        if (pointProp is not null
            && GmlCoordinateParser.ParsePointElement(pointProp, s100Ns, axisOrder) is { } point)
        {
            geometryType = S100GeometryType.Point;
            points = [point];
        }

        var curveProp = geometryContainer.Element(s100Ns + "curveProperty");
        if (curveProp is not null)
        {
            geometryType = S100GeometryType.Curve;
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp, axisOrder);
            curves = coords.Count > 0 ? [coords] : [];
        }

        var surfaceProp = geometryContainer.Element(s100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = S100GeometryType.Surface;
            (exteriorRing, interiorRings) = GmlCoordinateParser.ParseSurfaceCoordinates(surfaceProp, axisOrder);
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static (IReadOnlyDictionary<string, string>, IReadOnlyList<S412ComplexAttribute>, IReadOnlyList<GmlReference>) ParseAttributes(
        XElement element, XNamespace s100Ns)
    {
        var simple = new Dictionary<string, string>();
        var complex = new List<S412ComplexAttribute>();
        var refs = new List<GmlReference>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName is "geometry" or "boundedBy"
                || child.Name.Namespace == GmlNamespaces.Gml
                || child.Name.Namespace == s100Ns)
                continue;

            // xlink:href cross-reference: the element's local name is the
            // association role (dataProviderInformation in FC 1.1.0).
            if (child.Attribute(XLinkNs + "href")?.Value is { } href)
            {
                refs.Add(new GmlReference
                {
                    Role = localName,
                    Href = href,
                    ArcRole = child.Attribute(XLinkNs + "arcrole")?.Value,
                });
                continue;
            }

            if (LeafValue(child, s100Ns) is { } value)
            {
                if (value.Length > 0)
                    simple[localName] = value;
            }
            else if (ParseComplex(child, s100Ns) is { } attribute)
            {
                complex.Add(attribute);
            }
        }

        return (simple, complex, refs);
    }

    /// <summary>
    /// Parses a complex attribute: its leaves into
    /// <see cref="S412ComplexAttribute.SubAttributes"/> and each nested complex
    /// attribute into <see cref="S412ComplexAttribute.Groups"/>, its leaves also
    /// copied up under dotted keys.
    /// </summary>
    private static S412ComplexAttribute? ParseComplex(XElement element, XNamespace s100Ns)
    {
        var subAttributes = new Dictionary<string, string>();
        List<S412ComplexAttribute>? groups = null;

        foreach (var sub in element.Elements())
        {
            if (sub.Attribute(XLinkNs + "href") is not null)
                continue;

            if (LeafValue(sub, s100Ns) is { } value)
            {
                if (value.Length > 0)
                    subAttributes.TryAdd(sub.Name.LocalName, value);
                continue;
            }

            if (ParseComplex(sub, s100Ns) is not { } group)
                continue;

            (groups ??= []).Add(group);
            foreach (var (code, nested) in group.SubAttributes)
                subAttributes.TryAdd(sub.Name.LocalName + "." + code, nested);
        }

        return subAttributes.Count == 0
            ? null
            : new S412ComplexAttribute
            {
                Code = element.Name.LocalName,
                SubAttributes = subAttributes,
                Groups = (IReadOnlyList<S412ComplexAttribute>?)groups ?? [],
            };
    }

    /// <summary>
    /// The value of a simple attribute or sub-attribute, or <c>null</c> when
    /// <paramref name="element"/> is a complex one. An enumeration's value is
    /// its <c>code</c> attribute (S-100 Part 10b 5.0 writes the label as the
    /// text). An <c>S100_TruncatedDate</c> wrapper (<c>dateStart</c> holding
    /// <c>S100:date</c>) yields the date text.
    /// </summary>
    private static string? LeafValue(XElement element, XNamespace s100Ns)
    {
        if (!element.HasElements)
        {
            return element.Attribute("code")?.Value is { Length: > 0 } code
                ? code.Trim()
                : element.Value.Trim();
        }

        using var children = element.Elements().GetEnumerator();
        children.MoveNext();
        var only = children.Current;
        return !children.MoveNext() && only.Name.Namespace == s100Ns && !only.HasElements
            ? only.Value.Trim()
            : null;
    }

    private static bool IsDatasetName(XName name, XNamespace datasetNs) =>
        name.Namespace == datasetNs || name.Namespace == XNamespace.None;
}
