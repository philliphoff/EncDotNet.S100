using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Datasets.S124;

/// <summary>
/// Reads an S-124 GML encoded dataset (S-100 Part 10b) into an <see cref="S124Dataset"/>.
/// </summary>
internal static class S124DatasetReader
{
    // S-100 Part 10b GML namespaces
    private static readonly XNamespace GmlNs = "http://www.opengis.net/gml/3.2";
    private static readonly XNamespace S100Ns = "http://www.iho.int/S100/profile/s100gml/1.0";

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
        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-124 GML document has no root element.");

        // Detect the dataset namespace from the root element
        var datasetNs = root.Name.Namespace;

        // Parse dataset identification
        string? productId = null;
        string? datasetId = root.Attribute(GmlNs + "id")?.Value;

        var dsInfo = root.Element(S100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(S100Ns + "productIdentifier")?.Value;
        }

        // Parse features from <member> elements
        var features = ImmutableArray.CreateBuilder<S124Feature>();
        foreach (var member in root.Elements(MemberName(root)))
        {
            var featureElement = member.Elements()
                .FirstOrDefault(e => IsFeatureType(e.Name, datasetNs));

            if (featureElement is not null)
            {
                var feature = ParseFeature(featureElement);
                if (feature is not null)
                    features.Add(feature);
            }
        }

        // Parse information types from <imember> elements
        var informationTypes = ImmutableArray.CreateBuilder<S124InformationType>();
        foreach (var imember in root.Elements(IMemberName(root)))
        {
            var infoElement = imember.Elements()
                .FirstOrDefault(e => IsInformationType(e.Name, datasetNs));

            if (infoElement is not null)
            {
                var info = ParseInformationType(infoElement);
                if (info is not null)
                    informationTypes.Add(info);
            }
        }

        return new S124Dataset
        {
            ProductIdentifier = productId ?? "S-124",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
            InformationTypes = informationTypes.ToImmutable(),
        };
    }

    private static S124Feature? ParseFeature(XElement element)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var featureType = element.Name.LocalName;

        // Parse geometry
        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element);

        // Parse attributes
        var (simpleAttrs, complexAttrs) = ParseAttributes(element);

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
        };
    }

    private static S124InformationType? ParseInformationType(XElement element)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var typeCode = element.Name.LocalName;

        var (simpleAttrs, complexAttrs) = ParseAttributes(element);

        return new S124InformationType
        {
            Id = id,
            TypeCode = typeCode,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
        };
    }

    private static (S124GeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement)
    {
        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var geometryType = S124GeometryType.None;

        // Look for geometry in the "geometry" child element or directly under the feature
        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");

        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        // S-100 Part 10b point property
        var pointProp = geometryContainer.Element(S100Ns + "pointProperty")
            ?? geometryContainer.Element(S100Ns + "Point");
        if (pointProp is not null)
        {
            var pointCoords = ParsePointElement(pointProp);
            if (pointCoords is not null)
            {
                geometryType = S124GeometryType.Point;
                points = [pointCoords.Value];
            }
            else
            {
                // Try descendant gml:Point
                var gmlPoint = pointProp.Descendants(GmlNs + "Point").FirstOrDefault()
                    ?? pointProp.Descendants(GmlNs + "pos").FirstOrDefault()?.Parent;
                if (gmlPoint is not null)
                {
                    var coord = ParseGmlPoint(gmlPoint);
                    if (coord is not null)
                    {
                        geometryType = S124GeometryType.Point;
                        points = [coord.Value];
                    }
                }
            }
        }

        // S-100 Part 10b curve property
        var curveProp = geometryContainer.Element(S100Ns + "curveProperty");
        if (curveProp is not null)
        {
            geometryType = S124GeometryType.Curve;
            var curveBuilder = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>();
            var coords = ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
                curveBuilder.Add(coords);
            curves = curveBuilder.ToImmutable();
        }

        // S-100 Part 10b surface property
        var surfaceProp = geometryContainer.Element(S100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = S124GeometryType.Surface;
            var (ext, intRings) = ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static (double Latitude, double Longitude)? ParsePointElement(XElement element)
    {
        // Try gml:Point > gml:pos
        var pos = element.Element(GmlNs + "Point")?.Element(GmlNs + "pos")
            ?? element.Element(GmlNs + "pos");

        if (pos is not null)
            return ParsePos(pos.Value);

        // Try S100:Point > gml:pos
        var s100Point = element.Element(S100Ns + "Point");
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

        // Navigate through gml:Curve > gml:segments > gml:LineStringSegment > gml:posList
        // or gml:LineString > gml:posList
        foreach (var posList in curveContainer.Descendants(GmlNs + "posList"))
        {
            coords.AddRange(ParsePosList(posList.Value));
        }

        // Also handle individual gml:pos elements
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

        // gml:Surface > gml:patches > gml:PolygonPatch > gml:exterior/interior
        // or gml:Polygon > gml:exterior/interior
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

    private static (ImmutableDictionary<string, string>, ImmutableArray<S124ComplexAttribute>) ParseAttributes(XElement element)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S124ComplexAttribute>();
        var ns = element.Name.Namespace;

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip geometry, GML id, and S-100 infrastructure elements
            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNs ||
                child.Name.Namespace == S100Ns)
                continue;

            if (child.HasElements)
            {
                // Complex attribute — collect sub-attributes
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
                    complex.Add(new S124ComplexAttribute
                    {
                        Code = localName,
                        SubAttributes = subAttrs.ToImmutable(),
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

        return (simple.ToImmutable(), complex.ToImmutable());
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
