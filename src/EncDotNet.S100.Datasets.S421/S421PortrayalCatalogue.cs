using EncDotNet.S100.Core;
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
    public override SpecRef Spec => new("S-421", default);
    protected override System.Xml.XmlResolver CreateXmlResolver(IReadOnlyDictionary<string, byte[]> registeredBytes) =>
        new FetchRuleFallbackXmlResolver(Provider, registeredBytes);
}
