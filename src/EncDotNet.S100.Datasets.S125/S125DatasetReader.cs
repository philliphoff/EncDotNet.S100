using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Gml;
using S100Diag = EncDotNet.S100.Datasets.S125.Diagnostics;

namespace EncDotNet.S100.Datasets.S125;

/// <summary>
/// Reads an S-125 Marine Aids to Navigation GML encoded dataset
/// (S-100 Part 10b) into an <see cref="S125Dataset"/>.
/// </summary>
/// <remarks>
/// The S-125 1.0.0 application schema declares its target namespace as
/// <c>http://www.iho.int/S125/1.0</c> and imports geometry from the S-100
/// GML 5.0 profile (<c>http://www.iho.int/s100gml/5.0</c>). This reader is
/// tolerant of the older S-100 GML 1.0 profile namespaces still seen in
/// some sample datasets and of <c>S100:</c>-prefixed bare-Dataset roots
/// that carry the spec only via the <c>productIdentifier</c> element.
/// </remarks>
internal static class S125DatasetReader
{
    private static readonly XNamespace GmlNs = "http://www.opengis.net/gml/3.2";
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";

    private static readonly XNamespace S100Ns_5_0 = "http://www.iho.int/s100gml/5.0";
    private static readonly XNamespace S100Ns_1_0_lower = "http://www.iho.int/s100gml/1.0";
    private static readonly XNamespace S100Ns_1_0_profile = "http://www.iho.int/S100/profile/s100gml/1.0";

    private static readonly XNamespace[] CandidateS100Namespaces =
        [S100Ns_5_0, S100Ns_1_0_profile, S100Ns_1_0_lower];

