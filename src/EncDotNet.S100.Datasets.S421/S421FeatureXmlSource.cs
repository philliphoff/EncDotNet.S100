using System.Xml.Linq;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Datasets.S421;

/// <summary>
/// Projects an <see cref="S421Dataset"/> into the S-100 Part 9 FeatureXML
/// intermediate format for consumption by S-421 XSLT portrayal rules.
/// </summary>
/// <remarks>
/// Extends the base GML projection with xlink references on features
/// (e.g. <c>routeWaypoint</c> references between route objects).
/// </remarks>
public sealed class S421FeatureXmlSource : GmlFeatureXmlSource<S421Feature>
{
    /// <summary>
    /// Initializes a new <see cref="S421FeatureXmlSource"/> wrapping the given dataset.
    /// </summary>
    public S421FeatureXmlSource(S421Dataset dataset)
        : base(dataset.Features)
    {
    }

    /// <inheritdoc/>
    protected override void WriteFeatureExtensions(S421Feature feature, XElement featureElement)
    {
        foreach (var reference in feature.References)
        {
            featureElement.Add(new XElement(reference.Role,
                new XAttribute("href", reference.Href)));
        }
    }
}
