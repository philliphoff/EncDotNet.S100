using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;

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
    private static readonly XNamespace GmlNs = "http://www.opengis.net/gml/3.2";

    // S-100 Part 10b GML namespaces — accept both 5.0 (current) and 1.0 (legacy).
    private static readonly XNamespace S100Ns5 = "http://www.iho.int/s100gml/5.0";
    private static readonly XNamespace S100Ns1 = "http://www.iho.int/S100/profile/s100gml/1.0";

    public static S127Dataset Read(Stream stream)
    {
        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-127 GML document has no root element.");

        var datasetNs = root.Name.Namespace;

        string? datasetId = root.Attribute(GmlNs + "id")?.Value;
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
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element);
        var (simpleAttrs, complexAttrs) = ParseAttributes(element);

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
        };
    }

    private static S127InformationType ParseInformationType(XElement element)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var (simpleAttrs, complexAttrs) = ParseAttributes(element);

        return new S127InformationType
        {
            Id = id,
            TypeCode = element.Name.LocalName,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
        };
    }

    private static (S127GeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement)
    {
        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var geometryType = S127GeometryType.None;

        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");

        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        // pointProperty under either S-100 GML 5.0 or 1.0
        var pointProp = FindS100Element(geometryContainer, "pointProperty");
        if (pointProp is not null)
        {
            var coord = ParsePointDescendant(pointProp);
            if (coord is not null)
            {
                geometryType = S127GeometryType.Point;
                points = [coord.Value];
            }
        }

        // curveProperty
        var curveProp = FindS100Element(geometryContainer, "curveProperty");
        if (curveProp is not null)
        {
            geometryType = S127GeometryType.Curve;
            var coords = ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
            {
                curves = [coords];
            }
        }

        // surfaceProperty
        var surfaceProp = FindS100Element(geometryContainer, "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = S127GeometryType.Surface;
            var (ext, intRings) = ParseSurfaceCoordinates(surfaceProp);
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
    }

    private static (double Latitude, double Longitude)? ParsePointDescendant(XElement element)
    {
        // gml:Point/gml:pos descendants — accept whatever wrapper the encoder used
        // (S100:Point, gml:Point, or a bare gml:pos under the property).
        var pos = element.Descendants(GmlNs + "pos").FirstOrDefault();
        return pos is null ? null : ParsePos(pos.Value);
    }

    private static ImmutableArray<(double Latitude, double Longitude)> ParseCurveCoordinates(XElement curveContainer)
    {
        var coords = ImmutableArray.CreateBuilder<(double, double)>();

        foreach (var posList in curveContainer.Descendants(GmlNs + "posList"))
            coords.AddRange(ParsePosList(posList.Value));

        if (coords.Count == 0)
        {
            foreach (var pos in curveContainer.Descendants(GmlNs + "pos"))
            {
                var coord = ParsePos(pos.Value);
                if (coord is not null) coords.Add(coord.Value);
            }
        }

        return coords.ToImmutable();
    }

    private static (ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseSurfaceCoordinates(XElement surfaceContainer)
    {
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>();

        var exterior = surfaceContainer.Descendants(GmlNs + "exterior").FirstOrDefault();
        if (exterior is not null) exteriorRing = ReadRing(exterior);

        foreach (var interior in surfaceContainer.Descendants(GmlNs + "interior"))
        {
            var ring = ReadRing(interior);
            if (ring.Length > 0) interiorRings.Add(ring);
        }

        return (exteriorRing, interiorRings.ToImmutable());
    }

    private static ImmutableArray<(double Latitude, double Longitude)> ReadRing(XElement ringContainer)
    {
        var posList = ringContainer.Descendants(GmlNs + "posList").FirstOrDefault();
        if (posList is not null) return ParsePosList(posList.Value);

        var coords = ImmutableArray.CreateBuilder<(double, double)>();
        foreach (var pos in ringContainer.Descendants(GmlNs + "pos"))
        {
            var coord = ParsePos(pos.Value);
            if (coord is not null) coords.Add(coord.Value);
        }
        return coords.ToImmutable();
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

    private static (ImmutableDictionary<string, string>, ImmutableArray<S127ComplexAttribute>) ParseAttributes(XElement element)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S127ComplexAttribute>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip geometry, GML id, and S-100 infrastructure elements.
            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNs ||
                child.Name.Namespace == S100Ns5 ||
                child.Name.Namespace == S100Ns1)
                continue;

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

        return (simple.ToImmutable(), complex.ToImmutable());
    }

    /// <summary>
    /// True when the element name lives in the dataset's application schema,
    /// i.e. it is a candidate feature or information type wrapper rather than
    /// GML or S-100-base infrastructure.
    /// </summary>
    private static bool IsApplicationSchema(XName name, XNamespace datasetNs)
    {
        if (name.Namespace == GmlNs) return false;
        if (name.Namespace == S100Ns5 || name.Namespace == S100Ns1) return false;
        return name.Namespace == datasetNs || name.Namespace == XNamespace.None;
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
