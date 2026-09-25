using EncDotNet.S100.Core;

namespace EncDotNet.S100.Pipelines;

/// <summary>
/// Members shared by every product portrayal catalogue: the product it
/// portrays, its edition, and the active colour palette. Specialised by
/// <see cref="Vector.IVectorPortrayalCatalogue"/> for feature-based products
/// and <see cref="Coverage.ICoveragePortrayalCatalogue"/> for gridded
/// coverage products.
/// </summary>
/// <remarks>
/// A catalogue instance is stateful (active palette, and for vector
/// catalogues the viewing-group, display-mode and display-plane controllers)
/// and is not intended for concurrent mutation.
/// </remarks>
public interface IPortrayalCatalogue
{
    /// <summary>The product specification (name + edition) this catalogue targets.</summary>
    SpecRef Spec { get; }

    /// <summary>The edition of the underlying portrayal catalogue (matches <c>PortrayalCatalogue.Version</c>).</summary>
    string Edition { get; }

    /// <summary>The currently active colour palette.</summary>
    /// <remarks>
    /// Implementations start at the empty <see cref="ColorPalette.Default"/>
    /// until the first <see cref="SwitchPaletteAsync"/> call loads the
    /// catalogue's colour profile; never <see langword="null"/>.
    /// </remarks>
    ColorPalette ActivePalette { get; }

    /// <summary>
    /// Switches the active colour palette to <paramref name="type"/>.
    /// </summary>
    /// <remarks>
    /// Asynchronous because catalogues that load palettes lazily from
    /// <see cref="IAssetSource"/> may need to fetch the colour profile
    /// XML on first access. Cached implementations complete
    /// synchronously through the <see cref="ValueTask"/> fast path.
    /// When the catalogue has no palette for <paramref name="type"/>,
    /// implementations keep a usable palette active (the current one, or
    /// Day) rather than failing. Coverage catalogues also use this call to
    /// pre-load the assets their synchronous resolve methods need, so it must
    /// be awaited before those are called (see
    /// <see cref="Coverage.ICoveragePortrayalCatalogue"/>).
    /// </remarks>
    /// <param name="type">The palette mood (Day, Dusk, or Night).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default);
}
