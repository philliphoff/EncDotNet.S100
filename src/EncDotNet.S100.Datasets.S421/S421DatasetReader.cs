using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Gml;
using S100Diag = EncDotNet.S100.Datasets.S421.Diagnostics;

namespace EncDotNet.S100.Datasets.S421;

/// <summary>
/// Reads an S-421 Route Plan GML encoded dataset (S-100 Part 10b) into an
/// <see cref="S421Dataset"/>. Features are gathered from <c>&lt;member&gt;</c>
/// elements and information types from <c>&lt;imember&gt;</c> elements.
/// </summary>
internal static class S421DatasetReader
{
    private static readonly XNamespace GmlNs = "http://www.opengis.net/gml/3.2";
    private static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";

    // S-421 sample data uses the lowercase 1.0 namespace, but be liberal:
    // any namespace whose URI ends with "/s100gml/<version>" is accepted.
    private static readonly XNamespace S100Ns_1_0_lower = "http://www.iho.int/s100gml/1.0";
    private static readonly XNamespace S100Ns_1_0_profile = "http://www.iho.int/S100/profile/s100gml/1.0";
    private static readonly XNamespace S100Ns_5_0 = "http://www.iho.int/s100gml/5.0";

    public static S421Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-421");
        // Some sample datasets contain leading whitespace before the XML declaration.
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd().TrimStart();
        var doc = XDocument.Parse(xml);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-421 GML document has no root element.");

        var datasetNs = root.Name.Namespace;
        var s100Ns = DetectS100Namespace(root);

        string? datasetId = root.Attribute(GmlNs + "id")?.Value;
        string? productId = null;

        // Optional S-100 dataset identification block (some samples omit it).
        var dsInfo = root.Element(s100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(s100Ns + "productIdentifier")?.Value;
        }

        var features = ImmutableArray.CreateBuilder<S421Feature>();
        foreach (var member in EnumerateMembers(root, datasetNs, "member"))
        {
            var element = member.Elements().FirstOrDefault();
            if (element is null) continue;
            features.Add(ParseFeature(element, s100Ns));
        }

        var informationTypes = ImmutableArray.CreateBuilder<S421InformationType>();
        foreach (var imember in EnumerateMembers(root, datasetNs, "imember"))
        {
            var element = imember.Elements().FirstOrDefault();
            if (element is null) continue;
            informationTypes.Add(ParseInformationType(element, s100Ns));
        }

