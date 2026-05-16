using System.Globalization;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S102;

/// <summary>
/// S-102 portrayal catalogue that executes the bundled
/// <c>BathymetryCoverage.lua</c> rule (S-102 Edition 3.0.0, Annex B)
/// via an <see cref="ILuaEngine"/> and resolves the emitted colour
/// tokens against the bundled Day / Dusk / Night palettes
/// (<c>ColorProfiles/colorProfile.xml</c>, S-100 Part 9 §11).
/// </summary>
/// <remarks>
/// <para>
/// The bundled Lua rule emits one <c>CoverageColor:&lt;TOKEN&gt;;LookupEntry:&lt;label&gt;,&lt;min&gt;,&lt;max&gt;,&lt;interval&gt;</c>
/// instruction per depth band. The bands depend on the four S-102
/// context parameters supplied via <see cref="MarinerSettings"/>:
/// <c>FourShades</c>, <c>SafetyContour</c>, <c>ShallowContour</c>,
/// <c>DeepContour</c>. Invariants
/// (<c>ShallowContour ≤ SafetyContour ≤ DeepContour</c>) are clamped
/// rather than throwing — the viewer surface area is a settings panel
/// and bad inputs must not crash the render pipeline.
/// </para>
/// <para>
/// Cells whose depth equals <see cref="S102CoverageSource.FillValue"/>
/// resolve to the palette's <c>NODTA</c> token via
/// <see cref="CoverageColorScheme.NoDataColor"/>.
/// </para>
/// </remarks>
public class S102PortrayalCatalogue : ICoveragePortrayalCatalogue
{
    // Last-resort fallback if the bundled colour profile cannot be
    // loaded. These are the sRGB values from the official S-102 Day
    // palette so that a renderer always has *some* spec-aligned colours
    // for the five depth tokens (and a grey for NoData). They are
    // only used when palette loading fails entirely.
    private static readonly Dictionary<string, string> FallbackDayTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DEPDW"] = "#C9EDFF",
        ["DEPMD"] = "#A7D9FB",
        ["DEPMS"] = "#82CAFF",
        ["DEPVS"] = "#61B7FF",
        ["DEPIT"] = "#58AF9C",
        ["NODTA"] = "#A3B4B7",
    };

    private const string ProductTag = "S-102";

    private readonly ILuaEngine _luaEngine;
    private readonly PortrayalCatalogueProvider _provider;
    private readonly IPortrayalAssetCache _cache;
    private string? _cachedLuaSource;

    /// <summary>
    /// Creates a catalogue backed by Lua script execution.
    /// </summary>
    /// <param name="luaEngine">The Lua engine used to run portrayal scripts.</param>
    /// <param name="provider">The portrayal catalogue provider that supplies script assets.</param>
    public S102PortrayalCatalogue(ILuaEngine luaEngine, PortrayalCatalogueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(luaEngine);
        ArgumentNullException.ThrowIfNull(provider);
        _luaEngine = luaEngine;
        _provider = provider;
        _cache = provider.AssetCache;
    }

    public SpecRef Spec => new("S-102", default);
    public string Edition => "3.0.0";

    /// <summary>The identity of the underlying portrayal catalogue XML, when available.</summary>
    public CatalogueRef? CatalogueRef => _provider.Catalogue.CatalogueRef;

    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    public void SwitchPalette(PaletteType type)
    {
        EnsurePalettesLoaded();

        if (_cache.Palettes.TryGetValue(type, out var palette))
        {
            ActivePalette = palette;
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(
                ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Palette);
        }
        else
        {
            // No palette available for the requested mood — keep
            // whichever palette is currently active (Day, if loaded)
            // rather than wiping back to the empty Default.
        }
    }

    public CoverageColorScheme ResolveColorScheme(MarinerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        EnsurePalettesLoaded();

        var (fourShades, safetyContour, shallowContour, deepContour) = ClampParameters(settings);

        using var lua = _luaEngine.CreateContext();

        var instructions = new List<string>();

        // The four S-102 PC context parameters
        // (S-102 Edition 3.0.0 §portrayal_catalogue.xml §context).
        var contextParams = new Dictionary<string, object?>
        {
            ["FourShades"] = fourShades,
            ["SafetyContour"] = safetyContour,
            ["ShallowContour"] = shallowContour,
            ["DeepContour"] = deepContour,
            ["SafetyDepth"] = settings.SafetyDepth,
        };
        lua.SetGlobal("contextParameters", contextParams);

        // Stub featurePortrayal.AddInstructions that captures every
        // emitted drawing instruction; the script invokes it via
        // `featurePortrayal:AddInstructions(str)` (Lua method-call
        // sugar — translates to AddInstructions(obj, str)).
        lua.SetGlobal("_addInstructions",
            (Action<string>)(instr => instructions.Add(instr)));

        lua.Execute("""
            featurePortrayal = {}
            function featurePortrayal:AddInstructions(instr)
                _addInstructions(instr)
            end
            feature = { Code = 'BathymetryCoverage' }
            """);

        string luaSource = GetLuaSource();
        lua.Execute(luaSource);

        // Invoke BathymetryCoverage(feature, featurePortrayal, contextParameters)
        // with the three globals we set up above.
        lua.Execute("BathymetryCoverage(feature, featurePortrayal, contextParameters)");

        return ParseDrawingInstructions(instructions);
    }

    public IReadOnlyList<ContourStyle> Contours => [];

    // ── Palettes ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the bundled Day / Dusk / Night palettes from the S-102
    /// colour profile manifest (S-102 §Annex B; S-100 Part 9 §11). Mirrors
    /// the pattern used by <c>S101PortrayalCatalogue.EnsurePalettesLoaded</c>:
    /// scans <c>PortrayalCatalogue.ColorProfiles</c>, fetches each via
    /// <see cref="PortrayalCatalogueProvider.FetchAssetAsync(CatalogItem, string, System.Threading.CancellationToken)"/>
    /// from the conventional <c>ColorProfiles/</c> subdirectory, and
    /// stores parsed palettes on the shared
    /// <see cref="IPortrayalAssetCache"/>. The S-102 colour profile
    /// uses the multi-palette form
    /// (<c>&lt;palette name="Day|Dusk|Night"&gt;</c>) so a single
    /// manifest entry yields all three palettes.
    /// </summary>
    private void EnsurePalettesLoaded()
    {
        if (_cache.PalettesLoaded)
        {
            if (ActivePalette.Colors.Count == 0 &&
                _cache.Palettes.TryGetValue(PaletteType.Day, out var dayPalette))
            {
                ActivePalette = dayPalette;
            }
            return;
        }
        _cache.PalettesLoaded = true;

        foreach (var item in _provider.Catalogue.ColorProfiles)
        {
            var manifestName = item.Description.Name;
            if (string.IsNullOrEmpty(manifestName))
            {
                manifestName = Path.GetFileNameWithoutExtension(item.FileName);
            }

            var paletteType = manifestName switch
            {
                var n when n.Contains("Day", StringComparison.OrdinalIgnoreCase) => PaletteType.Day,
                var n when n.Contains("Dusk", StringComparison.OrdinalIgnoreCase) => PaletteType.Dusk,
                var n when n.Contains("Night", StringComparison.OrdinalIgnoreCase) => PaletteType.Night,
                _ => (PaletteType?)null,
            };

            if (paletteType is not null)
            {
                try
                {
                    using var stream = _provider.FetchAssetAsync(item, "ColorProfiles").GetAwaiter().GetResult();
                    var palette = ColorProfileReader.Read(stream, manifestName);
                    _cache.Palettes[paletteType.Value] = palette;
                }
                catch (Exception)
                {
                    // Skip gracefully — fall through to fallback colours
                    // at instruction-parse time.
                }
            }
            else
            {
                // Single manifest entry that contains multiple named
                // palettes (the S-102 layout). Try Day / Dusk / Night.
                foreach (var (type, name) in new[] { (PaletteType.Day, "Day"), (PaletteType.Dusk, "Dusk"), (PaletteType.Night, "Night") })
                {
                    if (_cache.Palettes.ContainsKey(type)) continue;
                    try
                    {
                        using var stream = _provider.FetchAssetAsync(item, "ColorProfiles").GetAwaiter().GetResult();
                        var palette = ColorProfileReader.Read(stream, name);
                        if (palette.Colors.Count > 0)
                        {
                            _cache.Palettes[type] = palette;
                        }
                    }
                    catch (Exception)
                    {
                        // Skip gracefully.
                    }
                }
            }
        }

        if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayFinal))
        {
            ActivePalette = dayFinal;
        }
    }

    // ── Context parameters ─────────────────────────────────────────────

    /// <summary>
    /// Pulls the four S-102 context parameters from
    /// <see cref="MarinerSettings"/> and clamps them so the resulting
    /// band layout is monotonic (Shallow ≤ Safety ≤ Deep). Bad input
    /// is logged via <see cref="System.Diagnostics.Debug.WriteLine(string)"/>
    /// — we never throw because these values originate in viewer
    /// sliders where transient out-of-order states are normal.
    /// </summary>
    private static (bool FourShades, double Safety, double Shallow, double Deep) ClampParameters(MarinerSettings settings)
    {
        double safety = settings.SafetyContour;
        double shallow = settings.ShallowContour;
        double deep = settings.DeepContour;

        bool clamped = false;
        if (shallow > safety)
        {
            shallow = safety;
            clamped = true;
        }
        if (deep < safety)
        {
            deep = safety;
            clamped = true;
        }

        if (clamped)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[S-102 portrayal] Invariant Shallow ≤ Safety ≤ Deep violated " +
                $"(Shallow={settings.ShallowContour}, Safety={settings.SafetyContour}, " +
                $"Deep={settings.DeepContour}); clamped to ({shallow}, {safety}, {deep}).");
        }

        return (settings.FourShades, safety, shallow, deep);
    }

    // ── Lua source ─────────────────────────────────────────────────────

    private string GetLuaSource()
    {
        if (_cachedLuaSource is not null)
        {
            return _cachedLuaSource;
        }

        var ruleFile = _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.FileName.Contains("BathymetryCoverage", StringComparison.OrdinalIgnoreCase));

        if (ruleFile is null)
        {
            throw new InvalidOperationException(
                "The S-102 portrayal catalogue does not contain a BathymetryCoverage rule file.");
        }

        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        _cachedLuaSource = reader.ReadToEnd();
        return _cachedLuaSource;
    }

    // ── Drawing-instruction parser ─────────────────────────────────────

    /// <summary>
    /// Parses the drawing instruction strings emitted by
    /// <c>BathymetryCoverage.lua</c> into a
    /// <see cref="CoverageColorScheme"/>.
    /// </summary>
    /// <remarks>
    /// Each emitted instruction is a semicolon-separated directive list, e.g.:
    /// <code>
    ///   CoverageColor:DEPVS,0;LookupEntry:Shallow Water,0,30,geLtInterval;CoverageFill:depth
    ///   CoverageColor:DEPIT,0;LookupEntry:Intertidal,,0,ltSemiInterval
    /// </code>
    /// <para>
    /// Empty <c>min</c> means the band extends to negative infinity
    /// (<c>ltSemiInterval</c> — drying / intertidal). Empty <c>max</c>
    /// means the band extends to positive infinity
    /// (<c>geSemiInterval</c> — deep water). The <c>≥ min … &lt; max</c>
    /// inequality used here matches the rule's <c>geLtInterval</c>
    /// semantics: a depth value exactly equal to <c>SafetyContour</c>
    /// falls in the band whose lower bound is <c>SafetyContour</c>.
    /// </para>
    /// <para>
    /// The active palette's <c>NODTA</c> token (if any) is surfaced via
    /// <see cref="CoverageColorScheme.NoDataColor"/> so the renderer
    /// can paint S-102 fill cells (<see cref="S102CoverageSource.FillValue"/>)
    /// rather than leaving them transparent.
    /// </para>
    /// </remarks>
    private CoverageColorScheme ParseDrawingInstructions(List<string> instructions)
    {
        var bands = new List<ColorBand>();
        string? fieldName = "depth";

        foreach (var instruction in instructions)
        {
            string? currentToken = null;
            string? label = null;
            float minValue = float.NegativeInfinity;
            float maxValue = float.PositiveInfinity;

            var directives = instruction.Split(';');
            foreach (var directive in directives)
            {
                var colonIdx = directive.IndexOf(':');
                if (colonIdx < 0) continue;

                var key = directive[..colonIdx];
                var value = directive[(colonIdx + 1)..];

                switch (key)
                {
                    case "CoverageColor":
                    {
                        // CoverageColor:TOKEN,transparency
                        var parts = value.Split(',');
                        currentToken = parts[0];
                        break;
                    }
                    case "LookupEntry":
                    {
                        // LookupEntry:label,min,max,intervalType
                        // min/max may be empty for semi-intervals.
                        var parts = value.Split(',');
                        label = parts.Length > 0 ? parts[0] : null;

                        if (parts.Length > 1 && !string.IsNullOrEmpty(parts[1]))
                            minValue = float.Parse(parts[1], CultureInfo.InvariantCulture);

                        if (parts.Length > 2 && !string.IsNullOrEmpty(parts[2]))
                            maxValue = float.Parse(parts[2], CultureInfo.InvariantCulture);

                        break;
                    }
                    case "CoverageFill":
                    {
                        fieldName = value;
                        break;
                    }
                }
            }

            if (currentToken is not null)
            {
                bands.Add(new ColorBand
                {
                    MinValue = minValue,
                    MaxValue = maxValue,
                    Color = ResolveColorToken(currentToken),
                    Label = label,
                });
            }
        }

        // Resolve NODTA via the active palette (Day-palette grey by
        // default in the bundled colour profile). If the palette has
        // not loaded for any reason, fall back to the S-102 Day NODTA.
        var noDataColor = ResolveColorToken("NODTA");

        return new CoverageColorScheme
        {
            FieldName = fieldName ?? "depth",
            Bands = bands,
            NoDataColor = noDataColor,
        };
    }

    private string ResolveColorToken(string token)
    {
        if (ActivePalette.TryResolve(token, out var hex))
            return hex;

        return FallbackDayTokens.TryGetValue(token, out var fallback) ? fallback : "#000000";
    }
}
