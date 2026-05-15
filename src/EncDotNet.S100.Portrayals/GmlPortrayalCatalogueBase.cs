using System.Diagnostics;
using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Core;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// Base class for XSLT-based GML portrayal catalogues. Provides all the
/// shared infrastructure — palette loading, rule discovery, XSLT compilation
/// with caching, symbol/line-style/area-fill asset loading, and the XML
/// resolver for <c>xsl:include</c> resolution.
/// </summary>
/// <remarks>
/// Subclasses typically only need to supply <see cref="Spec"/> and,
/// where necessary, override <see cref="CreateXmlResolver"/> (for specs
/// whose XSLT includes reference unregistered sub-templates) or
/// <see cref="GetCompiledRule"/> (for specs that inject an adapter rule).
/// </remarks>
public abstract class GmlPortrayalCatalogueBase : IVectorPortrayalCatalogue
{
    private readonly PortrayalCatalogueProvider _provider;

    // PR-3 (asset-caching audit §6): every GML-XSLT catalogue subclass
    // (S-122, S-124, S-125, S-127, S-128, S-129, S-201, S-411, S-421)
    // routes its decoded XSLT / SVG / line-style / area-fill / palette
    // storage through the provider's IPortrayalAssetCache. When
    // PortrayalCatalogueManager owns the cache, two open datasets of
    // the same spec pay each underlying asset open at most once.
    //
    // Thread-safety: PortrayalAssetCache uses non-concurrent
    // dictionaries. Today each GML dataset processor reads and writes
    // these slots on a single pipeline thread per dataset; two
    // pipelines running concurrently against catalogues that share a
    // manager-owned cache would race. PR-6 of the audit tracks
    // hardening to ConcurrentDictionary.
    private readonly IPortrayalAssetCache _cache;

    private IReadOnlyList<PortrayalRule>? _rules;

    /// <summary>
    /// Initializes a new <see cref="GmlPortrayalCatalogueBase"/> backed by
    /// the given provider.
    /// </summary>
    protected GmlPortrayalCatalogueBase(PortrayalCatalogueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _cache = provider.AssetCache;
        DisplayModeMembership.Bind(DisplayModes, ViewingGroups, _provider.Catalogue);
    }

    /// <summary>Gets the underlying portrayal catalogue provider.</summary>
    protected PortrayalCatalogueProvider Provider => _provider;

    /// <summary>The product specification (name + edition) this catalogue targets.</summary>
    public abstract SpecRef Spec { get; }

    /// <summary>Gets the edition of the portrayal catalogue.</summary>
    public string Edition => _provider.Catalogue.Version;

    /// <summary>
    /// The identity (name + version) of the underlying portrayal catalogue
    /// XML, when populated. Used to surface mismatches between the dataset's
    /// declared <see cref="Spec"/> edition and the catalogue version actually
    /// resolved for it.
    /// </summary>
    public CatalogueRef? CatalogueRef => _provider.Catalogue.CatalogueRef;

    /// <summary>Gets the currently active color palette.</summary>
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <summary>Switches the active color palette to the given type.</summary>
    public void SwitchPalette(PaletteType type)
    {
        EnsurePalettesLoaded();

        if (_cache.Palettes.TryGetValue(type, out var palette))
        {
            Diagnostics.PortrayalCacheMetrics.RecordHit(Spec.Name, Diagnostics.PortrayalAssetKinds.Palette);
            ActivePalette = palette;
        }
    }

    /// <summary>Gets the controller for viewing group visibility.</summary>
    public ViewingGroupController ViewingGroups { get; } = new();

    /// <summary>Tracks the active S-100 Part 9 §11.7 display mode.</summary>
    public DisplayModeController DisplayModes { get; } = new();

    /// <summary>Controls which S-100 Part 9 §11.6 display planes are visible.</summary>
    public DisplayPlaneController DisplayPlanes { get; } = new();

    // ── Palettes ───────────────────────────────────────────────────────

