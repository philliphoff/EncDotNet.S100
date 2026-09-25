using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// S-128 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/>.
/// </summary>
public sealed class S128PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    /// <summary>
    /// Creates an S-128 portrayal catalogue backed by the given provider.
    /// </summary>
    /// <param name="provider">The portrayal catalogue provider that supplies rule files and assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
    public S128PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    /// <inheritdoc/>
    public override SpecRef Spec => new("S-128", default);
}
