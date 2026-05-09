using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S421;

/// <summary>
/// S-421 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/> and uses
/// <see cref="GmlPortrayalCatalogueBase.FetchRuleFallbackXmlResolver"/>
/// to resolve unregistered sub-templates.
/// </summary>
public sealed class S421PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    public S421PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    public override string ProductSpec => "S-421";
    protected override System.Xml.XmlResolver CreateXmlResolver() =>
        new FetchRuleFallbackXmlResolver(Provider);
}
