namespace EncDotNet.S100.Pipelines.Coverage;

public interface ICoveragePortrayalCatalogue : IPortrayalCatalogue
{
    CoverageColorScheme ResolveColorScheme(NavigationContext context);
    IReadOnlyList<ContourStyle> Contours { get; }
}
