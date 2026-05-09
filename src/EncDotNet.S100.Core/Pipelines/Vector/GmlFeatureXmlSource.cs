using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Generic base class that projects GML-encoded S-100 features into the
/// S-100 Part 9 FeatureXML intermediate format consumed by XSLT portrayal
/// rules.
/// </summary>
/// <remarks>
/// <para>Emits the standard <c>Dataset</c> shape expected by the XSLT
/// pipeline:</para>
/// <code>
/// &lt;Dataset&gt;
///   &lt;Points&gt;…&lt;/Points&gt;
///   &lt;Features&gt;…&lt;/Features&gt;
/// &lt;/Dataset&gt;
/// </code>
/// <para>Subclasses can override <see cref="WriteFeatureExtensions"/>,
/// <see cref="BuildComplexAttributeElement"/>, and
/// <see cref="WriteDatasetExtensions"/> to add spec-specific elements
/// (e.g. information types, xlink references, nested complex attributes).
/// </para>
/// <para>S-411 does not use this class — its FeatureXML is a pass-through
/// of the original GML document.</para>
/// </remarks>
/// <typeparam name="TFeature">
/// The concrete feature type constrained to <see cref="IGmlFeature"/>.
/// </typeparam>
public class GmlFeatureXmlSource<TFeature> : IFeatureXmlSource
    where TFeature : IGmlFeature
{
    private readonly IReadOnlyList<TFeature> _features;
    private IReadOnlyList<string>? _featureTypes;

    /// <summary>
    /// Initializes a new <see cref="GmlFeatureXmlSource{TFeature}"/>.
    /// </summary>
    protected GmlFeatureXmlSource(IReadOnlyList<TFeature> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        _features = features;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> FeatureTypesPresent
    {
        get
        {
            if (_featureTypes is not null) return _featureTypes;
            var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in _features)
                types.Add(f.FeatureType);
            _featureTypes = [.. types];
            return _featureTypes;
        }
    }

    /// <inheritdoc/>
    public XmlReader GetFeatureXml() => BuildFeatureXml().CreateReader();

    private XDocument BuildFeatureXml()
    {
        var pointsElement = new XElement("Points");
        var featuresElement = new XElement("Features");
        int pointCounter = 0;

        foreach (var feature in _features)
        {
            var primitiveType = feature.GeometryType switch
            {
                GmlGeometryType.Point => "Point",
                GmlGeometryType.Curve => "Curve",
                GmlGeometryType.Surface => "Surface",
                _ => "NoGeometry",
            };

            var featureElement = new XElement(feature.FeatureType,
                new XAttribute("id", feature.Id),
                new XAttribute("primitive", primitiveType));

            switch (feature.GeometryType)
            {
                case GmlGeometryType.Point:
                    foreach (var (lat, lon) in feature.Points)
                        EmitPointRef(pointsElement, featureElement, ref pointCounter, lat, lon);
                    break;

                case GmlGeometryType.Curve:
                    foreach (var curve in feature.Curves)
                        foreach (var (lat, lon) in curve)
                            EmitPointRef(pointsElement, featureElement, ref pointCounter, lat, lon);
                    break;

                case GmlGeometryType.Surface:
                    foreach (var (lat, lon) in feature.ExteriorRing)
                        EmitPointRef(pointsElement, featureElement, ref pointCounter, lat, lon);
                    break;
            }

            foreach (var (code, value) in feature.Attributes)
                featureElement.Add(new XElement(code, value));

            foreach (var complex in feature.ComplexAttributes)
                featureElement.Add(BuildComplexAttributeElement(complex));

            WriteFeatureExtensions(feature, featureElement);

            featuresElement.Add(featureElement);
        }

        var root = new XElement("Dataset",
            pointsElement,
            featuresElement);

        WriteDatasetExtensions(root);

        return new XDocument(root);
    }

    /// <summary>
    /// Called after attributes and complex attributes have been written to a
    /// feature element. Override to add spec-specific elements such as xlink
    /// references or information-type associations.
    /// </summary>
    protected virtual void WriteFeatureExtensions(TFeature feature, XElement featureElement)
    {
    }

    /// <summary>
    /// Called after all features have been written to the root <c>Dataset</c>
    /// element. Override to add spec-specific dataset-level elements such as
    /// an <c>InformationTypes</c> section.
    /// </summary>
    protected virtual void WriteDatasetExtensions(XElement root)
    {
    }

    /// <summary>
    /// Builds an XML element for a complex attribute. Override for specs
    /// that support nested complex attributes (e.g. S-128).
    /// </summary>
    protected virtual XElement BuildComplexAttributeElement(IGmlComplexAttribute complex)
    {
        var el = new XElement(complex.Code);
        foreach (var (subCode, subValue) in complex.SubAttributes)
            el.Add(new XElement(subCode, subValue));
        return el;
    }

    /// <summary>Emits a point into the registry and a reference into the feature.</summary>
    protected static void EmitPointRef(
        XElement pointsElement,
        XElement featureElement,
        ref int counter,
        double lat,
        double lon)
    {
        var pointId = $"p{++counter}";
        pointsElement.Add(new XElement("Point",
            new XAttribute("id", pointId),
            new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
        featureElement.Add(new XElement("Point", new XAttribute("ref", pointId)));
    }
}
