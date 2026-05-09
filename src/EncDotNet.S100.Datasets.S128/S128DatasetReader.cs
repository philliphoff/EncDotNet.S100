using System.Collections.Immutable;
using System.Xml.Linq;
using S100Diag = EncDotNet.S100.Datasets.S128.Diagnostics;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// Reads an S-128 (Catalogue of Nautical Products) GML encoded dataset
/// (S-100 Part 10b) into an <see cref="S128Dataset"/>.
/// </summary>
/// <remarks>
/// <para>
/// The reader follows the same parsing strategy as
/// <c>EncDotNet.S100.Datasets.S122</c> and inherits the four producer-bug
/// compensations documented there:
/// </para>
/// <list type="number">
/// <item><description>S-100 GML namespace tolerance (1.0 / profile-1.0 / 5.0 plus an in-document scan).</description></item>
/// <item><description>Acceptance of both <c>&lt;member&gt;/&lt;imember&gt;</c> per-feature wrappers and inline <c>&lt;members&gt;/&lt;imembers&gt;</c> containers.</description></item>
/// <item><description>Comma-and-whitespace tokenisation in <c>gml:posList</c> and <c>gml:pos</c>.</description></item>
/// <item><description>Lon-lat axis-order swap when the parsed coordinates clearly fall outside the dataset's <c>gml:Envelope</c> but their swapped form fits.</description></item>
/// </list>
/// <para>
/// The S-128 2.0.0 sample dataset uses inline <c>&lt;S128:members&gt;</c>
/// and emits all catalogue records (including
/// <c>DistributorInformation</c>, <c>ProducerInformation</c>, etc.)
/// inside that container. The upstream Portrayal Catalogue's
/// <c>main.xsl</c> walks <c>Dataset/Features/*</c>, so the reader treats
/// every <c>&lt;members&gt;</c> child as a feature to keep portrayal
/// behaviour aligned with the upstream PC.
/// </para>
/// </remarks>
internal static class S128DatasetReader
{
    private static readonly XNamespace XlinkNs = "http://www.w3.org/1999/xlink";

    private static readonly XNamespace[] CandidateS100Namespaces =
    [
        "http://www.iho.int/s100gml/5.0",
        "http://www.iho.int/s100gml/1.0",
        "http://www.iho.int/S100/profile/s100gml/1.0",
    ];

    public static S128Dataset Read(Stream stream)
    {
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-128");
        var doc = XDocument.Load(stream);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-128 GML document has no root element.");

        var datasetNs = root.Name.Namespace;
        var s100Ns = DetectS100Namespace(root);

        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;
        string? productId = null;
        var dsInfo = root.Element(s100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(s100Ns + "productIdentifier")?.Value;
        }

        var features = ImmutableArray.CreateBuilder<S128Feature>();
        var infoTypes = ImmutableArray.CreateBuilder<S128InformationType>();

        // Walk every member/members child as a feature, every imember/imembers
        // child as an information type. We accept both per-element wrappers
        // (<member>/<imember>) and inline containers (<members>/<imembers>).
        foreach (var container in EnumerateMemberContainers(root, datasetNs))
        {
            var (isInfo, candidates) = container;
            foreach (var element in candidates)
            {
                if (!IsApplicationElement(element, datasetNs))
                    continue;

                if (isInfo)
                {
                    var info = ParseInformationType(element, s100Ns);
                    if (info is not null) infoTypes.Add(info);
                }
                else
                {
                    var feature = ParseFeature(element, s100Ns);
                    if (feature is not null) features.Add(feature);
                }
            }
        }

        // Apply the lon-lat axis-order producer-bug heuristic. S-128 2.0.0
        // datasets often omit a top-level <gml:Envelope>; in that case we
        // simply leave coordinates as-parsed (the spec is lat-lon).
        var envelope = ParseEnvelope(root);
        if (envelope is not null && ShouldSwapAxes(features, envelope.Value))
        {
            var swapped = ImmutableArray.CreateBuilder<S128Feature>(features.Count);
            foreach (var f in features) swapped.Add(SwapFeatureAxes(f));
            features = swapped;
        }

        return new S128Dataset
        {
            ProductIdentifier = productId ?? "S-128",
            DatasetIdentifier = datasetId,
            Features = features.ToImmutable(),
            InformationTypes = infoTypes.ToImmutable(),
        };
    }

