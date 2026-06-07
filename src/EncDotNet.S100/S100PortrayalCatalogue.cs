using System;
using EncDotNet.S100.Core;

namespace EncDotNet.S100;

/// <summary>
/// A portrayal catalogue (S-100 Part 9) — the symbology rules used to render an
/// <see cref="S100Dataset"/>. Use <see cref="Bundled(string)"/> for the official
/// catalogue shipped in <c>EncDotNet.S100.Specifications</c>, or
/// <see cref="FromAssetSource(IAssetSource)"/> to supply your own.
/// </summary>
public sealed class S100PortrayalCatalogue
{
    private S100PortrayalCatalogue(IAssetSource? customSource) => CustomSource = customSource;

    /// <summary>
    /// The caller-supplied portrayal-catalogue asset source, or <c>null</c> when this
    /// is the bundled catalogue (resolved from the specifications package).
    /// </summary>
    internal IAssetSource? CustomSource { get; }

    /// <summary>
    /// The official portrayal catalogue bundled in
    /// <c>EncDotNet.S100.Specifications</c> for the given product specification.
    /// </summary>
    /// <param name="productSpec">Product specification name (e.g. <c>"S-101"</c>).</param>
    public static S100PortrayalCatalogue Bundled(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);
        return new S100PortrayalCatalogue(customSource: null);
    }

    /// <summary>
    /// A caller-supplied portrayal catalogue backed by <paramref name="source"/>
    /// (a folder or ZIP asset source containing the catalogue XML and its
    /// referenced rule, symbol, and palette assets).
    /// </summary>
    public static S100PortrayalCatalogue FromAssetSource(IAssetSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new S100PortrayalCatalogue(source);
    }
}
