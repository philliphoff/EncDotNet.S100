using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// Projects an <see cref="S411Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format for consumption by S-411 XSLT portrayal rules.
/// </summary>
public sealed class S411FeatureXmlSource : IFeatureXmlSource
{
    private readonly S411Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    /// <summary>
    /// Initializes a new <see cref="S411FeatureXmlSource"/> wrapping the given dataset.
    /// </summary>
    public S411FeatureXmlSource(S411Dataset dataset)
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
                types.Add(f.FeatureType);

            _featureTypes = [.. types];
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
            var primitiveType = feature.GeometryType switch
            {
                S411GeometryType.Point => "Point",
                S411GeometryType.Curve => "Curve",
                S411GeometryType.Surface => "Surface",
                _ => "NoGeometry",
            };

            var featureElement = new XElement(feature.FeatureType,
                new XAttribute("id", feature.Id),
                new XAttribute("primitive", primitiveType));

            switch (feature.GeometryType)
            {
                case S411GeometryType.Point:
                    foreach (var (lat, lon) in feature.Points)
                        AddPointReference(pointsElement, featureElement, lat, lon, ref pointCounter);
                    break;

                case S411GeometryType.Curve:
                    foreach (var curve in feature.Curves)
                        foreach (var (lat, lon) in curve)
                            AddPointReference(pointsElement, featureElement, lat, lon, ref pointCounter);
                    break;

                case S411GeometryType.Surface:
                    foreach (var (lat, lon) in feature.ExteriorRing)
                        AddPointReference(pointsElement, featureElement, lat, lon, ref pointCounter);
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

    private static void AddPointReference(XElement pointsElement, XElement featureElement, double lat, double lon, ref int counter)
    {
        var pointId = $"p{++counter}";
        pointsElement.Add(new XElement("Point",
            new XAttribute("id", pointId),
            new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
        featureElement.Add(new XElement("Point", new XAttribute("ref", pointId)));
    }
}
