using System.Xml.Linq;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S201;

/// <summary>
/// Projects an <see cref="S201Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format consumed by the S-201 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// Extends the base GML projection with information references on
/// features (e.g. <c>AtoNStatus</c>, <c>Positioning</c>) and an
/// <c>InformationTypes</c> section that the bundled
/// <c>main_PaperChart.xsl</c> resolves via
/// <c>/Dataset/InformationTypes/*[@id=$ref]</c>. Feature-to-feature
/// xlink references (<c>theParentFeature</c> / <c>theSubordinateFeature</c>
/// from the S-201 Edition 2.0.0 <c>Structure/Equipment</c> aggregation)
/// are emitted as <c>&lt;Role featureRef="…"/&gt;</c> children so XSLT
/// rules can navigate equipment-on-structure relationships.
/// </remarks>
public sealed class S201FeatureXmlSource : GmlFeatureXmlSource<S201Feature>
{
    private readonly S201Dataset _dataset;

    /// <summary>Initializes a new <see cref="S201FeatureXmlSource"/>.</summary>
    public S201FeatureXmlSource(S201Dataset dataset)
        : base((dataset ?? throw new ArgumentNullException(nameof(dataset))).Features)
    {
        _dataset = dataset;
    }

    /// <inheritdoc/>
    protected override void WriteFeatureExtensions(S201Feature feature, XElement featureElement)
    {
        if (!feature.InformationReferences.IsDefaultOrEmpty)
        {
            foreach (var infoRef in feature.InformationReferences)
            {
                featureElement.Add(new XElement(infoRef.Role,
                    new XAttribute("informationRef", infoRef.InformationRef)));
            }
        }

        if (!feature.FeatureReferences.IsDefaultOrEmpty)
        {
            foreach (var featureRef in feature.FeatureReferences)
            {
                featureElement.Add(new XElement(featureRef.Role,
                    new XAttribute("featureRef", featureRef.TargetRef)));
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
