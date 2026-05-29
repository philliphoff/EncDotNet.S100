namespace EncDotNet.S100.Pipelines.Coverage;

public interface ICoveragePortrayalCatalogue : IPortrayalCatalogue
{
    /// <summary>
    /// Resolves a colour scheme for the value field this coverage carries,
    /// or <c>null</c> when the bundled portrayal catalogue does not
    /// specify a coverage colour fill (e.g. S-111 Edition 2.0.0, whose
    /// portrayal catalogue defines arrow symbology only — see
    /// <c>content/S111/pc/Rules/select_arrow.xsl</c>).
    /// </summary>
    CoverageColorScheme? ResolveColorScheme(MarinerSettings settings);

    /// <summary>
    /// Returns a symbol scheme for oriented overlay symbols (e.g. current arrows),
    /// or <c>null</c> if this catalogue does not define one.
    /// </summary>
    CoverageSymbolScheme? ResolveSymbolScheme(MarinerSettings settings) => null;

    IReadOnlyList<ContourStyle> Contours { get; }
}