    /// <summary>
    /// Returns all member/imember candidate elements found beneath the dataset
    /// root, accepting both the wrapper form (<c>&lt;member&gt;</c> with one
    /// child) and the inline-container form (<c>&lt;members&gt;</c> with many
    /// children).
    /// </summary>
    private static IEnumerable<(bool IsInfoType, IEnumerable<XElement> Candidates)>
        EnumerateMemberContainers(XElement root, XNamespace datasetNs)
    {
        // Inline containers: <members>, <imembers>. May appear under any
        // namespace (often the dataset's), so match by local name.
        foreach (var members in root.Descendants().Where(e => e.Name.LocalName == "members"))
            yield return (false, members.Elements());

        foreach (var imembers in root.Descendants().Where(e => e.Name.LocalName == "imembers"))
            yield return (true, imembers.Elements());

        // Per-feature wrappers: <member>, <imember>.
        foreach (var member in root.Descendants().Where(e => e.Name.LocalName == "member"))
            yield return (false, member.Elements());

        foreach (var imember in root.Descendants().Where(e => e.Name.LocalName == "imember"))
            yield return (true, imember.Elements());

        // Some datasets inline catalogue members directly under the root
        // (no wrapper). Surface direct children in the application namespace
        // that have a gml:id but were not yielded above. We only fall back
        // when no <members>/<member> container exists — otherwise we would
        // double-emit. The upstream sample uses <S128:members>, so this
        // branch is dormant for typical inputs.
        bool hasContainer = root.Descendants().Any(e =>
            e.Name.LocalName is "members" or "member" or "imembers" or "imember");
        if (!hasContainer)
        {
            yield return (false, root.Elements()
                .Where(e => e.Attribute(GmlNamespaces.Gml + "id") is not null
                            && (e.Name.Namespace == datasetNs || e.Name.Namespace == XNamespace.None)));
        }
    }

    private static bool IsApplicationElement(XElement element, XNamespace datasetNs)
    {
        // Skip GML / S100 framework elements; only application-namespace
        // (or unqualified) elements are catalogue members.
        if (element.Name.Namespace == GmlNamespaces.Gml) return false;
        if (CandidateS100Namespaces.Any(n => n == element.Name.Namespace)) return false;
        if (element.Name.NamespaceName.Contains("s100gml", StringComparison.OrdinalIgnoreCase)) return false;
        return element.Name.Namespace == datasetNs || element.Name.Namespace == XNamespace.None;
    }

    private static S128Feature ParseFeature(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGeometry(element, s100Ns);
        var (simple, complex) = ParseAttributes(element, s100Ns);
        var refs = ParseReferences(element);

        return new S128Feature
        {
            Id = id,
            FeatureType = element.Name.LocalName,
            GeometryType = geometryType,
            Points = points,
            Curves = curves,
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
            Attributes = simple,
            ComplexAttributes = complex,
            References = refs,
        };
    }

    private static S128InformationType ParseInformationType(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var (simple, complex) = ParseAttributes(element, s100Ns);
        return new S128InformationType
        {
            Id = id,
            TypeCode = element.Name.LocalName,
            Attributes = simple,
            ComplexAttributes = complex,
        };
    }

    // ── Geometry ────────────────────────────────────────────────────────

    private static (GmlGeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>)
        ParseGeometry(XElement featureElement, XNamespace s100Ns)
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

