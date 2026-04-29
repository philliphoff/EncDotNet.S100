namespace EncDotNet.S100.Pipelines.Coverage;

public interface ICoveragePortrayalCatalogue : IPortrayalCatalogue
{
    CoverageColorScheme ResolveColorScheme(MarinerSettings settings);

    /// <summary>
    /// Returns a symbol scheme for oriented overlay symbols (e.g. current arrows),
    /// or <c>null</c> if this catalogue does not define one.
    /// </summary>
    CoverageSymbolScheme? ResolveSymbolScheme(MarinerSettings settings) => null;

    IReadOnlyList<ContourStyle> Contours { get; }
}
