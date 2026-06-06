using EncDotNet.S100.Core;

namespace EncDotNet.S100.Pipelines;

public interface IPortrayalCatalogue
{
    /// <summary>The product specification (name + edition) this catalogue targets.</summary>
    SpecRef Spec { get; }

    /// <summary>The edition of the underlying portrayal catalogue (matches <see cref="PortrayalCatalogue.Version"/>).</summary>
    string Edition { get; }

    /// <summary>The currently active colour palette.</summary>
    ColorPalette ActivePalette { get; }

    /// <summary>
    /// Switches the active colour palette to <paramref name="type"/>.
    /// </summary>
    /// <remarks>
    /// Asynchronous because catalogues that load palettes lazily from
    /// <see cref="IAssetSource"/> may need to fetch the colour profile
    /// XML on first access. Cached implementations complete
    /// synchronously through the <see cref="ValueTask"/> fast path.
    /// </remarks>
    /// <param name="type">The palette mood (Day, Dusk, or Night).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default);
}
 