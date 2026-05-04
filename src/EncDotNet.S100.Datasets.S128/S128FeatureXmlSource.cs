using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// Projects an <see cref="S128Dataset"/> into the S-100 Part 9 FeatureXML
/// neutral form consumed by the bundled S-128 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// The bundled S-128 2.0.0 <c>main.xsl</c> walks <c>Dataset/Features/*</c>
/// and dispatches to per-feature templates (e.g. <c>ElectronicProduct.xsl</c>,
/// <c>S100Service.xsl</c>) plus a <c>Default</c> fallback. The shape produced
/// here mirrors the S-122 FeatureXML projection — features in
/// <c>Dataset/Features</c>, points in <c>Dataset/Points</c> referenced by
/// id — so the existing portrayal pipeline plumbing applies unchanged.
/// </remarks>
public sealed class S128FeatureXmlSource : IFeatureXmlSource
{
    private readonly S128Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    /// <summary>Initializes a new <see cref="S128FeatureXmlSource"/> wrapping the given dataset.</summary>
    public S128FeatureXmlSource(S128Dataset dataset)
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
            _featureTypes = types.ToList();
            return _featureTypes;
        }
    }

    /// <inheritdoc/>
    public XmlReader GetFeatureXml() => BuildFeatureXml().CreateReader();

    private XDocument BuildFeatureXml()
    {
        var pointsElement = new XElement("Points");
        var featuresElement = new XElement("Features");
        var infoElement = new XElement("InformationTypes");
        int pointCounter = 0;

        foreach (var feature in _dataset.Features)
        {
            var primitive = feature.GeometryType switch
            {
                S128GeometryType.Point => "Point",
                S128GeometryType.Curve => "Curve",
                S128GeometryType.Surface => "Surface",
                _ => "NoGeometry",
            };

            var featureElement = new XElement(feature.FeatureType,
                new XAttribute("id", feature.Id),
                new XAttribute("primitive", primitive));

            switch (feature.GeometryType)
            {
                case S128GeometryType.Point:
                    AppendPointRefs(featureElement, pointsElement, feature.Points, ref pointCounter);
                    break;
                case S128GeometryType.Curve:
                    foreach (var curve in feature.Curves)
                        AppendPointRefs(featureElement, pointsElement, curve, ref pointCounter);
                    break;
                case S128GeometryType.Surface:
                    AppendPointRefs(featureElement, pointsElement, feature.ExteriorRing, ref pointCounter);
                    break;
            }

            foreach (var (code, value) in feature.Attributes)
                featureElement.Add(new XElement(code, value));

            foreach (var complex in feature.ComplexAttributes)
                featureElement.Add(BuildComplex(complex));

            foreach (var r in feature.References)
            {
                featureElement.Add(new XElement(r.Role,
                    new XAttribute("href", r.Href),
                    new XAttribute("targetId", r.TargetId)));
            }

            featuresElement.Add(featureElement);
        }

        foreach (var info in _dataset.InformationTypes)
        {
            var infoEl = new XElement(info.TypeCode, new XAttribute("id", info.Id));
            foreach (var (code, value) in info.Attributes)
                infoEl.Add(new XElement(code, value));
            foreach (var complex in info.ComplexAttributes)
                infoEl.Add(BuildComplex(complex));
            infoElement.Add(infoEl);
        }

        var root = new XElement("Dataset",
            pointsElement,
            featuresElement,
            infoElement);
        return new XDocument(root);
    }

    private static void AppendPointRefs(
        XElement featureElement,
        XElement pointsElement,
        IEnumerable<(double Latitude, double Longitude)> coords,
        ref int counter)
    {
        foreach (var (lat, lon) in coords)
        {
            var pid = $"p{++counter}";
            pointsElement.Add(new XElement("Point",
                new XAttribute("id", pid),
                new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
            featureElement.Add(new XElement("Point", new XAttribute("ref", pid)));
        }
    }

    private static XElement BuildComplex(S128ComplexAttribute complex)
    {
        var el = new XElement(complex.Code);
        foreach (var (k, v) in complex.SubAttributes)
            el.Add(new XElement(k, v));
        foreach (var nested in complex.NestedAttributes)
            el.Add(BuildComplex(nested));
        return el;
    }
}
