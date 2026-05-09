using System.Collections.Immutable;
using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Gml;
using S100Diag = EncDotNet.S100.Datasets.S411.Diagnostics;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// Reads an S-411 Sea Ice GML encoded dataset (S-100 Part 10b) into an
/// <see cref="S411Dataset"/>.
/// </summary>
/// <remarks>
/// <para>The reader handles two distinct dataset shapes that S-411
/// producers emit in practice:</para>
/// <list type="number">
/// <item>
/// <description>
/// <b>JCOMM operational shape</b> (e.g. Canadian Ice Service feeds): root
/// <c>&lt;ice:IceDataSet xmlns:ice="http://www.jcomm.info/ice"&gt;</c>,
/// each feature wrapped in an <c>&lt;ice:IceFeatureMember&gt;</c> with the
/// feature element using the lowercase short code (<c>ice:seaice</c>,
/// <c>ice:icebrg</c>, ...). Geometry appears as a direct GML primitive
/// child (<c>gml:Polygon</c>, <c>gml:LineString</c>, <c>gml:Point</c>).
/// Attributes are simple element values such as <c>&lt;ice:iceact&gt;91&lt;/ice:iceact&gt;</c>.
/// This shape matches what the official S-411 1.2.1 portrayal catalogue
/// templates were authored for.
/// </description>
/// </item>
/// <item>
/// <description>
/// <b>IHO 1.2.1 sample shape</b> (the GMLs under <c>src/Documents/1.2.1/samples/</c>
/// in the IHO repository): bare <c>&lt;Dataset&gt;</c> root declaring only
/// <c>xmlns:S100="http://www.iho.int/s100gml/5.0"</c>, with features
/// grouped under a <c>&lt;members&gt;</c> wrapper and named with
/// PascalCase Feature Catalogue class names (<c>SeaIce</c>, <c>Iceberg</c>,
/// ...). Geometry is wrapped in a <c>geometry/&lt;...Property&gt;</c>
/// container. This shape is recognised but cannot be portrayed by the
/// official catalogue (its templates target the JCOMM shape).
/// </description>
/// </item>
/// </list>
/// </remarks>
internal static class S411DatasetReader
{
    private static readonly XNamespace IceNs = "http://www.jcomm.info/ice";

    private static readonly XNamespace S100Ns_1_0_lower = "http://www.iho.int/s100gml/1.0";
    private static readonly XNamespace S100Ns_1_0_profile = "http://www.iho.int/S100/profile/s100gml/1.0";
    private static readonly XNamespace S100Ns_5_0 = "http://www.iho.int/s100gml/5.0";

    // S-411 Edition 1.2.1 Feature Catalogue feature types (PascalCase form).
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
        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.dataset.open");
        __activity?.SetTag("s100.product", "S-411");
        // Tolerate leading whitespace before the XML declaration.
        using var reader = new StreamReader(stream);
        var xml = reader.ReadToEnd().TrimStart();
        var doc = XDocument.Parse(xml);
        var root = doc.Root
            ?? throw new InvalidOperationException("S-411 GML document has no root element.");

