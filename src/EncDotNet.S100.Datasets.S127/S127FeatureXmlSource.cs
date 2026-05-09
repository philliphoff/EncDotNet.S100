using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S127;

/// <summary>
/// Projects an <see cref="S127Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format consumed by the bundled S-127 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// The S-127 <c>main.xsl</c> rule (PC 2.0.0) selects on
/// <c>Dataset/Features/*</c>, producing a display list element. This source
/// emits exactly that shape, with each feature element named after its
/// type code (e.g. <c>PilotBoardingPlace</c>) plus a <c>primitive</c>
/// attribute and a flat list of <c>Point</c> references.
/// </remarks>
public sealed class S127FeatureXmlSource : IFeatureXmlSource
{
    private readonly S127Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    /// <summary>Initializes a new <see cref="S127FeatureXmlSource"/> over the given dataset.</summary>
    public S127FeatureXmlSource(S127Dataset dataset)
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
            foreach (var f in _dataset.Features) types.Add(f.FeatureType);
            _featureTypes = types.ToList();
            return _featureTypes;
        }
    }

    /// <inheritdoc/>
    public XmlReader GetFeatureXml() => BuildFeatureXml().CreateReader();

    private XDocument BuildFeatureXml()
    {
        var root = new XElement("Dataset");
        var featuresElement = new XElement("Features");
        var pointsElement = new XElement("Points");

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
                        EmitPoint(pointsElement, featureElement, lat, lon, ref pointCounter);
                    break;
                case GmlGeometryType.Curve:
                    foreach (var curve in feature.Curves)
                        foreach (var (lat, lon) in curve)
                            EmitPoint(pointsElement, featureElement, lat, lon, ref pointCounter);
                    break;
                case GmlGeometryType.Surface:
                    foreach (var (lat, lon) in feature.ExteriorRing)
                        EmitPoint(pointsElement, featureElement, lat, lon, ref pointCounter);
                    break;
            }

            foreach (var (code, value) in feature.Attributes)
                featureElement.Add(new XElement(code, value));

            foreach (var complex in feature.ComplexAttributes)
            {
                var complexElement = new XElement(complex.Code);
                foreach (var (subCode, subValue) in complex.SubAttributes)
                    complexElement.Add(new XElement(subCode, subValue));
                featureElement.Add(complexElement);
            }

            featuresElement.Add(featureElement);
        }

        root.Add(pointsElement);
        root.Add(featuresElement);
        return new XDocument(root);
    }

    private static void EmitPoint(XElement points, XElement feature, double lat, double lon, ref int counter)
    {
        var id = $"p{++counter}";
        points.Add(new XElement("Point",
            new XAttribute("id", id),
            new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
        feature.Add(new XElement("Point", new XAttribute("ref", id)));
    }
}
