using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// Reads an S-411 Sea Ice GML encoded dataset (S-100 Part 10b) into an
/// <see cref="S411Dataset"/>. Features are gathered from <c>&lt;members&gt;</c>
/// child elements; S-411 defines no information types.
/// </summary>
/// <remarks>
/// The S-411 1.2.1 sample datasets emit feature elements directly under a
/// <c>&lt;members&gt;</c> wrapper (note the trailing 's'), with no S-411
/// application-schema namespace declared on the dataset root. This reader is
/// permissive about both forms (<c>member</c> or <c>members</c>) and accepts
/// either the S-100 Part 10b 1.0 or 5.0 GML profile namespace.
/// </remarks>
internal static class S411DatasetReader
{
    private static readonly XNamespace GmlNs = "http://www.opengis.net/gml/3.2";

    private static readonly XNamespace S100Ns_1_0_lower = "http://www.iho.int/s100gml/1.0";
    private static readonly XNamespace S100Ns_1_0_profile = "http://www.iho.int/S100/profile/s100gml/1.0";
    private static readonly XNamespace S100Ns_5_0 = "http://www.iho.int/s100gml/5.0";

    // S-411 Edition 1.2.1 Feature Catalogue feature types.
    private static readonly HashSet<string> FeatureTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DataCoverage",
        "Floeberg",
        "GroundedHummock",
        "IceCompacting",
        "IceDivergence",
        "IceDrift",
        "IceEdge",
        "IceFracture",
        "IceKeelBummock",
        "IceLead",
        "IceRafting",
        "IceRidgeHummock",
        "IceShear",
        "IceThickness",
        "Iceberg",
        "IcebergArea",
        "IcebergLimit",
        "JammedBrashBarrier",
        "LakeIce",
        "LimitOfAllKnownIce",
        "LimitOfOpenWater",
        "LineOfIceCrack",
        "LineOfIceFracture",
        "LineOfIceLead",
        "LineOfIceRidge",
        "SeaIce",
        "SnowCover",
        "StageOfMelt",
        "StripsAndPatches",
    };

    public static S411Dataset Read(Stream stream)
    {
        // Tolerate leading whitespace before the XML declaration.
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd().TrimStart();
        var doc = XDocument.Parse(xml);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-411 GML document has no root element.");

        var datasetNs = root.Name.Namespace;
        var s100Ns = DetectS100Namespace(root);

        string? datasetId = root.Attribute(GmlNs + "id")?.Value;
        string? productId = null;

        var dsInfo = root.Element(s100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(s100Ns + "productIdentifier")?.Value;
        }

        var features = ImmutableArray.CreateBuilder<S411Feature>();
        foreach (var memberContainer in EnumerateMembers(root, datasetNs))
        {
            // Each <members> wrapper in S-411 samples contains many feature
            // siblings (DataCoverage, SeaIce, Iceberg, ...). Iterate children
            // and treat any element whose name matches a known feature type as
            // a feature instance. Other forms emit a single feature inside
            // <member>, which this loop handles equally well.
            foreach (var element in memberContainer.Elements())
            {
                if (!IsFeatureType(element.Name, datasetNs)) continue;
                features.Add(ParseFeature(element, s100Ns));
            }
        }

        return new S411Dataset
        {
            ProductIdentifier = productId ?? "S-411",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
        };
    }

    private static IEnumerable<XElement> EnumerateMembers(XElement root, XNamespace datasetNs)
    {
        foreach (var localName in new[] { "members", "member" })
        {
            foreach (var el in root.Elements(datasetNs + localName))
                yield return el;
            if (datasetNs != XNamespace.None)
            {
                foreach (var el in root.Elements((XName)localName))
                    yield return el;
            }
        }
    }

    private static XNamespace DetectS100Namespace(XElement root)
    {
        foreach (var attr in root.Attributes())
        {
            if (attr.IsNamespaceDeclaration)
            {
                var v = attr.Value;
                if (v == S100Ns_5_0.NamespaceName) return S100Ns_5_0;
                if (v == S100Ns_1_0_profile.NamespaceName) return S100Ns_1_0_profile;
                if (v == S100Ns_1_0_lower.NamespaceName) return S100Ns_1_0_lower;
            }
        }
        return S100Ns_5_0;
    }

    private static S411Feature ParseFeature(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var featureType = element.Name.LocalName;

        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element, s100Ns);
        var (simpleAttrs, complexAttrs) = ParseAttributes(element, s100Ns);

        return new S411Feature
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

    private static (S411GeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement, XNamespace s100Ns)
    {
        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var geometryType = S411GeometryType.None;

        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");

        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        var pointProp = geometryContainer.Element(s100Ns + "pointProperty")
            ?? geometryContainer.Element(s100Ns + "Point");
        if (pointProp is not null)
        {
            var coord = ParseGmlPointCoord(pointProp);
            if (coord is not null)
            {
                geometryType = S411GeometryType.Point;
                points = [coord.Value];
            }
        }

        var curveProp = geometryContainer.Element(s100Ns + "curveProperty");
        if (curveProp is not null)
        {
            geometryType = S411GeometryType.Curve;
            var coords = ParseCurveCoordinates(curveProp);
            curves = coords.Length > 0
                ? ImmutableArray.Create(coords)
                : ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        }

        var surfaceProp = geometryContainer.Element(s100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = S411GeometryType.Surface;
            var (ext, intRings) = ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static (double Latitude, double Longitude)? ParseGmlPointCoord(XElement element)
    {
        var pos = element.Descendants(GmlNs + "pos").FirstOrDefault();
        if (pos is not null)
            return ParsePos(pos.Value);
        return null;
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

        var exterior = surfaceContainer.Descendants(GmlNs + "exterior").FirstOrDefault();
        if (exterior is not null)
        {
            exteriorRing = ParseRing(exterior);
        }

        foreach (var interior in surfaceContainer.Descendants(GmlNs + "interior"))
        {
            var ring = ParseRing(interior);
            if (ring.Length > 0)
                interiorRings.Add(ring);
        }

        return (exteriorRing, interiorRings.ToImmutable());
    }

    private static ImmutableArray<(double, double)> ParseRing(XElement ringContainer)
    {
        var posList = ringContainer.Descendants(GmlNs + "posList").FirstOrDefault();
        if (posList is not null)
            return ParsePosList(posList.Value);

        var builder = ImmutableArray.CreateBuilder<(double, double)>();
        foreach (var pos in ringContainer.Descendants(GmlNs + "pos"))
        {
            var coord = ParsePos(pos.Value);
            if (coord is not null)
                builder.Add(coord.Value);
        }
        return builder.ToImmutable();
    }

    private static (double Latitude, double Longitude)? ParsePos(string posValue)
    {
        var parts = posValue.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
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
        var parts = posListValue.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
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

    private static (ImmutableDictionary<string, string>, ImmutableArray<S411ComplexAttribute>) ParseAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S411ComplexAttribute>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNs ||
                child.Name.Namespace == s100Ns)
                continue;

            if (child.HasElements)
            {
                var subAttrs = ImmutableDictionary.CreateBuilder<string, string>();
                foreach (var sub in child.Elements())
                {
                    if (!sub.HasElements && !string.IsNullOrEmpty(sub.Value))
                    {
                        subAttrs[sub.Name.LocalName] = sub.Value;
                    }
                }
                if (subAttrs.Count > 0)
                {
                    complex.Add(new S411ComplexAttribute
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
}