        // surfaceProperty (the dominant case for S-128).
        var surfaceProp = geometryContainer.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = GmlGeometryType.Surface;
            var (ext, intRings) = GmlCoordinateParser.ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        var curveProp = geometryContainer.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "curveProperty");
        if (curveProp is not null)
        {
            geometryType = GmlGeometryType.Curve;
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp);
            if (coords.Length > 0)
                curves = [coords];
        }

        var pointProp = geometryContainer.Descendants()
            .FirstOrDefault(e => e.Name.LocalName == "pointProperty");
        if (pointProp is not null)
        {
            var coord = GmlCoordinateParser.ParsePointElement(pointProp);
            if (coord is not null)
            {
                geometryType = GmlGeometryType.Point;
                points = [coord.Value];
            }
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }    // ── Attributes & references ─────────────────────────────────────────

    private static (ImmutableDictionary<string, string>, ImmutableArray<S128ComplexAttribute>)
        ParseAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S128ComplexAttribute>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            // Skip GML / S100 infrastructure and the geometry container.
            if (localName is "geometry" or "boundedBy") continue;
            if (child.Name.Namespace == GmlNamespaces.Gml) continue;
            if (CandidateS100Namespaces.Any(n => n == child.Name.Namespace)) continue;

            // Pure xlink-only references are surfaced via ParseReferences.
            // Skip elements that have no leaf text and no attribute children
            // beyond xlink, but only when they have no nested complex content.
            if (IsXlinkOnly(child)) continue;

            if (child.HasElements)
            {
                var attr = ParseComplex(child);
                if (attr is not null) complex.Add(attr);
            }
            else
            {
                var text = child.Value;
                if (!string.IsNullOrEmpty(text))
                    simple[localName] = text;
            }
        }

        return (simple.ToImmutable(), complex.ToImmutable());
    }

    private static S128ComplexAttribute? ParseComplex(XElement element)
    {
        var subAttrs = ImmutableDictionary.CreateBuilder<string, string>();
        var nested = ImmutableArray.CreateBuilder<S128ComplexAttribute>();

        foreach (var sub in element.Elements())
        {
            if (sub.Name.Namespace == GmlNamespaces.Gml) continue;
            if (CandidateS100Namespaces.Any(n => n == sub.Name.Namespace)) continue;

            if (sub.HasElements)
            {
                var nestedAttr = ParseComplex(sub);
                if (nestedAttr is not null) nested.Add(nestedAttr);
            }
            else if (!string.IsNullOrEmpty(sub.Value))
            {
                subAttrs[sub.Name.LocalName] = sub.Value;
            }
        }

        if (subAttrs.Count == 0 && nested.Count == 0)
            return null;

        return new S128ComplexAttribute
        {
            Code = element.Name.LocalName,
            SubAttributes = subAttrs.ToImmutable(),
            NestedAttributes = nested.ToImmutable(),
        };
    }

    private static bool IsXlinkOnly(XElement element)
    {
        if (element.HasElements) return false;
        if (!string.IsNullOrWhiteSpace(element.Value)) return false;
        var hasHref = element.Attribute(XlinkNs + "href") is not null
                      || element.Attribute("href") is not null;
        return hasHref;
    }

    private static ImmutableArray<S128XlinkReference> ParseReferences(XElement element)
    {
        var refs = ImmutableArray.CreateBuilder<S128XlinkReference>();
        foreach (var child in element.Elements())
        {
            var href = child.Attribute(XlinkNs + "href")?.Value
                       ?? child.Attribute("href")?.Value;
            if (string.IsNullOrEmpty(href)) continue;

            var arcrole = child.Attribute(XlinkNs + "arcrole")?.Value
                          ?? child.Attribute("arcrole")?.Value;

            var target = href.StartsWith('#') ? href[1..] : href;

            refs.Add(new S128XlinkReference(child.Name.LocalName, href, arcrole, target));
        }
        return refs.ToImmutable();
    }

    // ── Envelope / axis-swap heuristic ──────────────────────────────────

    private static (double MinLat, double MinLon, double MaxLat, double MaxLon)? ParseEnvelope(XElement root)
    {
        var envelope = root.Descendants(GmlNamespaces.Gml + "Envelope").FirstOrDefault();
        if (envelope is null) return null;

        var lower = envelope.Element(GmlNamespaces.Gml + "lowerCorner")?.Value;
        var upper = envelope.Element(GmlNamespaces.Gml + "upperCorner")?.Value;
        if (lower is null || upper is null) return null;

        var lo = GmlCoordinateParser.ParsePos(lower);
        var hi = GmlCoordinateParser.ParsePos(upper);
        if (lo is null || hi is null) return null;

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
        IEnumerable<S128Feature> features,
        (double MinLat, double MinLon, double MaxLat, double MaxLon) env)
    {
        var latPad = Math.Max(0.001, (env.MaxLat - env.MinLat) * 0.05);
        var lonPad = Math.Max(0.001, (env.MaxLon - env.MinLon) * 0.05);
        double minLat = env.MinLat - latPad, maxLat = env.MaxLat + latPad;
        double minLon = env.MinLon - lonPad, maxLon = env.MaxLon + lonPad;

        int asIs = 0, swapped = 0, total = 0;
        foreach (var f in features)
        {
            foreach (var (lat, lon) in EnumerateCoords(f))
            {
                total++;
                if (lat >= minLat && lat <= maxLat && lon >= minLon && lon <= maxLon) asIs++;
                if (lon >= minLat && lon <= maxLat && lat >= minLon && lat <= maxLon) swapped++;
            }
        }

        if (total == 0) return false;
        return asIs * 4 < total && swapped * 4 > total * 3;
    }

    private static IEnumerable<(double Lat, double Lon)> EnumerateCoords(S128Feature f)
    {
        foreach (var p in f.Points) yield return p;
        foreach (var c in f.Curves) foreach (var p in c) yield return p;
        foreach (var p in f.ExteriorRing) yield return p;
        foreach (var ring in f.InteriorRings) foreach (var p in ring) yield return p;
    }

    private static S128Feature SwapFeatureAxes(S128Feature f) => new()
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
        References = f.References,
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

    // ── S100 namespace detection ────────────────────────────────────────

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
}
