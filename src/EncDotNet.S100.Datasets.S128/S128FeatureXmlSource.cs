using System.Xml.Linq;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// Projects an <see cref="S128Dataset"/> into the S-100 Part 9 FeatureXML
/// neutral form consumed by the bundled S-128 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// Extends the base GML projection with xlink references on features,
/// recursive nested complex attributes, and an <c>InformationTypes</c>
/// section.
/// </remarks>
public sealed class S128FeatureXmlSource : GmlFeatureXmlSource<S128Feature>
{
    private readonly S128Dataset _dataset;

    /// <summary>Initializes a new <see cref="S128FeatureXmlSource"/> wrapping the given dataset.</summary>
    public S128FeatureXmlSource(S128Dataset dataset)
        : base(dataset.Features)
    {
        _dataset = dataset;
    }

    /// <inheritdoc/>
    protected override void WriteFeatureExtensions(S128Feature feature, XElement featureElement)
    {
        foreach (var r in feature.References)
        {
            featureElement.Add(new XElement(r.Role,
                new XAttribute("href", r.Href),
                new XAttribute("targetId", r.TargetId)));
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
                infoEl.Add(BuildS128Complex(complex));

            infoElement.Add(infoEl);
        }

        root.Add(infoElement);
    }

    /// <inheritdoc/>
    protected override XElement BuildComplexAttributeElement(IGmlComplexAttribute complex)
    {
        if (complex is S128ComplexAttribute s128Complex)
            return BuildS128Complex(s128Complex);

        return base.BuildComplexAttributeElement(complex);
    }

    private static XElement BuildS128Complex(S128ComplexAttribute complex)
    {
        var el = new XElement(complex.Code);
        foreach (var (k, v) in complex.SubAttributes)
            el.Add(new XElement(k, v));
        foreach (var nested in complex.NestedAttributes)
            el.Add(BuildS128Complex(nested));
        return el;
    }
}
