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
/// <see cref="GetCompiledRuleAsync"/> (for specs that inject an adapter rule).
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

    /// <inheritdoc/>
    public async ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default)
    {
        await EnsurePalettesLoadedAsync(cancellationToken).ConfigureAwait(false);

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

    private async ValueTask EnsurePalettesLoadedAsync(CancellationToken cancellationToken)
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
        await LoadPalettesAsync(((PortrayalAssetCache)_cache).PalettesDictionary, cancellationToken).ConfigureAwait(false);

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
    protected virtual async Task LoadPalettesAsync(Dictionary<PaletteType, ColorPalette> palettes, CancellationToken cancellationToken)
    {
        foreach (var item in _provider.Catalogue.ColorProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
                    using var stream = await _provider.FetchAssetAsync(item, "ColorProfiles", cancellationToken).ConfigureAwait(false);
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
                        using var stream = await _provider.FetchAssetAsync(item, "ColorProfiles", cancellationToken).ConfigureAwait(false);
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
    /// Returns the compiled XSLT transform for the given rule name, loading
    /// and caching it on first access. Override to intercept specific rules
    /// (e.g. inject an adapter transform).
    /// </summary>
    /// <param name="ruleName">The rule identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The compiled XSLT transform.</returns>
    /// <exception cref="KeyNotFoundException">No such rule in the catalogue.</exception>
    public virtual async ValueTask<XslCompiledTransform> GetCompiledRuleAsync(string ruleName, CancellationToken cancellationToken = default)
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

        var transform = await LoadXsltRuleAsync(ruleFile, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Loads and compiles an XSLT rule file with telemetry.
    /// </summary>
    /// <remarks>
    /// Pre-fetches every registered rule file's bytes into a memory dict
    /// before invoking <see cref="XslCompiledTransform.Load(XmlReader, XsltSettings, XmlResolver)"/>,
    /// because <see cref="XmlResolver.GetEntity(Uri, string?, Type?)"/> is
    /// a synchronous .NET API contract during XSLT compile and we cannot
    /// await inside it. The pre-fetch is bounded to the (small) set of
    /// declared rule files; <see cref="FetchRuleFallbackXmlResolver"/>
    /// handles the unregistered-include case via a documented sync bridge.
    /// </remarks>
    protected async Task<XslCompiledTransform> LoadXsltRuleAsync(RuleFile ruleFile, CancellationToken cancellationToken)
    {
        using var activity = Diagnostics.Telemetry.ActivitySource.StartActivity("s100.xslt.compile");
        activity?.SetTag(TelemetryTags.XsltRule, ruleFile.Id);
        var start = Stopwatch.GetTimestamp();

        var primaryBytes = await ReadAllBytesAsync(ruleFile, cancellationToken).ConfigureAwait(false);

        // Pre-fetch every other registered rule file so AssetSourceXmlResolver
        // can serve xsl:include / xsl:import lookups synchronously from memory.
        var registeredBytes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var rf in _provider.Catalogue.RuleFiles)
        {
            if (registeredBytes.ContainsKey(rf.FileName)) continue;
            if (string.Equals(rf.Id, ruleFile.Id, StringComparison.OrdinalIgnoreCase))
            {
                registeredBytes[rf.FileName] = primaryBytes;
                continue;
            }
            try
            {
                registeredBytes[rf.FileName] = await ReadAllBytesAsync(rf, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Best-effort pre-warm: a missing sibling rule file is left
                // for the resolver's unregistered-fallback path.
            }
        }

        var resolver = CreateXmlResolver(registeredBytes);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
        };

        using var primaryStream = new MemoryStream(primaryBytes, writable: false);
        using var reader = XmlReader.Create(primaryStream, settings, ruleFile.FileName);

        var transform = new XslCompiledTransform();
        transform.Load(reader, XsltSettings.TrustedXslt, resolver);

        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        activity?.SetTag("s100.xslt.compile.duration_ms", elapsedMs);

        return transform;
    }

    private async Task<byte[]> ReadAllBytesAsync(RuleFile ruleFile, CancellationToken cancellationToken)
    {
        using var stream = await _provider.FetchAssetAsync(ruleFile, cancellationToken).ConfigureAwait(false);
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>
    /// Creates an <see cref="XmlResolver"/> used to resolve
    /// <c>xsl:include</c>/<c>xsl:import</c> URIs during XSLT compilation.
    /// </summary>
    /// <remarks>
    /// The default implementation resolves filenames against the pre-fetched
    /// rule-file bytes dictionary. Override to add fallback resolution
    /// strategies (e.g. <see cref="PortrayalCatalogueProvider.FetchRuleAsync"/>
    /// for specs whose sub-templates are not registered as rule files).
    /// </remarks>
    protected virtual XmlResolver CreateXmlResolver(IReadOnlyDictionary<string, byte[]> registeredBytes)
    {
        return new AssetSourceXmlResolver(_provider, registeredBytes);
    }

    // ── Lua ────────────────────────────────────────────────

    /// <summary>
    /// GML portrayal catalogues use XSLT only and ship no Lua rules, so this
    /// always returns an empty list.
    /// </summary>
    public ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default)
        => new(Array.Empty<string>());

    /// <summary>
    /// GML portrayal catalogues ship no Lua rules, so this always returns
    /// <see langword="null"/> (honouring the module loader's
    /// "missing module → null" contract).
    /// </summary>
    public ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default)
        => new((string?)null);

    /// <summary>
    /// GML portrayal catalogues declare no Lua context parameters.
    /// </summary>
    public IReadOnlyList<Pipelines.Vector.Lua.LuaContextParameter> ContextParameters => [];

    // ── Symbols ────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the SVG symbol with the given name, loading and caching it
    /// on first access.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such symbol in the catalogue.</exception>
    public ValueTask<SvgSymbol> GetSymbolAsync(string symbolName, CancellationToken cancellationToken = default)
    {
        if (_cache.Symbols.TryGetValue(symbolName, out var cached))
        {
            Diagnostics.PortrayalCacheMetrics.RecordHit(Spec.Name, Diagnostics.PortrayalAssetKinds.Svg);
            return new ValueTask<SvgSymbol>(cached);
        }

        return new ValueTask<SvgSymbol>(LoadSymbolAsync(symbolName, cancellationToken));
    }

    private async Task<SvgSymbol> LoadSymbolAsync(string symbolName, CancellationToken cancellationToken)
    {
        Diagnostics.PortrayalCacheMetrics.RecordMiss(Spec.Name, Diagnostics.PortrayalAssetKinds.Svg);
        var catalogItem = _provider.Catalogue.Symbols
            .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Symbol '{symbolName}' not found in the portrayal catalogue.");

        using var stream = await _provider.FetchAssetAsync(catalogItem, "Symbols", cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var svgContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        var symbol = new SvgSymbol
        {
            Name = symbolName,
            SvgContent = svgContent,
        };

        _cache.Symbols[symbolName] = symbol;
        return symbol;
    }

    // ── Line styles ────────────────────────────────────────────────────

    /// <summary>
    /// Returns the line style with the given name, loading and caching it
    /// on first access.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such line style in the catalogue.</exception>
    public ValueTask<LineStyle> GetLineStyleAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_cache.LineStyles.TryGetValue(name, out var cached))
        {
            Diagnostics.PortrayalCacheMetrics.RecordHit(Spec.Name, Diagnostics.PortrayalAssetKinds.LineStyle);
            return new ValueTask<LineStyle>(cached);
        }

        return new ValueTask<LineStyle>(LoadLineStyleAsync(name, cancellationToken));
    }

    private async Task<LineStyle> LoadLineStyleAsync(string name, CancellationToken cancellationToken)
    {
        Diagnostics.PortrayalCacheMetrics.RecordMiss(Spec.Name, Diagnostics.PortrayalAssetKinds.LineStyle);
        var catalogItem = _provider.Catalogue.LineStyles
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Line style '{name}' not found in the portrayal catalogue.");

        using var stream = await _provider.FetchAssetAsync(catalogItem, "LineStyles", cancellationToken).ConfigureAwait(false);
        var style = LineStyleReader.Read(stream, name);

        _cache.LineStyles[name] = style;
        return style;
    }

    // ── Area fills ─────────────────────────────────────────────────────

    /// <summary>
    /// Returns the area fill with the given name, loading and caching it
    /// on first access.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No such area fill in the catalogue.</exception>
    public ValueTask<AreaFill> GetAreaFillAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_cache.AreaFills.TryGetValue(name, out var cached))
        {
            Diagnostics.PortrayalCacheMetrics.RecordHit(Spec.Name, Diagnostics.PortrayalAssetKinds.AreaFill);
            return new ValueTask<AreaFill>(cached);
        }

        return new ValueTask<AreaFill>(LoadAreaFillAsync(name, cancellationToken));
    }

    private async Task<AreaFill> LoadAreaFillAsync(string name, CancellationToken cancellationToken)
    {
        Diagnostics.PortrayalCacheMetrics.RecordMiss(Spec.Name, Diagnostics.PortrayalAssetKinds.AreaFill);
        var catalogItem = _provider.Catalogue.AreaFills
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Area fill '{name}' not found in the portrayal catalogue.");

        using var stream = await _provider.FetchAssetAsync(catalogItem, "AreaFills", cancellationToken).ConfigureAwait(false);
        var fill = AreaFillReader.Read(stream, name);

        _cache.AreaFills[name] = fill;
        return fill;
    }

    // ── XML Resolver ──────────────────────────────────────────────────

    /// <summary>
    /// Default <see cref="XmlResolver"/> that resolves <c>xsl:include</c>/
    /// <c>xsl:import</c> URIs against the pre-fetched rule-file bytes.
    /// </summary>
    /// <remarks>
    /// <see cref="XmlResolver.GetEntity(Uri, string?, Type?)"/> is invoked
    /// synchronously by <see cref="XslCompiledTransform.Load(XmlReader, XsltSettings, XmlResolver)"/>
    /// — a .NET API contract we don't own. The base resolver therefore
    /// reads from a memory dict that <see cref="LoadXsltRuleAsync"/>
    /// pre-fetched on the async hot path; no sync I/O happens here.
    /// </remarks>
    protected class AssetSourceXmlResolver : XmlResolver
    {
        private readonly PortrayalCatalogueProvider _provider;
        private readonly IReadOnlyDictionary<string, byte[]> _registeredBytes;

        /// <summary>Creates a resolver backed by the given provider and pre-fetched rule bytes.</summary>
        public AssetSourceXmlResolver(PortrayalCatalogueProvider provider, IReadOnlyDictionary<string, byte[]> registeredBytes)
        {
            _provider = provider;
            _registeredBytes = registeredBytes;
        }

        /// <summary>Gets the underlying provider (for subclass use).</summary>
        protected PortrayalCatalogueProvider Provider => _provider;

        /// <inheritdoc/>
        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var fileName = Path.GetFileName(absoluteUri.LocalPath);

            if (_registeredBytes.TryGetValue(fileName, out var bytes))
            {
                return new MemoryStream(bytes, writable: false);
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
        public FetchRuleFallbackXmlResolver(PortrayalCatalogueProvider provider, IReadOnlyDictionary<string, byte[]> registeredBytes)
            : base(provider, registeredBytes)
        {
        }

        /// <inheritdoc/>
        protected override object? ResolveUnregistered(Uri absoluteUri, string fileName)
        {
            try
            {
                // SYNC BRIDGE: XmlResolver.GetEntity is a synchronous .NET
                // API contract invoked during XSLT compile. The unregistered
                // sub-template names aren't known up front (the rule file
                // list doesn't include them), so we cannot pre-warm this
                // path the way LoadXsltRuleAsync pre-warms registered rules.
                // The bridge is bounded to one-time XSLT cold-compile lookups
                // per rule, and there is no async equivalent in the BCL.
                return Provider.FetchRuleAsync(fileName).GetAwaiter().GetResult();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }
}
