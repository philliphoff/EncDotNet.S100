using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Projects an <see cref="S101Dataset"/> into S-100 Part 9 FeatureXML
/// for consumption by the XSLT/Lua portrayal pipeline.
/// </summary>
public sealed class S101FeatureXmlSource : IFeatureXmlSource
{
    private const byte RcnmPoint = 110;
    private const byte RcnmCurveSegment = 120;
    private const byte RcnmCompositeCurve = 125;
    private const byte RcnmSurface = 130;
    private const byte OrientationReverse = 2;
    private const byte UsageExterior = 1;
    private const byte UsageInterior = 2;

    private static readonly XNamespace S100Ns = "http://www.iho.int/s100/5.0";

    // Cached XName instances. These are constructed once at class init
    // and reused across every feature in every dataset for the lifetime
    // of the AppDomain. Per the perf report (§3 P4 / s101-real-cold +
    // s101-real-warm), un-cached XName / attribute-name lookups in
    // MakePointElement and the per-feature element builders showed up
    // as String.Concat(string, string, string) consuming 50 % / 43 %
    // of all managed allocations during S-101 portrayal — driven by
    // the volume of Point geometry built per cell. Caching avoids the
    // XNamespace.GetName hash lookup *and* keeps these names rooted
    // by a Gen2 static field so they never participate in GC promotion.
    private static readonly XName DatasetName = S100Ns + "Dataset";
    private static readonly XName FeatureName = S100Ns + "Feature";
    private static readonly XName GeometryName = S100Ns + "Geometry";
    private static readonly XName PointName = S100Ns + "Point";
    private static readonly XName CurveName = S100Ns + "Curve";
    private static readonly XName SurfaceName = S100Ns + "Surface";
    private static readonly XName RingName = S100Ns + "Ring";
    private static readonly XName AttributeName = S100Ns + "Attribute";

    private static readonly XName IdAttrName = "id";
    private static readonly XName TypeAttrName = "type";
    private static readonly XName CodeAttrName = "code";
    private static readonly XName LatAttrName = "lat";
    private static readonly XName LonAttrName = "lon";

    private readonly S101Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    public S101FeatureXmlSource(S101Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
    }

    public IReadOnlyList<string> FeatureTypesPresent
    {
        get
        {
            if (_featureTypes is not null) return _featureTypes;

            var doc = _dataset.Document;
            var types = new HashSet<string>();

            foreach (var f in doc.Features)
            {
                var name = doc.FeatureTypeCatalogue.TryGetValue(f.FeatureTypeCode, out var n)
                    ? n : f.FeatureTypeCode.ToString();
                types.Add(name);
            }

            _featureTypes = types.ToList();
            return _featureTypes;
        }
    }

    public XmlReader GetFeatureXml(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var xmlDoc = BuildFeatureXml();
        return xmlDoc.CreateReader();
    }

    private XDocument BuildFeatureXml()
    {
        var doc = _dataset.Document;
        var root = new XElement(DatasetName);

        foreach (var feat in doc.Features)
        {
            var featureType = doc.FeatureTypeCatalogue.TryGetValue(feat.FeatureTypeCode, out var name)
                ? name : feat.FeatureTypeCode.ToString();

            var el = new XElement(FeatureName,
                new XAttribute(IdAttrName, feat.RecordId),
                new XAttribute(TypeAttrName, featureType));

            // Geometry
            if (feat.SpatialAssociations.Length > 0)
            {
                var first = feat.SpatialAssociations[0];
                var geom = first.RecordName switch
                {
                    RcnmPoint => BuildPointGeometry(feat, doc),
                    RcnmCurveSegment or RcnmCompositeCurve => BuildCurveGeometry(feat, doc),
                    RcnmSurface => BuildSurfaceGeometry(feat, doc),
                    _ => null,
                };
                if (geom is not null)
                    el.Add(geom);
            }

            // Attributes
            foreach (var attr in feat.Attributes)
            {
                var attrName = doc.AttributeTypeCatalogue.TryGetValue(attr.NumericCode, out var aName)
                    ? aName : attr.NumericCode.ToString();
                el.Add(new XElement(AttributeName,
                    new XAttribute(CodeAttrName, attrName),
                    attr.Value));
            }

            root.Add(el);
        }

        return new XDocument(root);
    }

