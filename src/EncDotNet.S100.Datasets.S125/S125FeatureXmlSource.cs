using System.Xml.Linq;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S125;

/// <summary>
/// Projects an <see cref="S125Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format consumed by the S-125 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// Extends the base GML projection with information references on features
/// (e.g. <c>AtoNStatus</c>) and an <c>InformationTypes</c> section that the
/// bundled <c>main.xsl</c> resolves via
/// <c>/Dataset/InformationTypes/*[@id=$ref]</c>.
/// </remarks>
public sealed class S125FeatureXmlSource : GmlFeatureXmlSource<S125Feature>
{
    private readonly S125Dataset _dataset;

    /// <summary>Initializes a new <see cref="S125FeatureXmlSource"/>.</summary>
    public S125FeatureXmlSource(S125Dataset dataset)
        : base(dataset.Features)
    {
        _dataset = dataset;
    }

    /// <inheritdoc/>
    protected override void WriteFeatureExtensions(S125Feature feature, XElement featureElement)
    {
        if (!feature.InformationReferences.IsDefaultOrEmpty)
        {
            foreach (var infoRef in feature.InformationReferences)
            {
                featureElement.Add(new XElement(infoRef.Role,
                    new XAttribute("informationRef", infoRef.InformationRef)));
            }
        }
    }

    /// <inheritdoc/>
    protected override void WriteDatasetExtensions(XElement root)
    {
        var infoElement = new XElement("InformationTypes");

        foreach (var info in _dataset.InformationTypes)
        {
            var infoEl = new XElement(info.TypeCode, new XAttribute("id", info.Id));

            foreach (var (code, value) in info.Attributes)
                infoEl.Add(new XElement(code, value));

            foreach (var complex in info.ComplexAttributes)
                infoEl.Add(BuildComplexAttributeElement(complex));

            infoElement.Add(infoEl);
        }

        root.Add(infoElement);
    }
}
