using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S127;

/// <summary>
/// S-127 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/>.
/// </summary>
public sealed class S127PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    public S127PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    public override SpecRef Spec => new("S-127", default);
}
