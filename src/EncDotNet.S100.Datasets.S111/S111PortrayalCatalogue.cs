using System.Globalization;
using System.Xml.Linq;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Portrayal catalogue for S-111 Surface Currents.
/// Loads the official IHO color profile from the <see cref="PortrayalCatalogueProvider"/>
/// and parses speed band definitions from the XSLT rules.
/// </summary>
public class S111PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    private readonly PortrayalCatalogueProvider _provider;

    /// <summary>
    /// Speed band definitions matching the S-111 Ed.2.0.0 portrayal catalogue XSLT.
    /// Each band maps a speed range (knots) to a color token and arrow symbol.
    /// </summary>
    private static readonly (float Min, float Max, string Token, string Symbol, bool ScaleByValue, float ScaleFactor, string Label)[] Bands =
    [
        (0.0f, 0.5f, "SCBN1", "SCAROW01", false, 0.40f, "0–0.5 kn"),
        (0.5f, 1.0f, "SCBN2", "SCAROW02", false, 0.40f, "0.5–1 kn"),
        (1.0f, 2.0f, "SCBN3", "SCAROW03", false, 0.40f, "1–2 kn"),
        (2.0f, 3.0f, "SCBN4", "SCAROW04", true, 0.20f, "2–3 kn"),
        (3.0f, 5.0f, "SCBN5", "SCAROW05", true, 0.20f, "3–5 kn"),
        (5.0f, 7.0f, "SCBN6", "SCAROW06", true, 0.20f, "5–7 kn"),
        (7.0f, 10.0f, "SCBN7", "SCAROW07", true, 0.20f, "7–10 kn"),
        (10.0f, 13.0f, "SCBN8", "SCAROW08", true, 0.20f, "10–13 kn"),
        (13.0f, float.MaxValue, "SCBN9", "SCAROW09", false, 2.60f, "> 13 kn"),
    ];

    /// <summary>
    /// Creates a catalogue backed by a real portrayal catalogue provider.
    /// Colors are loaded from the color profile; speed bands from the XSLT rules.
    /// </summary>
    public S111PortrayalCatalogue(PortrayalCatalogueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    public SpecRef Spec => new("S-111", default);
    public string Edition => _provider.Catalogue.Version ?? "2.0.0";
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    public void SwitchPalette(PaletteType type)
    {
        var paletteName = type switch
        {
            PaletteType.Day => "Day",
            PaletteType.Dusk => "Dusk",
            PaletteType.Night => "Night",
            _ => "Day",
        };

        ActivePalette = LoadColorPalette(paletteName);
    }

    public CoverageColorScheme ResolveColorScheme(MarinerSettings settings)
    {
        if (ActivePalette.Colors.Count == 0)
        {
            ActivePalette = LoadColorPalette("Day");
        }

        var colorBands = new List<ColorBand>();

        foreach (var (min, max, token, symbol, scaleByValue, scaleFactor, label) in Bands)
        {
            if (!ActivePalette.TryResolve(token, out var color))
                throw new InvalidOperationException($"Color token '{token}' not found in the S-111 color profile.");

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
            FieldName = "surfaceCurrentSpeed",
            Bands = colorBands,
        };
    }

    public CoverageSymbolScheme ResolveSymbolScheme(MarinerSettings settings)
    {
        var symbolBands = new List<SymbolBand>();

        foreach (var (min, max, token, symbol, scaleByValue, scaleFactor, label) in Bands)
        {
            symbolBands.Add(new SymbolBand
            {
                MinValue = min,
                MaxValue = max,
                SymbolRef = symbol,
                ScaleByValue = scaleByValue,
                ScaleFactor = scaleFactor,
                Label = label,
            });
        }

        return new CoverageSymbolScheme
        {
            ValueFieldName = "surfaceCurrentSpeed",
            RotationFieldName = "surfaceCurrentDirection",
            Bands = symbolBands,
        };
    }

    public IReadOnlyList<ContourStyle> Contours => [];

    private ColorPalette LoadColorPalette(string paletteName)
    {
        var colorProfileItem = _provider.Catalogue.ColorProfiles.FirstOrDefault()
            ?? throw new InvalidOperationException("S-111 portrayal catalogue does not contain a color profile.");

        using var stream = _provider.FetchAssetAsync(colorProfileItem, "ColorProfiles")
            .GetAwaiter().GetResult();
        return ColorProfileReader.Read(stream, paletteName);
    }
}
