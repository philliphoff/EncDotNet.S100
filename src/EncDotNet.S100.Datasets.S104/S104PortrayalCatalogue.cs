using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.S104;

/// <summary>
/// Default portrayal catalogue for S-104 Water Level data.
/// Provides a standalone heatmap color scheme with sensible height bands.
/// </summary>
/// <remarks>
/// S-104 Ed.2.0.0 does not define an official portrayal catalogue — the data is
/// intended for water level adjustment on ECDIS. This catalogue provides a
/// standalone visualization with height bands centred on zero (chart datum).
/// Replace with an official IHO portrayal catalogue if one is published.
/// </remarks>
public class S104PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    /// <summary>
    /// Default height bands (metres relative to chart datum) with a diverging
    /// blue (negative/below datum) → neutral → green/yellow (positive/above datum) scheme.
    /// </summary>
    private static readonly (float Min, float Max, string Color, string Label)[] DefaultBands =
    [
        (-5.0f,  -2.0f, "#08519C", "−5 to −2 m"),
        (-2.0f,  -1.0f, "#3182BD", "−2 to −1 m"),
        (-1.0f,  -0.5f, "#6BAED6", "−1 to −0.5 m"),
        (-0.5f,  -0.2f, "#9ECAE1", "−0.5 to −0.2 m"),
        (-0.2f,   0.0f, "#C6DBEF", "−0.2 to 0 m"),
        ( 0.0f,   0.2f, "#C7E9C0", "0 to 0.2 m"),
        ( 0.2f,   0.5f, "#A1D99B", "0.2 to 0.5 m"),
        ( 0.5f,   1.0f, "#74C476", "0.5 to 1 m"),
        ( 1.0f,   2.0f, "#31A354", "1 to 2 m"),
        ( 2.0f,   5.0f, "#006D2C", "2 to 5 m"),
    ];

    public string ProductSpec => "S-104";
    public string Edition => "2.0.0";
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    public void SwitchPalette(PaletteType type)
    {
        ActivePalette = ColorPalette.FromType(type);
    }

    public CoverageColorScheme ResolveColorScheme(NavigationContext context)
    {
        var colorBands = new List<ColorBand>(DefaultBands.Length);

        foreach (var (min, max, color, label) in DefaultBands)
        {
            colorBands.Add(new ColorBand
            {
                MinValue = min,
                MaxValue = max,
                Color = color,
                Label = label,
            });
        }

        return new CoverageColorScheme
        {
            FieldName = "waterLevelHeight",
            Bands = colorBands,
        };
    }

    public IReadOnlyList<ContourStyle> Contours => [];
}