    private static XElement? BuildPointGeometry(S101FeatureRecord feat, S101Document doc)
    {
        double cmfx = doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = doc.StructureInfo.CoordinateMultiplicationFactorY;
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;

        var geom = new XElement(GeometryName);
        bool hasPoints = false;

        foreach (var spa in feat.SpatialAssociations)
        {
            if (spa.RecordName == RcnmPoint && doc.Points.TryGetValue(spa.RecordId, out var pt))
            {
                geom.Add(MakePointElement(pt.Y / cmfy, pt.X / cmfx));
                hasPoints = true;
            }
        }

        return hasPoints ? geom : null;
    }

    private static XElement? BuildCurveGeometry(S101FeatureRecord feat, S101Document doc)
    {
        var geom = new XElement(GeometryName);
        var curve = new XElement(CurveName);

        foreach (var spa in feat.SpatialAssociations)
        {
            AddCurvePoints(spa.RecordName, spa.RecordId, spa.Orientation, doc, curve);
        }

        if (!curve.HasElements) return null;
        geom.Add(curve);
        return geom;
    }

    private static XElement? BuildSurfaceGeometry(S101FeatureRecord feat, S101Document doc)
    {
        var geom = new XElement(GeometryName);
        bool hasGeometry = false;

        foreach (var spa in feat.SpatialAssociations)
        {
            if (spa.RecordName != RcnmSurface) continue;
            if (!doc.Surfaces.TryGetValue(spa.RecordId, out var surface)) continue;

            var surfaceEl = new XElement(SurfaceName);
            var exteriorRing = new XElement(RingName, new XAttribute(TypeAttrName, "exterior"));
            var interiorRing = new XElement(RingName, new XAttribute(TypeAttrName, "interior"));

            foreach (var ring in surface.RingAssociations)
            {
                var target = ring.Usage == UsageExterior ? exteriorRing : interiorRing;
                AddCurvePoints(ring.RecordName, ring.RecordId, ring.Orientation, doc, target);
            }

            surfaceEl.Add(exteriorRing);
            if (interiorRing.HasElements)
                surfaceEl.Add(interiorRing);

            geom.Add(surfaceEl);
            hasGeometry = true;
        }

        return hasGeometry ? geom : null;
    }

    private static void AddCurvePoints(byte rcnm, uint rcid, byte orientation, S101Document doc, XElement target)
    {
        double cmfx = doc.StructureInfo.CoordinateMultiplicationFactorX;
        double cmfy = doc.StructureInfo.CoordinateMultiplicationFactorY;
        if (cmfx == 0) cmfx = 10_000_000;
        if (cmfy == 0) cmfy = 10_000_000;

        if (rcnm == RcnmCurveSegment && doc.CurveSegments.TryGetValue(rcid, out var seg))
        {
            var points = new List<XElement>();

            foreach (var pta in seg.PointAssociations)
            {
                if (pta.Topology == 1 && doc.Points.TryGetValue(pta.RecordId, out var startPt))
                    points.Add(MakePointElement(startPt.Y / cmfy, startPt.X / cmfx));
            }

            foreach (var (y, x) in seg.IntermediateCoordinates)
            {
                points.Add(MakePointElement(y / cmfy, x / cmfx));
            }

            foreach (var pta in seg.PointAssociations)
            {
                if (pta.Topology == 2 && doc.Points.TryGetValue(pta.RecordId, out var endPt))
                    points.Add(MakePointElement(endPt.Y / cmfy, endPt.X / cmfx));
            }

            if (orientation == OrientationReverse)
                points.Reverse();

            foreach (var pt in points)
                target.Add(pt);
        }
        else if (rcnm == RcnmCompositeCurve && doc.CompositeCurves.TryGetValue(rcid, out var composite))
        {
            foreach (var component in composite.CurveComponents)
            {
                AddCurvePoints(component.RecordName, component.RecordId, component.Orientation, doc, target);
            }
        }
    }

    private static XElement MakePointElement(double lat, double lon)
    {
        return new XElement(PointName,
            new XAttribute(LatAttrName, lat.ToString(CultureInfo.InvariantCulture)),
            new XAttribute(LonAttrName, lon.ToString(CultureInfo.InvariantCulture)));
    }
}