        return new S421Dataset
        {
            ProductIdentifier = productId ?? "S-421",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
            InformationTypes = informationTypes.ToImmutable(),
        };
    }

    private static IEnumerable<XElement> EnumerateMembers(XElement root, XNamespace datasetNs, string localName)
    {
        // The GML Dataset envelope may emit member/imember either unqualified
        // or in the dataset namespace; tolerate both.
        foreach (var m in root.Elements(datasetNs + localName))
            yield return m;
        foreach (var m in root.Elements(localName))
            yield return m;
    }

    private static S421Feature ParseFeature(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var (geomType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element, s100Ns);
        var (simple, complex, references) = ParseAttributes(element, s100Ns);

        return new S421Feature
        {
            Id = id,
            FeatureType = element.Name.LocalName,
            GeometryType = geomType,
            Points = points,
            Curves = curves,
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
            Attributes = simple,
            ComplexAttributes = complex,
            References = references,
        };
    }

    private static S421InformationType ParseInformationType(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var (simple, complex, references) = ParseAttributes(element, s100Ns);

        return new S421InformationType
        {
            Id = id,
            TypeCode = element.Name.LocalName,
            Attributes = simple,
            ComplexAttributes = complex,
            References = references,
        };
    }

    private static (GmlGeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement, XNamespace s100Ns)
    {
        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exterior = ImmutableArray<(double, double)>.Empty;
        var interiors = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var geomType = GmlGeometryType.None;

        var geometry = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");
        if (geometry is null)
            return (geomType, points, curves, exterior, interiors);

        var pointProp = geometry.Element(s100Ns + "pointProperty")
            ?? geometry.Element(s100Ns + "Point");
        if (pointProp is not null)
        {
            var coord = ParsePointElement(pointProp, s100Ns);
            if (coord is not null)
            {
                geomType = GmlGeometryType.Point;
                points = [coord.Value];
            }
        }

        var curveProp = geometry.Element(s100Ns + "curveProperty");
        if (curveProp is not null)
        {
            var coords = ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
            {
                geomType = GmlGeometryType.Curve;
                curves = [coords];
            }
        }

        var surfaceProp = geometry.Element(s100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            var (ext, ints) = ParseSurfaceCoordinates(surfaceProp);
            if (ext.Length > 0)
            {
                geomType = GmlGeometryType.Surface;
                exterior = ext;
                interiors = ints;
            }
        }

        return (geomType, points, curves, exterior, interiors);
    }

    private static (double Latitude, double Longitude)? ParsePointElement(XElement element, XNamespace s100Ns)
    {
        var pos = element.Element(GmlNs + "Point")?.Element(GmlNs + "pos")
            ?? element.Element(GmlNs + "pos")
            ?? element.Element(s100Ns + "Point")?.Element(GmlNs + "pos");
        if (pos is not null)
            return ParsePos(pos.Value);

        // Fall back to any descendant gml:pos.
        var anyPos = element.Descendants(GmlNs + "pos").FirstOrDefault();
        return anyPos is not null ? ParsePos(anyPos.Value) : null;
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
                var c = ParsePos(pos.Value);
                if (c is not null) coords.Add(c.Value);
            }
        }
        return coords.ToImmutable();
    }

    private static (ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseSurfaceCoordinates(XElement surfaceContainer)
    {
        var exterior = ImmutableArray<(double, double)>.Empty;
        var interiors = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>();

        var ext = surfaceContainer.Descendants(GmlNs + "exterior").FirstOrDefault();
        if (ext is not null)
        {
            var posList = ext.Descendants(GmlNs + "posList").FirstOrDefault();
            if (posList is not null)
            {
                exterior = ParsePosList(posList.Value);
            }
            else
            {
                var b = ImmutableArray.CreateBuilder<(double, double)>();
                foreach (var pos in ext.Descendants(GmlNs + "pos"))
                {
                    var c = ParsePos(pos.Value);
                    if (c is not null) b.Add(c.Value);
                }
                exterior = b.ToImmutable();
            }
        }

        foreach (var interior in surfaceContainer.Descendants(GmlNs + "interior"))
        {
            var posList = interior.Descendants(GmlNs + "posList").FirstOrDefault();
            if (posList is not null)
            {
                interiors.Add(ParsePosList(posList.Value));
            }
            else
            {
                var b = ImmutableArray.CreateBuilder<(double, double)>();
                foreach (var pos in interior.Descendants(GmlNs + "pos"))
                {
                    var c = ParsePos(pos.Value);
                    if (c is not null) b.Add(c.Value);
                }
                if (b.Count > 0) interiors.Add(b.ToImmutable());
            }
        }

        return (exterior, interiors.ToImmutable());
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

    private static ImmutableArray<(double Latitude, double Longitude)> ParsePosList(string value)
    {
        var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
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

    private static (ImmutableDictionary<string, string>, ImmutableArray<S421ComplexAttribute>, ImmutableArray<S421Reference>) ParseAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S421ComplexAttribute>();
        var refs = ImmutableArray.CreateBuilder<S421Reference>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip geometry and infrastructure elements.
            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNs ||
                child.Name.Namespace == s100Ns)
                continue;

            // xlink:href reference (often an empty element with attributes only).
            var href = child.Attribute(XLinkNs + "href")?.Value;
            if (href is not null)
            {
                refs.Add(new S421Reference
                {
                    Role = localName,
                    Href = href,
                    ArcRole = child.Attribute(XLinkNs + "arcrole")?.Value,
                });
                continue;
            }

            if (child.HasElements)
            {
                var sub = ImmutableDictionary.CreateBuilder<string, string>();
                foreach (var s in child.Elements())
                {
                    if (!s.HasElements && s.Attribute(XLinkNs + "href") is null)
                    {
                        sub[s.Name.LocalName] = s.Value;
                    }
                }
                if (sub.Count > 0)
                {
                    complex.Add(new S421ComplexAttribute
                    {
                        Code = localName,
                        SubAttributes = sub.ToImmutable(),
                    });
                }
            }
            else if (!string.IsNullOrEmpty(child.Value))
            {
                simple[localName] = child.Value;
            }
        }

        return (simple.ToImmutable(), complex.ToImmutable(), refs.ToImmutable());
    }

    /// <summary>
    /// Detects which S-100 GML profile namespace is in use by inspecting the
    /// namespace declarations on the root element. Falls back to the lowercase
    /// 1.0 namespace used by the IEC S-421 sample datasets.
    /// </summary>
    private static XNamespace DetectS100Namespace(XElement root)
    {
        foreach (var attr in root.Attributes())
        {
            if (!attr.IsNamespaceDeclaration) continue;
            if (attr.Value == S100Ns_5_0.NamespaceName) return S100Ns_5_0;
            if (attr.Value == S100Ns_1_0_profile.NamespaceName) return S100Ns_1_0_profile;
            if (attr.Value == S100Ns_1_0_lower.NamespaceName) return S100Ns_1_0_lower;
        }
        return S100Ns_1_0_lower;
    }
}
