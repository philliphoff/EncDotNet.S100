namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// A portrayal catalogue for gridded coverage products (S-102, S-104,
/// S-111). Consumed by <see cref="CoveragePipeline"/>, which resolves the
/// catalogue's colour and symbol schemes against the mariner settings and
/// hands them, with the sampled grid, to the renderer.
/// </summary>
/// <remarks>
/// Call <see cref="IPortrayalCatalogue.SwitchPaletteAsync"/> before
/// <see cref="ResolveColorScheme"/> or <see cref="ResolveSymbolScheme"/>:
/// it selects the palette the resolved colours come from, and implementations
/// use it to pre-load the catalogue assets (palettes, rule scripts) the
/// synchronous resolve methods read from memory; the S-102 and S-111
/// catalogues throw <see cref="InvalidOperationException"/> when resolving
/// before that pre-warm. <see cref="CoveragePipeline"/> switches to
/// <see cref="PaletteType.Day"/> itself when
/// <see cref="IPortrayalCatalogue.ActivePalette"/> is still empty, so direct
/// callers of the resolve methods are the ones that must honour this order.
/// </remarks>
public interface ICoveragePortrayalCatalogue : IPortrayalCatalogue
{
    /// <summary>
    /// Resolves a colour scheme for the value field this coverage carries,
    /// or <c>null</c> when the bundled portrayal catalogue does not
    /// specify a coverage colour fill (e.g. S-111 Edition 2.0.0, whose
    /// portrayal catalogue defines arrow symbology only — see
    /// <c>content/S111/pc/Rules/select_arrow.xsl</c>).
    /// </summary>
    /// <param name="settings">
    /// Mariner display preferences that select the bands (e.g. the S-102
    /// safety, shallow and deep contours and the four-shade option).
    /// </param>
    /// <returns>
    /// A scheme whose band colours are already resolved against
    /// <see cref="IPortrayalCatalogue.ActivePalette"/>, or <c>null</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The implementation requires pre-loaded assets and
    /// <see cref="IPortrayalCatalogue.SwitchPaletteAsync"/> has not been awaited yet.
    /// </exception>
    CoverageColorScheme? ResolveColorScheme(MarinerSettings settings);

    /// <summary>
    /// Returns a symbol scheme for oriented overlay symbols (e.g. current arrows),
    /// or <c>null</c> if this catalogue does not define one.
    /// </summary>
    /// <param name="settings">Mariner display preferences.</param>
    /// <returns>
    /// The symbol scheme, or <c>null</c>. The default implementation returns
    /// <c>null</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The implementation requires pre-loaded assets and
    /// <see cref="IPortrayalCatalogue.SwitchPaletteAsync"/> has not been awaited yet.
    /// </exception>
    CoverageSymbolScheme? ResolveSymbolScheme(MarinerSettings settings) => null;

    /// <summary>
    /// Contour line styles to draw over the coverage, one per contour depth.
    /// Empty when the catalogue defines no contours (the case for every
    /// bundled catalogue today).
    /// </summary>
    IReadOnlyList<ContourStyle> Contours { get; }
}
