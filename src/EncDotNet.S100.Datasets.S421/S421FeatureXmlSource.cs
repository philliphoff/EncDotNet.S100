using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S421;

/// <summary>
/// Projects an <see cref="S421Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format for consumption by S-421 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// The emitted document has the form:
/// <code>
/// &lt;Dataset&gt;
///   &lt;Points&gt;
///     &lt;Point id="p1" lat="..." lon="..."/&gt;
///   &lt;/Points&gt;
///   &lt;Features&gt;
///     &lt;RouteWaypoint id="RTE.WPT.1" primitive="Point"&gt;
///       &lt;Point ref="p1"/&gt;
///       &lt;routeWaypointID&gt;1&lt;/routeWaypointID&gt;
///     &lt;/RouteWaypoint&gt;
///   &lt;/Features&gt;
/// &lt;/Dataset&gt;
/// </code>
/// </remarks>
public sealed class S421FeatureXmlSource : IFeatureXmlSource
{
    private readonly S421Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    /// <summary>
    /// Initializes a new <see cref="S421FeatureXmlSource"/> wrapping the given dataset.
    /// </summary>
    public S421FeatureXmlSource(S421Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> FeatureTypesPresent
    {
        get
        {
            if (_featureTypes is not null) return _featureTypes;

            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _dataset.Features)
            {
                types.Add(f.FeatureType);
            }
            _featureTypes = types.ToList();
            return _featureTypes;
        }
    }

    /// <inheritdoc/>
    public XmlReader GetFeatureXml() => BuildFeatureXml().CreateReader();

    private XDocument BuildFeatureXml()
    {
        var root = new XElement("Dataset");
        var pointsElement = new XElement("Points");
        var featuresElement = new XElement("Features");
        int pointCounter = 0;

        foreach (var feature in _dataset.Features)
        {
            var primitive = feature.GeometryType switch
            {
                GmlGeometryType.Point => "Point",
                GmlGeometryType.Curve => "Curve",
                GmlGeometryType.Surface => "Surface",
                _ => "NoGeometry",
            };

            var featureElement = new XElement(feature.FeatureType,
                new XAttribute("id", feature.Id),
                new XAttribute("primitive", primitive));

            switch (feature.GeometryType)
            {
                case GmlGeometryType.Point:
                    foreach (var (lat, lon) in feature.Points)
                        AddPointReference(pointsElement, featureElement, ref pointCounter, lat, lon);
                    break;

                case GmlGeometryType.Curve:
                    foreach (var curve in feature.Curves)
                        foreach (var (lat, lon) in curve)
                            AddPointReference(pointsElement, featureElement, ref pointCounter, lat, lon);
                    break;

                case GmlGeometryType.Surface:
                    foreach (var (lat, lon) in feature.ExteriorRing)
                        AddPointReference(pointsElement, featureElement, ref pointCounter, lat, lon);
                    break;
            }

            foreach (var (code, value) in feature.Attributes)
            {
                featureElement.Add(new XElement(code, value));
            }

            foreach (var complex in feature.ComplexAttributes)
            {
                var ce = new XElement(complex.Code);
                foreach (var (k, v) in complex.SubAttributes)
                    ce.Add(new XElement(k, v));
                featureElement.Add(ce);
            }

            foreach (var reference in feature.References)
            {
                featureElement.Add(new XElement(reference.Role,
                    new XAttribute("href", reference.Href)));
            }

            featuresElement.Add(featureElement);
        }

        root.Add(pointsElement);
        root.Add(featuresElement);
        return new XDocument(root);
    }

    private static void AddPointReference(XElement pointsElement, XElement featureElement, ref int counter, double lat, double lon)
    {
        var pointId = $"p{++counter}";
        pointsElement.Add(new XElement("Point",
            new XAttribute("id", pointId),
            new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
        featureElement.Add(new XElement("Point", new XAttribute("ref", pointId)));
    }
}
