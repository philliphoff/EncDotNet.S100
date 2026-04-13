using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S129;

/// <summary>
/// Projects an <see cref="S129Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format for consumption by S-129 XSLT portrayal rules.
/// </summary>
public sealed class S129FeatureXmlSource : IFeatureXmlSource
{
    private readonly S129Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    public S129FeatureXmlSource(S129Dataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        _dataset = dataset;
    }

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

    public XmlReader GetFeatureXml()
    {
        var xmlDoc = BuildFeatureXml();
        return xmlDoc.CreateReader();
    }

    private XDocument BuildFeatureXml()
    {
        var root = new XElement("Dataset");
        var featuresElement = new XElement("Features");

        int pointCounter = 0;
        var pointsElement = new XElement("Points");

        foreach (var feature in _dataset.Features)
        {
            var primitiveType = feature.GeometryType switch
            {
                S129GeometryType.Point => "Point",
                S129GeometryType.Curve => "Curve",
                S129GeometryType.Surface => "Surface",
                _ => "NoGeometry",
            };

            var featureElement = new XElement(feature.FeatureType,
                new XAttribute("id", feature.Id),
                new XAttribute("primitive", primitiveType));

            switch (feature.GeometryType)
            {
                case S129GeometryType.Point:
                    foreach (var (lat, lon) in feature.Points)
                    {
                        var pointId = $"p{++pointCounter}";
                        pointsElement.Add(new XElement("Point",
                            new XAttribute("id", pointId),
                            new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
                        featureElement.Add(new XElement("Point",
                            new XAttribute("ref", pointId)));
                    }
                    break;

                case S129GeometryType.Curve:
                    foreach (var curve in feature.Curves)
                    {
                        foreach (var (lat, lon) in curve)
                        {
                            var pointId = $"p{++pointCounter}";
                            pointsElement.Add(new XElement("Point",
                                new XAttribute("id", pointId),
                                new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
                                new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
                            featureElement.Add(new XElement("Point",
                                new XAttribute("ref", pointId)));
                        }
                    }
                    break;

                case S129GeometryType.Surface:
                    foreach (var (lat, lon) in feature.ExteriorRing)
                    {
                        var pointId = $"p{++pointCounter}";
                        pointsElement.Add(new XElement("Point",
                            new XAttribute("id", pointId),
                            new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
                            new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
                        featureElement.Add(new XElement("Point",
                            new XAttribute("ref", pointId)));
                    }
                    break;
            }

            foreach (var (code, value) in feature.Attributes)
            {
                featureElement.Add(new XElement(code, value));
            }

            foreach (var complex in feature.ComplexAttributes)
            {
                var complexElement = new XElement(complex.Code);
                foreach (var (subCode, subValue) in complex.SubAttributes)
                {
                    complexElement.Add(new XElement(subCode, subValue));
                }
                featureElement.Add(complexElement);
            }

            featuresElement.Add(featureElement);
        }

        root.Add(pointsElement);
        root.Add(featuresElement);

        return new XDocument(root);
    }
}
