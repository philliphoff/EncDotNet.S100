using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S124;

/// <summary>
/// S-124 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/>.
/// </summary>
public sealed class S124PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    public S124PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    public override SpecRef Spec => new("S-124", default);
}