        return root.Name.Namespace == IceNs
            ? ReadIceDataSet(doc, root)
            : ReadGenericDataset(doc, root);
    }

    // ── JCOMM ice:IceDataSet shape ─────────────────────────────────────

    private static S411Dataset ReadIceDataSet(XDocument doc, XElement root)
    {
        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;
        var issueDate = ParseIceIssueDate(root);

        var features = ImmutableArray.CreateBuilder<S411Feature>();

        // Operational S-411 producers (e.g. the Canadian Ice Service) frequently
        // emit every feature with the same gml:id ("seaice.None"). The geometry
        // provider keys on Feature.Id, so collisions cause every drawing
        // instruction to resolve to the first feature's geometry. Rewrite each
        // feature's gml:id to a sequential synthetic id before parsing so the
        // SourceDocument (which the XSLT pipeline transforms) and the parsed
        // S411Feature.Id agree on unique values.
        var idCounter = 0;
        foreach (var member in root.Elements(IceNs + "IceFeatureMember"))
        {
            foreach (var featureEl in member.Elements())
            {
                if (featureEl.Name.Namespace != IceNs) continue;

                var syntheticId = string.Format(
                    CultureInfo.InvariantCulture, "{0}.{1:0000}", featureEl.Name.LocalName, ++idCounter);
                featureEl.SetAttributeValue(GmlNamespaces.Gml + "id", syntheticId);

                features.Add(ParseIceFeature(featureEl));
            }
        }

        return new S411Dataset
        {
            ProductIdentifier = "S-411",
            DatasetIdentifier = datasetId,
            IssueDate = issueDate,
            Features = features.ToImmutable(),
            SourceDocument = doc,
        };
    }

    private static DateTime? ParseIceIssueDate(XElement root)
    {
        // Probe well-known JCOMM/CIS timestamp element names, in order of
        // specificity. Datetime variants take precedence over date-only.
        string[] candidates = ["issueDateTime", "issueDate", "observationDateTime", "observationDate"];
        foreach (var local in candidates)
        {
            var el = root.Element(IceNs + local);
            if (el is null || string.IsNullOrWhiteSpace(el.Value)) continue;
            if (TryParseDateTime(el.Value, out var dt))
                return dt;
        }
        return null;
    }

    private static S411Feature ParseIceFeature(XElement element)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value ?? "";
        var featureType = element.Name.LocalName;

        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseInlineGmlGeometry(element);
        var simple = ImmutableDictionary.CreateBuilder<string, string>();

        foreach (var child in element.Elements())
        {
            if (child.Name.Namespace == GmlNamespaces.Gml) continue;
            if (child.Name.Namespace != IceNs) continue;
            if (child.HasElements) continue;
            if (string.IsNullOrEmpty(child.Value)) continue;
            simple[child.Name.LocalName] = child.Value;
        }

        return new S411Feature
        {
            Id = id,
            FeatureType = featureType,
            GeometryType = geometryType,
            Points = points,
            Curves = curves,
            ExteriorRing = exteriorRing,
            InteriorRings = interiorRings,
            Attributes = simple.ToImmutable(),
            ComplexAttributes = ImmutableArray<S411ComplexAttribute>.Empty,
        };
    }

    private static (GmlGeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseInlineGmlGeometry(XElement element)
    {
        var points = ImmutableArray<(double, double)>.Empty;
        var curves = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var exteriorRing = ImmutableArray<(double, double)>.Empty;
        var interiorRings = ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        var geometryType = GmlGeometryType.None;

        var polygon = element.Element(GmlNamespaces.Gml + "Polygon");
        if (polygon is not null)
        {
            var (ext, intRings) = GmlCoordinateParser.ParseSurfaceCoordinates(polygon);
            return (GmlGeometryType.Surface, points, curves, ext, intRings);
        }

        var lineString = element.Element(GmlNamespaces.Gml + "LineString") ?? element.Element(GmlNamespaces.Gml + "Curve");
        if (lineString is not null)
        {
            var coords = GmlCoordinateParser.ParseCurveCoordinates(lineString);
            curves = coords.Length > 0
                ? ImmutableArray.Create(coords)
                : ImmutableArray<ImmutableArray<(double, double)>>.Empty;
            return (GmlGeometryType.Curve, points, curves, exteriorRing, interiorRings);
        }

        var point = element.Element(GmlNamespaces.Gml + "Point");
        if (point is not null)
        {
            var coord = GmlCoordinateParser.ParsePointElement(point);
            if (coord is not null)
            {
                geometryType = GmlGeometryType.Point;
                points = [coord.Value];
            }
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    // ── Generic / IHO-sample bare-Dataset shape ────────────────────────

    private static S411Dataset ReadGenericDataset(XDocument doc, XElement root)
    {
        var datasetNs = root.Name.Namespace;
        var s100Ns = DetectS100Namespace(root);

        string? datasetId = root.Attribute(GmlNamespaces.Gml + "id")?.Value;
        string? productId = null;
        DateTime? issueDate = null;

        var dsInfo = root.Element(s100Ns + "DatasetIdentificationInformation");
        if (dsInfo is not null)
        {
            productId = dsInfo.Element(s100Ns + "productIdentifier")?.Value;
            // S-100 Part 17 dataset identification metadata (encoded per
            // Part 10b §C.4); a date-only or a full xs:dateTime are both
            // accepted by the spec.
            var refDate = dsInfo.Element(s100Ns + "datasetReferenceDate")?.Value;
            if (!string.IsNullOrWhiteSpace(refDate) && TryParseDateTime(refDate, out var dt))
                issueDate = dt;
        }

        var features = ImmutableArray.CreateBuilder<S411Feature>();
        foreach (var memberContainer in EnumerateMembers(root, datasetNs))
        {
            foreach (var element in memberContainer.Elements())
            {
                if (!IsFeatureType(element.Name, datasetNs)) continue;
                features.Add(ParseGenericFeature(element, s100Ns));
            }
        }

        return new S411Dataset
        {
            ProductIdentifier = productId ?? "S-411",
            DatasetIdentifier = datasetId,
            IssueDate = issueDate,
            Features = features.ToImmutable(),
            SourceDocument = doc,
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

    private static S411Feature ParseGenericFeature(XElement element, XNamespace s100Ns)
    {
        var id = element.Attribute(GmlNamespaces.Gml + "id")?.Value
            ?? element.Attribute("id")?.Value
            ?? "";
        var featureType = element.Name.LocalName;

        var (geometryType, points, curves, exteriorRing, interiorRings) = ParseGenericGeometry(element, s100Ns);
        var (simpleAttrs, complexAttrs) = ParseGenericAttributes(element, s100Ns);

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

    private static (GmlGeometryType, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>, ImmutableArray<(double, double)>, ImmutableArray<ImmutableArray<(double, double)>>) ParseGenericGeometry(XElement featureElement, XNamespace s100Ns)
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
            var coord = GmlCoordinateParser.ParsePointElement(pointProp);
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
            var coords = GmlCoordinateParser.ParseCurveCoordinates(curveProp);
            curves = coords.Length > 0
                ? ImmutableArray.Create(coords)
                : ImmutableArray<ImmutableArray<(double, double)>>.Empty;
        }

        var surfaceProp = geometryContainer.Element(s100Ns + "surfaceProperty");
        if (surfaceProp is not null)
        {
            geometryType = GmlGeometryType.Surface;
            var (ext, intRings) = GmlCoordinateParser.ParseSurfaceCoordinates(surfaceProp);
            exteriorRing = ext;
            interiorRings = intRings;
        }

        return (geometryType, points, curves, exteriorRing, interiorRings);
    }

    private static (ImmutableDictionary<string, string>, ImmutableArray<S411ComplexAttribute>) ParseGenericAttributes(XElement element, XNamespace s100Ns)
    {
        var simple = ImmutableDictionary.CreateBuilder<string, string>();
        var complex = ImmutableArray.CreateBuilder<S411ComplexAttribute>();

        foreach (var child in element.Elements())
        {
            var localName = child.Name.LocalName;

            if (localName is "geometry" or "boundedBy" ||
                child.Name.Namespace == GmlNamespaces.Gml ||
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
    }    /// <summary>
    /// Parses an xs:dateTime or xs:date timestamp into UTC, accepting any
    /// of the formats S-100 Part 17 / Part 10b allow for dataset metadata.
    /// </summary>
    private static bool TryParseDateTime(string value, out DateTime result)
    {
        var trimmed = value.Trim();
        if (DateTime.TryParse(
                trimmed,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out result))
        {
            return true;
        }
        result = default;
        return false;
    }
}
