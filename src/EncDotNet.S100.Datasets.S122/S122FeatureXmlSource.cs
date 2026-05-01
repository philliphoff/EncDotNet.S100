using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S122;

/// <summary>
/// Projects an <see cref="S122Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format for consumption by S-122 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// The S-122 XSLT rules (main.xsl) expect input in this form:
/// <code>
/// &lt;Dataset&gt;
///   &lt;Features&gt;
///     &lt;NavwarnPart id="f0" primitive="Point"&gt;
///       &lt;Point ref="p1"/&gt;
///       &lt;restriction&gt;7&lt;/restriction&gt;
///     &lt;/NavwarnPart&gt;
///   &lt;/Features&gt;
/// &lt;/Dataset&gt;
/// </code>
/// </remarks>
public sealed class S122FeatureXmlSource : IFeatureXmlSource
{
    private readonly S122Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    /// <summary>
    /// Initializes a new <see cref="S122FeatureXmlSource"/> wrapping the given dataset.
    /// </summary>
    public S122FeatureXmlSource(S122Dataset dataset)
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
    public XmlReader GetFeatureXml()
    {
        var xmlDoc = BuildFeatureXml();
        return xmlDoc.CreateReader();
    }

    private XDocument BuildFeatureXml()
    {
        var root = new XElement("Dataset");
        var featuresElement = new XElement("Features");

        // Build a point registry so we can reference points by id (required by XSLT rules)
        int pointCounter = 0;
        var pointsElement = new XElement("Points");

        foreach (var feature in _dataset.Features)
        {
            var primitiveType = feature.GeometryType switch
            {
                S122GeometryType.Point => "Point",
                S122GeometryType.Curve => "Curve",
                S122GeometryType.Surface => "Surface",
                _ => "NoGeometry",
            };

            var featureElement = new XElement(feature.FeatureType,
                new XAttribute("id", feature.Id),
                new XAttribute("primitive", primitiveType));

            // Add geometry references
            switch (feature.GeometryType)
            {
                case S122GeometryType.Point:
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

                case S122GeometryType.Curve:
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

                case S122GeometryType.Surface:
                    // Emit exterior ring points
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

            // Add simple attributes as child elements
            foreach (var (code, value) in feature.Attributes)
            {
                featureElement.Add(new XElement(code, value));
            }

            // Add complex attributes as nested elements
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
