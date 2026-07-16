using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// S-128 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/>.
/// </summary>
public sealed class S128PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    public S128PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    public override SpecRef Spec => new("S-128", default);
}
