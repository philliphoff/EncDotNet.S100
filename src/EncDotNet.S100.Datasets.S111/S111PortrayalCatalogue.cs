using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;

namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Portrayal catalogue for S-111 Surface Currents.
/// Produces a color scheme mapping surface current speed to IHO-style color bands.
/// </summary>
public class S111PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    public string ProductSpec => "S-111";
    public string Edition => "1.0.0";
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    public void SwitchPalette(PaletteType type)
    {
        ActivePalette = ColorPalette.FromType(type);
    }

    public CoverageColorScheme ResolveColorScheme(NavigationContext context)
    {
        // Speed bands in knots, using a blue-to-red gradient to indicate
        // increasing current speed. These thresholds cover the typical range
        // of tidal/coastal currents.
        return new CoverageColorScheme
        {
            FieldName = "surfaceCurrentSpeed",
            Bands =
            [
                new ColorBand { MinValue = 0.0f,  MaxValue = 0.25f, Color = "#C9EDFF", Label = "0–0.25 kn" },
                new ColorBand { MinValue = 0.25f, MaxValue = 0.50f, Color = "#82CAFF", Label = "0.25–0.5 kn" },
                new ColorBand { MinValue = 0.50f, MaxValue = 1.0f,  Color = "#5BA3E6", Label = "0.5–1.0 kn" },
                new ColorBand { MinValue = 1.0f,  MaxValue = 1.5f,  Color = "#FFD966", Label = "1.0–1.5 kn" },
                new ColorBand { MinValue = 1.5f,  MaxValue = 2.0f,  Color = "#FFA54C", Label = "1.5–2.0 kn" },
                new ColorBand { MinValue = 2.0f,  MaxValue = 3.0f,  Color = "#FF6B4C", Label = "2.0–3.0 kn" },
                new ColorBand { MinValue = 3.0f,  MaxValue = float.MaxValue, Color = "#E63946", Label = "> 3.0 kn" },
            ]
        };
    }

    public IReadOnlyList<ContourStyle> Contours => [];
}
