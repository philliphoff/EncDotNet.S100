using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S124;

/// <summary>
/// S-124 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/>.
/// </summary>
public sealed class S124PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    /// <summary>
    /// Creates an S-124 portrayal catalogue backed by the given provider.
    /// </summary>
    /// <param name="provider">The portrayal catalogue provider that supplies rule files and assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
    public S124PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    /// <inheritdoc/>
    public override SpecRef Spec => new("S-124", default);
}
