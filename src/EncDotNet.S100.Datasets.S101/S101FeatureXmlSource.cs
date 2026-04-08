using System.Xml;
using System.Xml.Linq;
using EncDotNet.S57;
using EncDotNet.S57.Charts;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// Projects an <see cref="S101Dataset"/> into S-100 Part 9 FeatureXML
/// for consumption by the XSLT/Lua portrayal pipeline.
/// </summary>
public sealed class S101FeatureXmlSource : IFeatureXmlSource
{
    private static readonly XNamespace S100Ns = "http://www.iho.int/s100/5.0";

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

            var chart = _dataset.Chart;
            var types = new HashSet<string>();

            foreach (var f in chart.PointFeatures) types.Add(f.ObjectCode.ToString());
            foreach (var f in chart.LineFeatures) types.Add(f.ObjectCode.ToString());
            foreach (var f in chart.AreaFeatures) types.Add(f.ObjectCode.ToString());

            _featureTypes = types.ToList();
            return _featureTypes;
        }
    }

    public XmlReader GetFeatureXml()
    {
        var doc = BuildFeatureXml();
        return doc.CreateReader();
    }

    private XDocument BuildFeatureXml()
    {
        var chart = _dataset.Chart;
        int comf = chart.CoordinateMultiplicationFactor;

        var root = new XElement(S100Ns + "Dataset");

        // Point features
        foreach (var pf in chart.PointFeatures)
        {
            root.Add(BuildPointFeatureElement(pf, chart, comf));
        }

        // Line features
        foreach (var lf in chart.LineFeatures)
        {
            root.Add(BuildLineFeatureElement(lf, chart, comf));
        }

        // Area features
        foreach (var af in chart.AreaFeatures)
        {
            root.Add(BuildAreaFeatureElement(af, chart, comf));
        }

        return new XDocument(root);
    }

    private static XElement BuildPointFeatureElement(
        S57PointFeature feature, S57Chart chart, int comf)
    {
        var el = new XElement(S100Ns + "Feature",
            new XAttribute("id", feature.RecordName.RecordId),
            new XAttribute("type", feature.ObjectCode.ToString()));

        // Geometry — point position
        if (feature.HasSpatialReferences)
        {
            var geom = new XElement(S100Ns + "Geometry");
            foreach (var spatialRef in feature.SpatialReferences)
            {
                if (chart.IsolatedNodes.TryGetValue(spatialRef.Name, out var isolated) && isolated.HasPosition)
                {
                    var pos = isolated.Position!.Value;
                    geom.Add(MakePointElement(pos, comf));
                }
                else if (chart.ConnectedNodes.TryGetValue(spatialRef.Name, out var connected))
                {
                    geom.Add(MakePointElement(connected.Position, comf));
                }
            }
            el.Add(geom);
        }

        AddAttributes(el, feature);
        return el;
    }

    private static XElement BuildLineFeatureElement(
        S57LineFeature feature, S57Chart chart, int comf)
    {
        var el = new XElement(S100Ns + "Feature",
            new XAttribute("id", feature.RecordName.RecordId),
            new XAttribute("type", feature.ObjectCode.ToString()));

        if (feature.HasEdgeReferences)
        {
            var geom = new XElement(S100Ns + "Geometry");
            var curve = new XElement(S100Ns + "Curve");

            foreach (var edgeRef in feature.EdgeReferences)
            {
                if (!chart.Edges.TryGetValue(edgeRef.Name, out var edge)) continue;

                var points = ResolveEdgePoints(edge, edgeRef.Orientation, chart, comf);
                foreach (var pt in points)
                {
                    curve.Add(pt);
                }
            }

            geom.Add(curve);
            el.Add(geom);
        }

        AddAttributes(el, feature);
        return el;
    }

    private static XElement BuildAreaFeatureElement(
        S57AreaFeature feature, S57Chart chart, int comf)
    {
        var el = new XElement(S100Ns + "Feature",
            new XAttribute("id", feature.RecordName.RecordId),
            new XAttribute("type", feature.ObjectCode.ToString()));

        var geom = new XElement(S100Ns + "Geometry");
        bool hasGeometry = false;

        // Exterior ring
        if (feature.HasExteriorEdgeReferences)
        {
            var exterior = new XElement(S100Ns + "Surface");
            var ring = new XElement(S100Ns + "Ring", new XAttribute("type", "exterior"));

            foreach (var edgeRef in feature.ExteriorEdgeReferences)
            {
                if (!chart.Edges.TryGetValue(edgeRef.EdgeName, out var edge)) continue;

                var points = ResolveEdgePoints(edge, edgeRef.Orientation, chart, comf);
                foreach (var pt in points)
                {
                    ring.Add(pt);
                }
            }

            exterior.Add(ring);

            // Interior rings (holes)
            if (feature.InteriorEdgeReferences.Count > 0)
            {
                var interiorRing = new XElement(S100Ns + "Ring", new XAttribute("type", "interior"));

                foreach (var edgeRef in feature.InteriorEdgeReferences)
                {
                    if (!chart.Edges.TryGetValue(edgeRef.EdgeName, out var edge)) continue;

                    var points = ResolveEdgePoints(edge, edgeRef.Orientation, chart, comf);
                    foreach (var pt in points)
                    {
                        interiorRing.Add(pt);
                    }
                }

                exterior.Add(interiorRing);
            }

            geom.Add(exterior);
            hasGeometry = true;
        }

        if (hasGeometry) el.Add(geom);

        AddAttributes(el, feature);
        return el;
    }

    // ── Shared helpers ─────────────────────────────────────────────────

    private static IEnumerable<XElement> ResolveEdgePoints(
        S57Edge edge, S57Orientation orientation, S57Chart chart, int comf)
    {
        var points = new List<XElement>();

        if (edge.HasBeginningNode && chart.ConnectedNodes.TryGetValue(edge.BeginningNode!.Value, out var beginNode))
        {
            points.Add(MakePointElement(beginNode.Position, comf));
        }

        if (edge.HasIntermediatePoints)
        {
            foreach (var pt in edge.IntermediatePoints)
            {
                points.Add(MakePointElement(pt, comf));
            }
        }

        if (edge.HasEndNode && chart.ConnectedNodes.TryGetValue(edge.EndNode!.Value, out var endNode))
        {
            points.Add(MakePointElement(endNode.Position, comf));
        }

        if (orientation == S57Orientation.Reverse)
        {
            points.Reverse();
        }

        return points;
    }

    private static XElement MakePointElement(S57Coordinate2D coord, int comf)
    {
        double lat = coord.Y / (double)comf;
        double lon = coord.X / (double)comf;
        return new XElement(S100Ns + "Point",
            new XAttribute("lat", lat.ToString(System.Globalization.CultureInfo.InvariantCulture)),
            new XAttribute("lon", lon.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static void AddAttributes(XElement featureElement, S57TypedFeature feature)
    {
        if (!feature.HasAttributes) return;

        foreach (var attr in feature.Attributes)
        {
            featureElement.Add(new XElement(S100Ns + "Attribute",
                new XAttribute("code", attr.AttributeCode.ToString()),
                attr.Value));
        }
    }
}
