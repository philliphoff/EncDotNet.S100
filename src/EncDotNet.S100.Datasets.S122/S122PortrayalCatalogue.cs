using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S122;

/// <summary>
/// S-122 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/>.
/// </summary>
public sealed class S122PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    public S122PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    public override SpecRef Spec => new("S-122", default);
}
