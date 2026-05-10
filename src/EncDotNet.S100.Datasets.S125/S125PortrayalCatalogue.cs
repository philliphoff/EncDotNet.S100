using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S125;

/// <summary>
/// S-125 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/> and uses
/// <see cref="GmlPortrayalCatalogueBase.FetchRuleFallbackXmlResolver"/>
/// to resolve unregistered sub-templates.
/// </summary>
public sealed class S125PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    public S125PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    public override SpecRef Spec => new("S-125", default);
    protected override System.Xml.XmlResolver CreateXmlResolver() =>
        new FetchRuleFallbackXmlResolver(Provider);
}
