using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Gml;
using S100Diag = EncDotNet.S100.Datasets.S122.Diagnostics;

namespace EncDotNet.S100.Datasets.S122;

/// <summary>
/// Reads an S-122 GML encoded dataset (S-100 Part 10b) into an <see cref="S122Dataset"/>.
/// </summary>
internal static class S122DatasetReader
{
    // S-100 Part 10b GML namespace.
    private static readonly XNamespace GmlNs = "http://www.opengis.net/gml/3.2";

    // The s100gml namespace varies between releases of S-100 Part 10b
    // (and between official S-122 sample releases). Accept any of the
    // commonly observed forms and fall back to scanning the root for
    // an explicit declaration.
    private static readonly XNamespace[] CandidateS100Namespaces =
    [
        "http://www.iho.int/s100gml/1.0",
        "http://www.iho.int/S100/profile/s100gml/1.0",
        "http://www.iho.int/s100gml/5.0",
    ];

    // S-122 feature type codes (per FC 2.0.0, S-122 § Feature Catalogue).
    private static readonly HashSet<string> FeatureTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "DataCoverage",
        "InformationArea",
        "MarineProtectedArea",
        "RestrictedArea",
        "VesselTrafficServiceArea",
        "QualityOfNonBathymetricData",
        "TextPlacement",
    };

    // S-122 information type codes (per FC 2.0.0).
    private static readonly HashSet<string> InformationTypeCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AbstractRxN",
        "Applicability",
        "Authority",
        "ContactDetails",
        "NauticalInformation",
        "NonStandardWorkingDay",
        "Recommendations",
        "Regulations",
        "Restrictions",
        "ServiceHours",
        "SpatialQuality",
    };

    public static S122Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-122");
        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-122 GML document has no root element.");

        // Detect the dataset namespace from the root element.
        var datasetNs = root.Name.Namespace;
        // Detect the s100gml namespace actually declared on this document.
        var s100Ns = DetectS100Namespace(root);

        // Parse dataset identification.
        string? productId = null;
        string? datasetId = root.Attribute(GmlNs + "id")?.Value;

        var dsInfo = root.Element(s100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(s100Ns + "productIdentifier")?.Value;
        }

        // Parse features and information types.
        // S-122 GML can use either repeated <member>/<imember> wrappers (each
        // containing one feature) or a single <members>/<imembers> container
        // holding all features as direct children. Walk descendants of the
        // root and collect anything whose local name matches a known type.
        var features = ImmutableArray.CreateBuilder<S122Feature>();
        var informationTypes = ImmutableArray.CreateBuilder<S122InformationType>();

        foreach (var element in root.Descendants())
        {
            var name = element.Name;
            if (IsFeatureType(name, datasetNs))
            {
                var feature = ParseFeature(element, s100Ns);
                if (feature is not null)
                    features.Add(feature);
            }
            else if (IsInformationType(name, datasetNs))
            {
                var info = ParseInformationType(element, s100Ns);
                if (info is not null)
                    informationTypes.Add(info);
            }
        }

        // Some real-world S-122 datasets (e.g. the UK trial dataset
        // GBNPI12200002045) violate the S-100 Part 10b spec by emitting
        // <gml:posList> values in lon-lat order even though the spec
        // mandates lat-lon for EPSG:4326. Detect this by sampling parsed
        // coordinates against the dataset's bounding envelope (which we
        // observe is consistently lat-lon) and rebuild features with
        // swapped axes when the lon-lat interpretation clearly fits better.
        var envelope = ParseEnvelope(root);
        if (envelope is not null && ShouldSwapAxes(features, envelope.Value))
        {
            var swapped = ImmutableArray.CreateBuilder<S122Feature>();
            foreach (var f in features)
                swapped.Add(SwapFeatureAxes(f));
            features = swapped;
        }

        return new S122Dataset
        {
            ProductIdentifier = productId ?? "S-122",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
            InformationTypes = informationTypes.ToImmutable(),
        };
    }

    private static (double MinLat, double MinLon, double MaxLat, double MaxLon)? ParseEnvelope(XElement root)
    {
        var envelope = root.Descendants(GmlNs + "Envelope").FirstOrDefault();
        if (envelope is null) return null;

        var lower = envelope.Element(GmlNs + "lowerCorner")?.Value;
        var upper = envelope.Element(GmlNs + "upperCorner")?.Value;
        if (lower is null || upper is null) return null;

        var lo = ParsePos(lower);
        var hi = ParsePos(upper);
        if (lo is null || hi is null) return null;

        // Validate corners are plausible lat-lon values; otherwise the
        // envelope itself may use a non-spec axis order and we can't trust
        // it as ground truth.
        if (Math.Abs(lo.Value.Latitude) > 90 || Math.Abs(hi.Value.Latitude) > 90 ||
            Math.Abs(lo.Value.Longitude) > 180 || Math.Abs(hi.Value.Longitude) > 180)
            return null;

        return (
            Math.Min(lo.Value.Latitude, hi.Value.Latitude),
            Math.Min(lo.Value.Longitude, hi.Value.Longitude),
            Math.Max(lo.Value.Latitude, hi.Value.Latitude),
            Math.Max(lo.Value.Longitude, hi.Value.Longitude));
    }

    private static bool ShouldSwapAxes(
        IEnumerable<S122Feature> features,
        (double MinLat, double MinLon, double MaxLat, double MaxLon) env)
    {
        // Pad the envelope slightly (~5%) to absorb minor producer rounding.
        var latPad = Math.Max(0.001, (env.MaxLat - env.MinLat) * 0.05);
        var lonPad = Math.Max(0.001, (env.MaxLon - env.MinLon) * 0.05);
        double minLat = env.MinLat - latPad, maxLat = env.MaxLat + latPad;
        double minLon = env.MinLon - lonPad, maxLon = env.MaxLon + lonPad;

        int asIs = 0, swapped = 0, total = 0;
        foreach (var f in features)
        {
            foreach (var p in EnumerateCoords(f))
            {
                total++;
                if (p.lat >= minLat && p.lat <= maxLat && p.lon >= minLon && p.lon <= maxLon)
                    asIs++;
                if (p.lon >= minLat && p.lon <= maxLat && p.lat >= minLon && p.lat <= maxLon)
                    swapped++;
            }
        }

        if (total == 0) return false;
        // Swap only when the as-is interpretation is clearly wrong and the
        // swapped one is clearly right.
        return asIs * 4 < total && swapped * 4 > total * 3;
    }

    private static IEnumerable<(double lat, double lon)> EnumerateCoords(S122Feature f)
    {
        foreach (var p in f.Points) yield return p;
        foreach (var c in f.Curves)
            foreach (var p in c) yield return p;
        foreach (var p in f.ExteriorRing) yield return p;
        foreach (var ring in f.InteriorRings)
            foreach (var p in ring) yield return p;
    }

    private static S122Feature SwapFeatureAxes(S122Feature f) => new()
    {
        Id = f.Id,
        FeatureType = f.FeatureType,
        GeometryType = f.GeometryType,
        Points = SwapMany(f.Points),
        Curves = SwapRings(f.Curves),
        ExteriorRing = SwapMany(f.ExteriorRing),
        InteriorRings = SwapRings(f.InteriorRings),
        Attributes = f.Attributes,
        ComplexAttributes = f.ComplexAttributes,
    };

    private static ImmutableArray<(double, double)> SwapMany(ImmutableArray<(double, double)> src)
    {
        if (src.IsDefaultOrEmpty) return src;
        var b = ImmutableArray.CreateBuilder<(double, double)>(src.Length);
        foreach (var (a, c) in src) b.Add((c, a));
        return b.ToImmutable();
    }

    private static ImmutableArray<ImmutableArray<(double, double)>> SwapRings(
        ImmutableArray<ImmutableArray<(double, double)>> src)
    {
        if (src.IsDefaultOrEmpty) return src;
        var b = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>(src.Length);
        foreach (var ring in src) b.Add(SwapMany(ring));
        return b.ToImmutable();
    }

    private static XNamespace DetectS100Namespace(XElement root)
    {
        // Look for any in-scope namespace declaration whose URI matches a
        // known s100gml release.
        foreach (var attr in root.Attributes())
        {
            if (!attr.IsNamespaceDeclaration) continue;
            foreach (var candidate in CandidateS100Namespaces)
            {
                if (string.Equals(attr.Value, candidate.NamespaceName, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            // Heuristic: any "s100gml" URI declared on the document.
            if (attr.Value.Contains("s100gml", StringComparison.OrdinalIgnoreCase))
                return attr.Value;
        }

        // Fallback: search descendants for a known S-100 element.
        foreach (var candidate in CandidateS100Namespaces)
        {
            if (root.Descendants(candidate + "DatasetIdentificationInformation").Any())
                return candidate;
        }

        return CandidateS100Namespaces[0];
    }

    private static S122Feature? ParseFeature(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var featureType = element.Name.LocalName;

        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element, s100Ns);
        var (simpleAttrs, complexAttrs) = ParseAttributes(element, s100Ns);

        return new S122Feature
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

    private static S122InformationType? ParseInformationType(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNs + "id")?.Value ?? "";
        var typeCode = element.Name.LocalName;

        var (simpleAttrs, complexAttrs) = ParseAttributes(element, s100Ns);

        return new S122InformationType
        {
            Id = id,
            TypeCode = typeCode,
            Attributes = simpleAttrs,
            ComplexAttributes = complexAttrs,
        };
    }

    private static (GmlGeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGeometry(XElement featureElement, XNamespace s100Ns)
    {
        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var geometryType = GmlGeometryType.None;

        // Look for geometry in the "geometry" child element or directly under the feature.
        var geometryContainer = featureElement.Element(featureElement.Name.Namespace + "geometry")
            ?? featureElement.Element("geometry");

        if (geometryContainer is null)
            return (geometryType, points, curves, exteriorRing, interiorRings);

        // S-100 Part 10b point property.
        var pointProp = geometryContainer.Element(s100Ns + "pointProperty")
            ?? geometryContainer.Element(s100Ns + "Point");
        if (pointProp is not null)
        {
            var pointCoords = ParsePointElement(pointProp, s100Ns);
            if (pointCoords is not null)
            {
                geometryType = GmlGeometryType.Point;
                points = [pointCoords.Value];
            }
            else
            {
                var gmlPoint = pointProp.Descendants(GmlNs + "Point").FirstOrDefault()
                    ?? pointProp.Descendants(GmlNs + "pos").FirstOrDefault()?.Parent;
                if (gmlPoint is not null)
                {
                    var coord = ParseGmlPoint(gmlPoint);
                    if (coord is not null)
                    {
                        geometryType = GmlGeometryType.Point;
                        points = [coord.Value];
                    }
                }
            }
        }

        // S-100 Part 10b curve property.
        var curveProp = geometryContainer.Element(s100Ns + "curveProperty");
        if (curveProp is not null)
        {
            geometryType = GmlGeometryType.Curve;
            var curveBuilder = ImmutableArray.CreateBuilder<ImmutableArray<(double, double)>>();
            var coords = ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
                curveBuilder.Add(coords);
            curves = curveBuilder.ToImmutable();
        }

        // S-100 Part 10b surface property.
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

    private static (double Latitude, double Longitude)? ParsePointElement(XElement element, XNamespace s100Ns)
    {
        var pos = element.Element(GmlNs + "Point")?.Element(GmlNs + "pos")
            ?? element.Element(GmlNs + "pos");

        if (pos is not null)
            return ParsePos(pos.Value);

        var s100Point = element.Element(s100Ns + "Point");
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

    // Tokenises a coordinate string. Per GML 3.2 <gml:posList>/<gml:pos>
    // numbers are whitespace-separated, but some real-world S-122 producers
    // (e.g. UKHO trial datasets) emit lon,lat tuples with commas inside a
    // tuple and whitespace between tuples — i.e. the gml:coordinates
    // convention smuggled into a posList element. Treating both whitespace
    // and commas as delimiters handles either shape transparently.
    private static readonly char[] CoordSeparators = [' ', '\t', '\n', '\r', ','];

    private static (double Latitude, double Longitude)? ParsePos(string posValue)
    {
        var parts = posValue.Split(CoordSeparators, StringSplitOptions.RemoveEmptyEntries);
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
        var parts = posListValue.Split(CoordSeparators, StringSplitOptions.RemoveEmptyEntries);
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

    private static (ImmutableDictionary<string, string>, ImmutableArray<S122ComplexAttribute>) ParseAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S122ComplexAttribute>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip geometry, GML id, and S-100 infrastructure elements.
            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNs ||
                child.Name.Namespace == s100Ns)
                continue;

            if (child.HasElements)
            {
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
                    complex.Add(new S122ComplexAttribute
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

    private static bool IsInformationType(XName name, XNamespace datasetNs)
    {
        return (name.Namespace == datasetNs || name.Namespace == XNamespace.None) &&
               InformationTypeCodes.Contains(name.LocalName);
    }
}
