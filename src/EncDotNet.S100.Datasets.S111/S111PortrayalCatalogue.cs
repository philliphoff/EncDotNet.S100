using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Portrayal catalogue for S-111 Surface Currents (Ed 2.0.0).
/// Loads the official IHO colour profile (Day/Dusk/Night) and the
/// surface-current speed-band table from the bundled portrayal
/// catalogue assets.
/// </summary>
/// <remarks>
/// <para>
/// Palettes are cached on the shared <see cref="IPortrayalAssetCache"/>
/// (PR-3 / PR-4 of the asset-caching audit). The parsed speed-band
/// table is cached on the catalogue instance itself rather than on the
/// shared cache: the table is the only consumer, the parser runs once
/// per catalogue lifetime, and a per-instance field avoids growing
/// <see cref="IPortrayalAssetCache"/>'s public surface for a
/// single-spec asset.
/// </para>
/// <para>
/// The speed-band table, symbol references and the three scale
/// constants are parsed from <c>Rules/select_arrow.xsl</c> via
/// <see cref="S111SpeedBandReader"/>; no spec-derived constants are
/// hard-coded in this catalogue.
/// </para>
/// </remarks>
public class S111PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    private readonly PortrayalCatalogueProvider _provider;
    private readonly IPortrayalAssetCache _cache;

    /// <summary>
    /// Per-instance cache of the parsed speed-band table. Sticky once
    /// <see cref="_bandsLoaded"/> flips to <c>true</c>. See the
    /// type-level remarks for the per-instance vs. shared-cache
    /// decision.
    /// </summary>
    private IReadOnlyList<S111SpeedBandReader.SpeedBand>? _bands;
    private bool _bandsLoaded;

    private const string SpeedFieldName = "surfaceCurrentSpeed";
    private const string DirectionFieldName = "surfaceCurrentDirection";
    private const string ProductTag = "S-111";

    /// <summary>
    /// Creates a catalogue backed by a real portrayal catalogue provider.
    /// Colours come from the bundled colour profile; speed bands from
    /// the bundled XSLT rules.
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

        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Palette);
        ActivePalette = palette;
    }

    /// <summary>
    /// Returns <c>null</c>: the bundled S-111 Edition 2.0.0 portrayal
    /// catalogue defines arrow symbology only — see
    /// <c>content/S111/pc/Rules/select_arrow.xsl</c>, which emits a
    /// <c>&lt;coverageInstruction&gt;</c> with per-band <c>&lt;symbol&gt;</c>
    /// elements and no <c>&lt;color&gt;</c> child. Overlaying a synthetic
    /// speed-band colour fill on top of S-101 ENC obscures the base
    /// chart, so this catalogue intentionally does not produce one.
    /// Renderers consult <see cref="ResolveSymbolScheme"/> instead.
    /// </summary>
    public CoverageColorScheme? ResolveColorScheme(MarinerSettings settings)
    {
        // Touch the palette / band table so callers still pay the
        // one-time load cost they previously did (keeps the asset-cache
        // metrics and the PR-4 contract — see S111PaletteCacheTests —
        // unchanged when SwitchPalette is the first call).
        EnsurePalettesLoaded();
        _ = EnsureSpeedBandsLoaded();
        return null;
    }

    public CoverageSymbolScheme ResolveSymbolScheme(MarinerSettings settings)
    {
        var bands = EnsureSpeedBandsLoaded();

        var symbolBands = new List<SymbolBand>(bands.Count);
        foreach (var band in bands)
        {
            symbolBands.Add(new SymbolBand
            {
                MinValue = band.Min,
                MaxValue = band.Max,
                SymbolRef = band.SymbolRef,
                ScaleByValue = band.ScaleByValue,
                ScaleFactor = band.ScaleFactor,
                Label = band.Label,
            });
        }

        return new CoverageSymbolScheme
        {
            ValueFieldName = SpeedFieldName,
            RotationFieldName = DirectionFieldName,
            Bands = symbolBands,
        };
    }

    public IReadOnlyList<ContourStyle> Contours => [];

    // ── Palettes ───────────────────────────────────────────────────────

    /// <summary>
    /// Eager-loads the Day/Dusk/Night palettes from the S-111 colour-profile
    /// asset into the shared <see cref="IPortrayalAssetCache.Palettes"/> on
    /// first access. Subsequent calls are a no-op thanks to the sticky
    /// <see cref="IPortrayalAssetCache.PalettesLoaded"/> flag.
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

    // ── Speed bands ────────────────────────────────────────────────────

    /// <summary>
    /// Lazily parses the speed-band table from
    /// <c>Rules/select_arrow.xsl</c> on first access; sticky on
    /// <see cref="_bandsLoaded"/>.
    /// </summary>
    private IReadOnlyList<S111SpeedBandReader.SpeedBand> EnsureSpeedBandsLoaded()
    {
        if (_bandsLoaded && _bands is not null)
        {
            return _bands;
        }

        var ruleFile = _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.FileName.Contains("select_arrow", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "S-111 portrayal catalogue does not contain a select_arrow rule file.");

        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
        _bands = S111SpeedBandReader.Read(stream);
        _bandsLoaded = true;
        return _bands;
    }

}
