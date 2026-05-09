using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S125;

/// <summary>
/// Projects an <see cref="S125Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format consumed by the S-125 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// <para>The S-125 portrayal catalogue's <c>main.xsl</c> top-level template
/// matches against <c>Dataset/Features/*</c> and resolves information
/// associations via <c>/Dataset/InformationTypes/*[@id=$ref]</c>. This
/// source emits exactly that shape:</para>
/// <code>
/// &lt;Dataset&gt;
///   &lt;Points&gt;
///     &lt;Point id="p1" lat="…" lon="…"/&gt;
///   &lt;/Points&gt;
///   &lt;Features&gt;
///     &lt;LateralBuoy id="f1" primitive="Point"&gt;
///       &lt;Point ref="p1"/&gt;
///       &lt;AtoNStatus informationRef="info1"/&gt;
///       &lt;categoryOfLateralMark&gt;1&lt;/categoryOfLateralMark&gt;
///     &lt;/LateralBuoy&gt;
///   &lt;/Features&gt;
///   &lt;InformationTypes&gt;
///     &lt;AtonStatusInformation id="info1"&gt;
///       &lt;changeTypes&gt;1&lt;/changeTypes&gt;
///     &lt;/AtonStatusInformation&gt;
///   &lt;/InformationTypes&gt;
/// &lt;/Dataset&gt;
/// </code>
/// </remarks>
public sealed class S125FeatureXmlSource : IFeatureXmlSource
{
    private readonly S125Dataset _dataset;
    private IReadOnlyList<string>? _featureTypes;

    /// <summary>Initializes a new <see cref="S125FeatureXmlSource"/>.</summary>
    public S125FeatureXmlSource(S125Dataset dataset)
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
        var informationTypesElement = new XElement("InformationTypes");

        int pointCounter = 0;

        foreach (var feature in _dataset.Features)
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
                        featureElement.Add(EmitPointRef(pointsElement, ref pointCounter, lat, lon));
                    break;

                case GmlGeometryType.Curve:
                    foreach (var curve in feature.Curves)
                        foreach (var (lat, lon) in curve)
                            featureElement.Add(EmitPointRef(pointsElement, ref pointCounter, lat, lon));
                    break;

                case GmlGeometryType.Surface:
                    foreach (var (lat, lon) in feature.ExteriorRing)
                        featureElement.Add(EmitPointRef(pointsElement, ref pointCounter, lat, lon));
                    break;
            }

            // Information references (e.g. AtoNStatus) emitted with the role
            // name as the element and an `informationRef` attribute. The
            // bundled S-125 main.xsl reads `@informationRef`.
            if (!feature.InformationReferences.IsDefaultOrEmpty)
            {
                foreach (var infoRef in feature.InformationReferences)
                {
                    featureElement.Add(new XElement(infoRef.Role,
                        new XAttribute("informationRef", infoRef.InformationRef)));
                }
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

        foreach (var info in _dataset.InformationTypes)
        {
            var infoElement = new XElement(info.TypeCode, new XAttribute("id", info.Id));

            foreach (var (code, value) in info.Attributes)
                infoElement.Add(new XElement(code, value));

            foreach (var complex in info.ComplexAttributes)
            {
                var complexElement = new XElement(complex.Code);
                foreach (var (subCode, subValue) in complex.SubAttributes)
                    complexElement.Add(new XElement(subCode, subValue));
                infoElement.Add(complexElement);
            }

            informationTypesElement.Add(infoElement);
        }

        root.Add(pointsElement);
        root.Add(featuresElement);
        root.Add(informationTypesElement);

        return new XDocument(root);
    }

    private static XElement EmitPointRef(XElement pointsElement, ref int counter, double lat, double lon)
    {
        var pointId = $"p{++counter}";
        pointsElement.Add(new XElement("Point",
            new XAttribute("id", pointId),
            new XAttribute("lat", lat.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("lon", lon.ToString(CultureInfo.InvariantCulture))));
        return new XElement("Point", new XAttribute("ref", pointId));
    }
}
