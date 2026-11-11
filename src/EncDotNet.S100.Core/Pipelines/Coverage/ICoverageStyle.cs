namespace EncDotNet.S100.Pipelines.Coverage;

/// <summary>
/// How a gridded coverage (S-102, S-104, S-111, weather grids) is drawn: its
/// colour scheme, symbol scheme, contours and active palette. Consumed by
/// <see cref="CoveragePipeline"/>, which resolves the schemes against the
/// mariner settings and hands them, with the sampled grid, to the renderer.
/// </summary>
/// <remarks>
/// <para>
/// A style is not necessarily a portrayal catalogue. The S-102 and S-111
/// styles come from file-backed catalogues and also implement
/// <see cref="IPortrayalCatalogue"/>; the S-104 style and the weather styles
/// are defined in code and carry no specification or edition.
/// </para>
/// <para>
/// Call <see cref="SwitchPaletteAsync"/> before
/// <see cref="ResolveColorScheme"/> or <see cref="ResolveSymbolScheme"/>:
/// it selects the palette the resolved colours come from, and implementations
/// use it to pre-load the assets (palettes, rule scripts) the synchronous
/// resolve methods read from memory; the S-102 and S-111 styles throw
/// <see cref="InvalidOperationException"/> when resolving before that
/// pre-warm. <see cref="CoveragePipeline"/> switches to
/// <see cref="PaletteType.Day"/> itself when <see cref="ActivePalette"/> is
/// still empty, so direct callers of the resolve methods are the ones that
/// must honour this order.
/// </para>
/// <para>
/// A style instance is stateful (the active palette) and is not intended for
/// concurrent mutation.
/// </para>
/// </remarks>
public interface ICoverageStyle
{
    /// <summary>The currently active colour palette.</summary>
    /// <remarks>
    /// Implementations start at the empty <see cref="ColorPalette.Default"/>
    /// until the first <see cref="SwitchPaletteAsync"/> call; never
    /// <see langword="null"/>.
    /// </remarks>
    ColorPalette ActivePalette { get; }

    /// <summary>
    /// Switches the active colour palette to <paramref name="type"/> and
    /// pre-loads the assets the synchronous resolve methods need.
    /// </summary>
    /// <remarks>
    /// When the style has no palette for <paramref name="type"/>,
    /// implementations keep a usable palette active (the current one, or
    /// Day) rather than failing.
    /// </remarks>
    /// <param name="type">The palette mood (Day, Dusk, or Night).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a colour scheme for the value field this coverage carries,
    /// or <c>null</c> when the style specifies no coverage colour fill (e.g.
    /// S-111 Edition 2.0.0, whose portrayal catalogue defines arrow symbology
    /// only — see <c>content/S111/pc/Rules/select_arrow.xsl</c>).
    /// </summary>
    /// <param name="settings">
    /// Mariner display preferences that select the bands (e.g. the S-102
    /// safety, shallow and deep contours and the four-shade option).
    /// </param>
    /// <returns>
    /// A scheme whose band colours are already resolved against
    /// <see cref="ActivePalette"/>, or <c>null</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The implementation requires pre-loaded assets and
    /// <see cref="SwitchPaletteAsync"/> has not been awaited yet.
    /// </exception>
    CoverageColorScheme? ResolveColorScheme(MarinerSettings settings);

    /// <summary>
    /// Returns a symbol scheme for oriented overlay symbols (e.g. current arrows),
    /// or <c>null</c> if this style does not define one.
    /// </summary>
    /// <param name="settings">Mariner display preferences.</param>
    /// <returns>
    /// The symbol scheme, or <c>null</c>. The default implementation returns
    /// <c>null</c>.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// The implementation requires pre-loaded assets and
    /// <see cref="SwitchPaletteAsync"/> has not been awaited yet.
    /// </exception>
    CoverageSymbolScheme? ResolveSymbolScheme(MarinerSettings settings) => null;

    /// <summary>
    /// Contour line styles to draw over the coverage, one per contour value.
    /// Empty when the style defines no contours (the case for every bundled
    /// style today).
    /// </summary>
    IReadOnlyList<ContourStyle> Contours { get; }
}