    // S-125 Edition 1.0.0 Feature Catalogue — concrete feature type codes (PascalCase form).
    private static readonly HashSet<string> FeatureTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AtonAggregation", "AtonAssociation", "AtonStatusIndication",
        "CardinalBeacon", "CardinalBuoy",
        "DangerousFeature", "DataCoverage", "Daymark",
        "EmergencyWreckMarkingBuoy",
        "FogSignal",
        "InstallationBuoy", "IsolatedDangerBeacon", "IsolatedDangerBuoy",
        "Landmark", "LateralBeacon", "LateralBuoy",
        "LightAirObstruction", "LightAllAround", "LightFloat", "LightFogDetector",
        "LightSectored", "LightVessel", "LocalDirectionOfBuoyage",
        "MooringBuoy",
        "NavigationLine", "NavigationalSystemOfMarks",
        "OffshorePlatform",
        "PhysicalAISAidToNavigation", "Pile",
        "QualityOfBathymetricData",
        "RadarReflector", "RadarTransponderBeacon", "RadioStation",
        "RecommendedTrack", "Retroreflector",
        "SafeWaterBeacon", "SafeWaterBuoy", "SiloTank",
        "SoundingDatum", "SpecialPurposeGeneralBeacon", "SpecialPurposeGeneralBuoy",
        "SyntheticAISAidToNavigation",
        "Topmark",
        "VerticalDatumOfData", "VirtualAISAidToNavigation",
        "WindTurbine",
    };

    // S-125 Edition 1.0.0 Feature Catalogue — concrete information type codes.
    private static readonly HashSet<string> InformationTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AtonStatusInformation", "SpatialQuality",
    };

    public static S125Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-125");
        // Tolerate leading whitespace/BOM before the XML declaration.
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd().TrimStart();
        var doc = XDocument.Parse(xml);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-125 GML document has no root element.");

        var datasetNs = root.Name.Namespace;
        var s100Ns = DetectS100Namespace(root);

        string? datasetId = root.Attribute(GmlNs + "id")?.Value;
        string? productId = null;

        var dsInfo = root.Element(s100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(s100Ns + "productIdentifier")?.Value;
        }

        var features = ImmutableArray.CreateBuilder<S125Feature>();
        foreach (var member in EnumerateChildren(root, datasetNs, "member"))
        {
            foreach (var element in member.Elements())
            {
                if (!IsFeatureType(element.Name, datasetNs)) continue;
                features.Add(ParseFeature(element, s100Ns));
            }
        }

        var informationTypes = ImmutableArray.CreateBuilder<S125InformationType>();
        foreach (var imember in EnumerateChildren(root, datasetNs, "imember"))
        {
            foreach (var element in imember.Elements())
            {
                if (!IsInformationType(element.Name, datasetNs)) continue;
                informationTypes.Add(ParseInformationType(element, s100Ns));
            }
        }

        return new S125Dataset
        {
            ProductIdentifier = productId ?? "S-125",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
            InformationTypes = informationTypes.ToImmutable(),
        };
    }

    private static IEnumerable<XElement> EnumerateChildren(XElement root, XNamespace datasetNs, string localName)
    {
        // Try dataset-namespaced first, then unnamespaced.
        foreach (var el in root.Elements(datasetNs + localName))
            yield return el;
        if (datasetNs != XNamespace.None)
        {
            foreach (var el in root.Elements((XName)localName))
                yield return el;
        }
    }

    private static XNamespace DetectS100Namespace(XElement root)
    {
        foreach (var element in root.DescendantsAndSelf())
        {
            foreach (var attr in element.Attributes())
            {
                if (!attr.IsNamespaceDeclaration) continue;
                foreach (var candidate in CandidateS100Namespaces)
                {
                    if (attr.Value == candidate.NamespaceName)
                        return candidate;
                }
            }
        }
        return S100Ns_5_0;
    }

    private static S125Feature ParseFeature(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNs + "id")?.Value
            ?? element.Attribute("id")?.Value
            ?? "";
        var featureType = element.Name.LocalName;

        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element, s100Ns);
        var (simpleAttrs, complexAttrs, infoRefs) = ParseAttributes(element, s100Ns);

        return new S125Feature
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
            InformationReferences = infoRefs,
        };
    }

    private static S125InformationType ParseInformationType(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNs + "id")?.Value
            ?? element.Attribute("id")?.Value
            ?? "";
        var typeCode = element.Name.LocalName;

        var (simpleAttrs, complexAttrs, _) = ParseAttributes(element, s100Ns);

        return new S125InformationType
        {
            Id = id,
            TypeCode = typeCode,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
        };
    }

    // ── Geometry ───────────────────────────────────────────────────────

    private static (GmlGeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement, XNamespace s100Ns)
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

        var pointProp = geometryContainer.Element(s100Ns + "pointProperty")
            ?? geometryContainer.Element(s100Ns + "Point");
        if (pointProp is not null)
        {
            var coord = ParseGmlPointCoord(pointProp);
            if (coord is not null)
            {
                geometryType = GmlGeometryType.Point;
                points = [coord.Value];
            }
        }

        var curveProp = geometryContainer.Element(s100Ns + "curveProperty");
        if (curveProp is not null)
        {
            geometryType = GmlGeometryType.Curve;
            var coords = ParseCurveCoordinates(curveProp);
            curves = coords.Length > 0
                ? ImmutableArray.Create(coords)
                : ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        }

        var surfaceProp = geometryContainer.Element(s100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = GmlGeometryType.Surface;
            var (ext, intRings) = ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static (double Latitude, double Longitude)? ParseGmlPointCoord(XElement element)
    {
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
        if (exterior is not null)
            exteriorRing = ParseRing(exterior);

        foreach (var interior in surfaceContainer.Descendants(GmlNs + "interior"))
        {
            var ring = ParseRing(interior);
            if (ring.Length > 0) interiorRings.Add(ring);
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
            if (coord is not null) builder.Add(coord.Value);
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

    // ── Attributes ─────────────────────────────────────────────────────

    private static (ImmutableDictionary<string, string>, ImmutableArray<S125ComplexAttribute>, ImmutableArray<S125InformationReference>) ParseAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S125ComplexAttribute>();
        var infoRefs = ImmutableArray.CreateBuilder<S125InformationReference>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNs ||
                child.Name.Namespace == s100Ns)
                continue;

            // Information binding: child carries an xlink:href or informationRef
            // pointing at an imember's gml:id. (S-125 PC's AtoNStatus association.)
            var href = child.Attribute(XlinkNs + "href")?.Value
                ?? child.Attribute("informationRef")?.Value;
            if (!string.IsNullOrEmpty(href) && !child.HasElements && string.IsNullOrEmpty(child.Value))
            {
                infoRefs.Add(new S125InformationReference
                {
                    Role = localName,
                    InformationRef = href.TrimStart('#'),
                });
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
                    complex.Add(new S125ComplexAttribute
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

        return (simple.ToImmutable(), complex.ToImmutable(), infoRefs.ToImmutable());
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