    private void EnsurePalettesLoaded()
    {
        if (_cache.PalettesLoaded)
        {
            if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayCached))
            {
                ActivePalette = dayCached;
            }
            return;
        }
        _cache.PalettesLoaded = true;
        LoadPalettes(((PortrayalAssetCache)_cache).PalettesDictionary);

        if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayPalette))
        {
            ActivePalette = dayPalette;
        }
    }

    /// <summary>
    /// Loads colour palettes from the catalogue's colour profiles.
    /// Override to change the palette loading strategy (e.g. skip the
    /// multi-palette-in-one-file fallback for specs that don't use it).
    /// </summary>
    protected virtual void LoadPalettes(Dictionary<PaletteType, ColorPalette> palettes)
    {
        foreach (var item in _provider.Catalogue.ColorProfiles)
        {
            var paletteName = item.Description.Name;
            if (string.IsNullOrEmpty(paletteName))
            {
                paletteName = Path.GetFileNameWithoutExtension(item.FileName);
            }

            var paletteType = paletteName switch
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
                    var palette = ColorProfileReader.Read(stream, paletteName);
                    palettes[paletteType.Value] = palette;
                }
                catch (Exception)
                {
                    // Skip gracefully.
                }
            }
            else
            {
                // Try loading each palette from the same file
                foreach (var (type, name) in new[] { (PaletteType.Day, "Day"), (PaletteType.Dusk, "Dusk"), (PaletteType.Night, "Night") })
                {
                    if (palettes.ContainsKey(type)) continue;
                    try
                    {
                        using var stream = _provider.FetchAssetAsync(item, "ColorProfiles").GetAwaiter().GetResult();
                        var palette = ColorProfileReader.Read(stream, name);
                        if (palette.Colors.Count > 0)
                        {
                            palettes[type] = palette;
                        }
                    }
                    catch (Exception)
                    {
                        // Skip gracefully.
                    }
                }
            }
        }
    }

    // ── Rules ──────────────────────────────────────────────────────────

    /// <summary>Gets the ordered list of portrayal rules.</summary>
    public IReadOnlyList<PortrayalRule> Rules
    {
        get
        {
            if (_rules is not null) return _rules;
            _rules = BuildRules();
            return _rules;
        }
    }

    /// <summary>
    /// Builds the list of portrayal rules from the catalogue's rule files.
    /// Override to change rule selection (e.g. restrict to a single named rule).
    /// </summary>
    protected virtual IReadOnlyList<PortrayalRule> BuildRules()
    {
        var rules = new List<PortrayalRule>();

        foreach (var ruleFile in _provider.Catalogue.RuleFiles)
        {
            if (!ruleFile.RuleType.Equals("TopLevelTemplate", StringComparison.OrdinalIgnoreCase))
                continue;

            rules.Add(new PortrayalRule
            {
                Name = ruleFile.Id,
                Type = PortrayalRuleType.Xslt,
                ExecutionOrder = 0,
                AppliesTo = [],
                AlwaysApply = true,
            });
        }

        return rules;
    }

    // ── XSLT ───────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the compiled XSLT transform for the given rule name,
    /// loading and caching it on first access. Override to intercept
    /// specific rules (e.g. inject an adapter transform).
    /// </summary>
    public virtual XslCompiledTransform GetCompiledRule(string ruleName)
    {
        if (_cache.CompiledXslt.TryGetValue(ruleName, out var cached))
        {
            Diagnostics.PortrayalCacheMetrics.RecordHit(Spec.Name, Diagnostics.PortrayalAssetKinds.Xslt);
            return cached;
        }

        Diagnostics.PortrayalCacheMetrics.RecordMiss(Spec.Name, Diagnostics.PortrayalAssetKinds.Xslt);
        var ruleFile = _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Rule '{ruleName}' not found in the portrayal catalogue.");

        var transform = LoadXsltRule(ruleFile);
        _cache.CompiledXslt[ruleName] = transform;
        return transform;
    }

    /// <summary>
    /// Caches a compiled transform under the given rule name. Useful for
    /// subclasses that load adapter rules from embedded resources.
    /// </summary>
    protected void CacheCompiledRule(string ruleName, XslCompiledTransform transform)
    {
        _cache.CompiledXslt[ruleName] = transform;
    }

    /// <summary>Loads and compiles an XSLT rule file with telemetry.</summary>
    protected XslCompiledTransform LoadXsltRule(RuleFile ruleFile)
    {
        using var activity = Diagnostics.Telemetry.ActivitySource.StartActivity("s100.xslt.compile");
        activity?.SetTag(TelemetryTags.XsltRule, ruleFile.Id);
        var start = Stopwatch.GetTimestamp();

        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();

        var resolver = CreateXmlResolver();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
        };

        using var reader = XmlReader.Create(stream, settings, ruleFile.FileName);

        var transform = new XslCompiledTransform();
        transform.Load(reader, XsltSettings.TrustedXslt, resolver);

        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        activity?.SetTag("s100.xslt.compile.duration_ms", elapsedMs);

        return transform;
    }

    /// <summary>
    /// Creates an <see cref="XmlResolver"/> used to resolve
    /// <c>xsl:include</c>/<c>xsl:import</c> URIs during XSLT compilation.
    /// </summary>
    /// <remarks>
    /// The default implementation resolves filenames against the catalogue's
    /// registered rule files. Override to add fallback resolution strategies
    /// (e.g. <see cref="PortrayalCatalogueProvider.FetchRuleAsync"/> for
    /// specs whose sub-templates are not registered as rule files).
    /// </remarks>
    protected virtual XmlResolver CreateXmlResolver()
    {
        return new AssetSourceXmlResolver(_provider);
    }

    // ── Lua ────────────────────────────────────────────────

    /// <summary>
    /// GML portrayal catalogues use XSLT only. Always throws
    /// <see cref="KeyNotFoundException"/>.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Always.</exception>
    public Script GetLuaScript(string scriptName)
    {
        throw new KeyNotFoundException(
            $"Lua script '{scriptName}' not found in the portrayal catalogue.");
    }

    // ── Symbols ────────────────────────────────────────────────────────

    /// <summary>Returns the SVG symbol with the given name, loading and caching it on first access.</summary>
    public SvgSymbol GetSymbol(string symbolName)
    {
        if (_cache.Symbols.TryGetValue(symbolName, out var cached))
        {
            Diagnostics.PortrayalCacheMetrics.RecordHit(Spec.Name, Diagnostics.PortrayalAssetKinds.Svg);
            return cached;
        }

        Diagnostics.PortrayalCacheMetrics.RecordMiss(Spec.Name, Diagnostics.PortrayalAssetKinds.Svg);
        var catalogItem = _provider.Catalogue.Symbols
            .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Symbol '{symbolName}' not found in the portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "Symbols").GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        var svgContent = reader.ReadToEnd();

        var symbol = new SvgSymbol
        {
            Name = symbolName,
            SvgContent = svgContent,
        };

        _cache.Symbols[symbolName] = symbol;
        return symbol;
    }

    // ── Line styles ────────────────────────────────────────────────────

    /// <summary>Returns the line style with the given name, loading and caching it on first access.</summary>
    public LineStyle GetLineStyle(string name)
    {
        if (_cache.LineStyles.TryGetValue(name, out var cached))
        {
            Diagnostics.PortrayalCacheMetrics.RecordHit(Spec.Name, Diagnostics.PortrayalAssetKinds.LineStyle);
            return cached;
        }

        Diagnostics.PortrayalCacheMetrics.RecordMiss(Spec.Name, Diagnostics.PortrayalAssetKinds.LineStyle);
        var catalogItem = _provider.Catalogue.LineStyles
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Line style '{name}' not found in the portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "LineStyles").GetAwaiter().GetResult();
        var style = LineStyleReader.Read(stream, name);

        _cache.LineStyles[name] = style;
        return style;
    }

    // ── Area fills ─────────────────────────────────────────────────────

    /// <summary>Returns the area fill with the given name, loading and caching it on first access.</summary>
    public AreaFill GetAreaFill(string name)
    {
        if (_cache.AreaFills.TryGetValue(name, out var cached))
        {
            Diagnostics.PortrayalCacheMetrics.RecordHit(Spec.Name, Diagnostics.PortrayalAssetKinds.AreaFill);
            return cached;
        }

        Diagnostics.PortrayalCacheMetrics.RecordMiss(Spec.Name, Diagnostics.PortrayalAssetKinds.AreaFill);
        var catalogItem = _provider.Catalogue.AreaFills
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Area fill '{name}' not found in the portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "AreaFills").GetAwaiter().GetResult();
        var fill = AreaFillReader.Read(stream, name);

        _cache.AreaFills[name] = fill;
        return fill;
    }

    // ── XML Resolver ──────────────────────────────────────────────────

    /// <summary>
    /// Default <see cref="XmlResolver"/> that resolves <c>xsl:include</c>/
    /// <c>xsl:import</c> URIs against the catalogue's registered rule files.
    /// </summary>
    protected class AssetSourceXmlResolver : XmlResolver
    {
        private readonly PortrayalCatalogueProvider _provider;

        /// <summary>Creates a resolver backed by the given provider.</summary>
        public AssetSourceXmlResolver(PortrayalCatalogueProvider provider)
        {
            _provider = provider;
        }

        /// <summary>Gets the underlying provider (for subclass use).</summary>
        protected PortrayalCatalogueProvider Provider => _provider;

        /// <inheritdoc/>
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var fileName = Path.GetFileName(absoluteUri.LocalPath);

            var ruleFile = _provider.Catalogue.RuleFiles
                .FirstOrDefault(r => r.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (ruleFile is not null)
            {
                return _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
            }

            return ResolveUnregistered(absoluteUri, fileName);
        }

        /// <summary>
        /// Called when the requested file is not a registered rule file.
        /// Returns <see langword="null"/> by default. Override to provide
        /// fallback resolution (e.g. via <see cref="PortrayalCatalogueProvider.FetchRuleAsync"/>).
        /// </summary>
        protected virtual object? ResolveUnregistered(Uri absoluteUri, string fileName)
        {
            return null;
        }
    }

    /// <summary>
    /// Resolver that falls back to <see cref="PortrayalCatalogueProvider.FetchRuleAsync"/>
    /// for unregistered sub-templates. Used by specs (S-125, S-421) whose
    /// upstream PCs reference sub-templates not listed as rule file entries.
    /// </summary>
    protected sealed class FetchRuleFallbackXmlResolver : AssetSourceXmlResolver
    {
        /// <summary>Creates a resolver with fetch-rule fallback.</summary>
        public FetchRuleFallbackXmlResolver(PortrayalCatalogueProvider provider)
            : base(provider)
        {
        }

        /// <inheritdoc/>
        protected override object? ResolveUnregistered(Uri absoluteUri, string fileName)
        {
            try
            {
                return Provider.FetchRuleAsync(fileName).GetAwaiter().GetResult();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }
}
