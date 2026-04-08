using System.Xml;

namespace EncDotNet.S100.Pipelines.Vector;

/// <summary>
/// Provides S-100 Part 9 FeatureXML for vector portrayal pipeline consumption.
/// Implementations project dataset features (e.g. from ISO 8211 records) into
/// the XML intermediate form that XSLT portrayal rules operate on.
/// </summary>
public interface IFeatureXmlSource
{
    /// <summary>
    /// Returns the feature type codes present in the dataset, used to select
    /// applicable portrayal rules before running any XSLT transforms.
    /// </summary>
    IReadOnlyList<string> FeatureTypesPresent { get; }

    /// <summary>
    /// Returns an <see cref="XmlReader"/> positioned at the start of the
    /// S-100 FeatureXML document. The caller owns the reader and will dispose it.
    /// </summary>
    XmlReader GetFeatureXml();
}
