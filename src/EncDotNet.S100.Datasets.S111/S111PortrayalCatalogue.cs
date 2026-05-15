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

    // PR-4 (asset-caching audit §6): palette storage lives on the
    // provider's IPortrayalAssetCache. The bundled S-111 colour profile
    // is a single XML file that contains all three palettes
    // (Day/Dusk/Night), so on first access we eager-load all three into
    // the shared cache (gated by IPortrayalAssetCache.PalettesLoaded).
    // Two S-111 catalogue instances that share a provider — or two
    // providers sharing a PortrayalCatalogueManager-owned cache for
    // SpecRef("S-111", _) — therefore open the colour-profile asset at
    // most once.
    //
    // Thread-safety: PortrayalAssetCache uses non-concurrent
    // dictionaries; PR-6 of the audit tracks hardening.
    private readonly IPortrayalAssetCache _cache;

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
        _cache = provider.AssetCache;
    }

    public SpecRef Spec => new("S-111", default);
    public string Edition => _provider.Catalogue.Version ?? "2.0.0";

    /// <summary>The identity of the underlying portrayal catalogue XML, when available.</summary>
    public CatalogueRef? CatalogueRef => _provider.Catalogue.CatalogueRef;
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    public void SwitchPalette(PaletteType type)
    {
        EnsurePalettesLoaded();

        if (!_cache.Palettes.TryGetValue(type, out var palette))
        {
            throw new KeyNotFoundException($"Color palette '{type}' not found in the S-111 portrayal catalogue.");
        }

        ActivePalette = palette;
    }

    public CoverageColorScheme ResolveColorScheme(MarinerSettings settings)
    {
        EnsurePalettesLoaded();

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

    /// <summary>
    /// Eager-loads the Day/Dusk/Night palettes from the S-111 colour-profile
    /// asset into the shared <see cref="IPortrayalAssetCache.Palettes"/> on
    /// first access. Subsequent calls are a no-op thanks to the sticky
    /// <see cref="IPortrayalAssetCache.PalettesLoaded"/> flag (PR-3). The
    /// S-111 portrayal catalogue ships a single <c>colorProfile.xml</c>
    /// that contains all three palettes, so we read it once and pull each
    /// palette out by name in turn — opening the asset stream per palette
    /// because <see cref="ColorProfileReader.Read(Stream, string)"/>
    /// consumes its argument.
    /// </summary>
    private void EnsurePalettesLoaded()
    {
        if (_cache.PalettesLoaded)
        {
            if (ActivePalette.Colors.Count == 0
                && _cache.Palettes.TryGetValue(PaletteType.Day, out var cachedDay))
            {
                ActivePalette = cachedDay;
            }
            return;
        }

        var colorProfileItem = _provider.Catalogue.ColorProfiles.FirstOrDefault()
            ?? throw new InvalidOperationException("S-111 portrayal catalogue does not contain a color profile.");

        foreach (var (type, name) in new[]
        {
            (PaletteType.Day, "Day"),
            (PaletteType.Dusk, "Dusk"),
            (PaletteType.Night, "Night"),
        })
        {
            using var stream = _provider.FetchAssetAsync(colorProfileItem, "ColorProfiles")
                .GetAwaiter().GetResult();
            var palette = ColorProfileReader.Read(stream, name);
            if (palette.Colors.Count > 0)
            {
                _cache.Palettes[type] = palette;
            }
        }

        _cache.PalettesLoaded = true;

        if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayPalette))
        {
            ActivePalette = dayPalette;
        }
    }
}
