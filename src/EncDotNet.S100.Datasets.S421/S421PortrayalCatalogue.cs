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
    /// <summary>
    /// Creates an S-421 portrayal catalogue backed by the given provider.
    /// </summary>
    /// <param name="provider">The portrayal catalogue provider that supplies rule files and assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
    public S421PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    /// <inheritdoc/>
    public override SpecRef Spec => new("S-421", default);
    /// <inheritdoc/>
    protected override System.Xml.XmlResolver CreateXmlResolver(IReadOnlyDictionary<string, byte[]> registeredBytes) =>
        new FetchRuleFallbackXmlResolver(Provider, registeredBytes);
}
