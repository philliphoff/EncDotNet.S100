using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Lua;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S131;

/// <summary>
/// S-131 portrayal catalogue implementing <see cref="IVectorPortrayalCatalogue"/>.
/// Loads Lua scripts, symbols, line styles, and colour palettes from a
/// <see cref="PortrayalCatalogueProvider"/> backed by the bundled
/// <c>EncDotNet.S100.Specifications/content/S131/pc/</c> assets.
/// </summary>
/// <remarks>
/// <para>
/// Modelled after <c>S101PortrayalCatalogue</c> rather than
/// <c>GmlPortrayalCatalogueBase</c> because S-131 uses Lua portrayal
/// (S-100 Part 9A) — the same engine as S-101 — not XSLT.
/// </para>
/// <para>
/// S-131 Edition 2.0.0 Portrayal Catalogue.
/// </para>
/// </remarks>
public sealed class S131PortrayalCatalogue : IVectorPortrayalCatalogue
{
    private readonly PortrayalCatalogueProvider _provider;
    private readonly ILuaEngine? _luaEngine;
    private readonly IPortrayalAssetCache _cache;

    private IReadOnlyList<PortrayalRule>? _rules;

    /// <summary>
    /// Initialises a new <see cref="S131PortrayalCatalogue"/> from the given
    /// portrayal catalogue provider.
    /// </summary>
    public S131PortrayalCatalogue(PortrayalCatalogueProvider provider, ILuaEngine? luaEngine = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _luaEngine = luaEngine;
        _cache = provider.AssetCache;
        DisplayModeMembership.Bind(DisplayModes, ViewingGroups, _provider.Catalogue);
    }

    /// <inheritdoc/>
    public SpecRef Spec => new("S-131", default);

    private const string ProductTag = "S-131";

    /// <inheritdoc/>
    public string Edition => _provider.Catalogue.Version;

    /// <summary>The identity of the underlying portrayal catalogue XML, when available.</summary>
    public CatalogueRef? CatalogueRef => _provider.Catalogue.CatalogueRef;

    /// <inheritdoc/>
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <inheritdoc/>
    public async ValueTask SwitchPaletteAsync(PaletteType type, CancellationToken cancellationToken = default)
    {
        await EnsurePalettesLoadedAsync(cancellationToken).ConfigureAwait(false);

        if (!_cache.Palettes.TryGetValue(type, out var palette))
            throw new KeyNotFoundException($"Color palette '{type}' not found in the S-131 portrayal catalogue.");

        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Palette);
        ActivePalette = palette;
    }

    /// <inheritdoc/>
    public ViewingGroupController ViewingGroups { get; } = new();

    /// <inheritdoc/>
    public DisplayModeController DisplayModes { get; } = new();

    /// <summary>Controls which S-100 Part 9 §11.6 display planes are visible.</summary>
    public DisplayPlaneController DisplayPlanes { get; } = new();

    // ── Palettes ───────────────────────────────────────────────────────

    private async ValueTask EnsurePalettesLoadedAsync(CancellationToken cancellationToken)
    {
        if (_cache.PalettesLoaded)
        {
            if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayCached))
                ActivePalette = dayCached;
            return;
        }
        _cache.PalettesLoaded = true;

        foreach (var item in _provider.Catalogue.ColorProfiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var paletteName = item.Description.Name;
            if (string.IsNullOrEmpty(paletteName))
                paletteName = Path.GetFileNameWithoutExtension(item.FileName);

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
                    _cache.Palettes[paletteType.Value] = palette;
                }
                catch
                {
                    // Skip gracefully if a colour profile cannot be loaded.
                }
            }
            else
            {
                foreach (var (type, name) in new[] { (PaletteType.Day, "Day"), (PaletteType.Dusk, "Dusk"), (PaletteType.Night, "Night") })
                {
                    if (_cache.Palettes.ContainsKey(type)) continue;
                    try
                    {
                        using var stream = await _provider.FetchAssetAsync(item, "ColorProfiles", cancellationToken).ConfigureAwait(false);
                        var palette = ColorProfileReader.Read(stream, name);
                        if (palette.Colors.Count > 0)
                            _cache.Palettes[type] = palette;
                    }
                    catch { }
                }
            }
        }

        if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayPalette))
            ActivePalette = dayPalette;
    }

    // ── Rules ──────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public IReadOnlyList<PortrayalRule> Rules
    {
        get
        {
            if (_rules is not null) return _rules;
            _rules = BuildRules();
            return _rules;
        }
    }

    private IReadOnlyList<PortrayalRule> BuildRules()
    {
        var rules = new List<PortrayalRule>();
        int order = 0;

        foreach (var ruleFile in _provider.Catalogue.RuleFiles)
        {
            var ruleType = Path.GetExtension(ruleFile.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase)
                ? PortrayalRuleType.Lua
                : PortrayalRuleType.Xslt;

            var featureTypes = InferFeatureTypes(ruleFile);

            rules.Add(new PortrayalRule
            {
                Name = ruleFile.Id,
                Type = ruleType,
                ExecutionOrder = order++,
                AppliesTo = featureTypes,
                AlwaysApply = featureTypes.Count == 0,
            });
        }

        return rules;
    }

    private static IReadOnlyList<string> InferFeatureTypes(RuleFile ruleFile)
    {
        var name = !string.IsNullOrEmpty(ruleFile.Description.Name)
            ? ruleFile.Description.Name
            : Path.GetFileNameWithoutExtension(ruleFile.FileName);

        // Framework / utility rules apply to all features
        if (name.Contains("main", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PortrayalAPI", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PortrayalModel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("S100Scripting", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("template", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        return [name];
    }

    // ── XSLT ───────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask<XslCompiledTransform> GetCompiledRuleAsync(string ruleName, CancellationToken cancellationToken = default)
    {
        if (_cache.CompiledXslt.TryGetValue(ruleName, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Xslt);
            return new ValueTask<XslCompiledTransform>(cached);
        }

        return new ValueTask<XslCompiledTransform>(LoadAndCacheXsltAsync(ruleName, cancellationToken));
    }

    private async Task<XslCompiledTransform> LoadAndCacheXsltAsync(string ruleName, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordMiss(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Xslt);
        var ruleFile = FindRuleFile(ruleName);
        using var stream = await _provider.FetchAssetAsync(ruleFile, cancellationToken).ConfigureAwait(false);
        using var buffered = new MemoryStream();
        await stream.CopyToAsync(buffered, cancellationToken).ConfigureAwait(false);
        buffered.Position = 0;
        using var reader = XmlReader.Create(buffered);
        var transform = new XslCompiledTransform();
        transform.Load(reader);
        _cache.CompiledXslt[ruleName] = transform;
        return transform;
    }

    // ── Lua ────────────────────────────────────────────────────────────

    /// <summary>
    /// The context parameters declared by the S-131 portrayal catalogue
    /// (S-100 Part 9A), projected onto the Core
    /// <see cref="LuaContextParameter"/> type for the generic Lua executor.
    /// </summary>
    public IReadOnlyList<LuaContextParameter> ContextParameters =>
        _contextParameters ??= [.. _provider.Catalogue.ContextParameters
            .Select(cp => new LuaContextParameter(cp.Id, cp.Type, cp.Default))];

    private IReadOnlyList<LuaContextParameter>? _contextParameters;

    /// <summary>
    /// Returns every Lua rule file declared in the portrayal catalogue
    /// manifest (filtered to <c>.lua</c> extension), e.g. <c>main.lua</c>,
    /// <c>S100Scripting.lua</c>.
    /// </summary>
    public ValueTask<IReadOnlyList<string>> GetLuaSourceNamesAsync(CancellationToken cancellationToken = default)
    {
        var names = _provider.Catalogue.RuleFiles
            .Where(rf => Path.GetExtension(rf.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase))
            .Select(rf => rf.FileName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new ValueTask<IReadOnlyList<string>>(names);
    }

    /// <summary>
    /// Returns the raw Lua source for the given bare filename inside the
    /// portrayal catalogue's <c>Rules/</c> directory (e.g. <c>"main.lua"</c>,
    /// <c>"S100Scripting.lua"</c>), caching the decoded string so subsequent
    /// reads do not re-open the underlying <see cref="Core.IAssetSource"/>.
    /// </summary>
    public ValueTask<string?> GetLuaSourceAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        if (_cache.LuaSources.TryGetValue(fileName, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordLuaSourceHit(ProductTag);
            return new ValueTask<string?>(cached);
        }

        return new ValueTask<string?>(LoadLuaSourceAsync(fileName, cancellationToken));
    }

    private async Task<string?> LoadLuaSourceAsync(string fileName, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordLuaSourceMiss(ProductTag);
        string? source;
        try
        {
            using var stream = await _provider.FetchRuleAsync(fileName, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            source = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            source = null;
        }

        _cache.LuaSources[fileName] = source;
        return source;
    }

    // ── Symbols ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public ValueTask<SvgSymbol> GetSymbolAsync(string symbolName, CancellationToken cancellationToken = default)
    {
        if (_cache.Symbols.TryGetValue(symbolName, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Svg);
            return new ValueTask<SvgSymbol>(cached);
        }

        return new ValueTask<SvgSymbol>(LoadSymbolAsync(symbolName, cancellationToken));
    }

    private async Task<SvgSymbol> LoadSymbolAsync(string symbolName, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordMiss(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.Svg);
        var catalogItem = _provider.Catalogue.Symbols
            .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Symbol '{symbolName}' not found in the S-131 portrayal catalogue.");

        using var stream = await _provider.FetchAssetAsync(catalogItem, "Symbols", cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var svgContent = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var symbol = new SvgSymbol { Name = symbolName, SvgContent = svgContent };
        _cache.Symbols[symbolName] = symbol;
        return symbol;
    }

    /// <inheritdoc/>
    public ValueTask<LineStyle> GetLineStyleAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_cache.LineStyles.TryGetValue(name, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.LineStyle);
            return new ValueTask<LineStyle>(cached);
        }

        return new ValueTask<LineStyle>(LoadLineStyleAsync(name, cancellationToken));
    }

    private async Task<LineStyle> LoadLineStyleAsync(string name, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordMiss(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.LineStyle);
        var catalogItem = _provider.Catalogue.LineStyles
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Line style '{name}' not found in the S-131 portrayal catalogue.");

        using var stream = await _provider.FetchAssetAsync(catalogItem, "LineStyles", cancellationToken).ConfigureAwait(false);
        var lineStyle = LineStyleReader.Read(stream, name);
        _cache.LineStyles[name] = lineStyle;
        return lineStyle;
    }

    /// <inheritdoc/>
    public ValueTask<AreaFill> GetAreaFillAsync(string name, CancellationToken cancellationToken = default)
    {
        if (_cache.AreaFills.TryGetValue(name, out var cached))
        {
            Portrayals.Diagnostics.PortrayalCacheMetrics.RecordHit(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.AreaFill);
            return new ValueTask<AreaFill>(cached);
        }

        return new ValueTask<AreaFill>(LoadAreaFillAsync(name, cancellationToken));
    }

    private async Task<AreaFill> LoadAreaFillAsync(string name, CancellationToken cancellationToken)
    {
        Portrayals.Diagnostics.PortrayalCacheMetrics.RecordMiss(ProductTag, Portrayals.Diagnostics.PortrayalAssetKinds.AreaFill);
        var catalogItem = _provider.Catalogue.AreaFills
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Area fill '{name}' not found in the S-131 portrayal catalogue.");

        using var stream = await _provider.FetchAssetAsync(catalogItem, "AreaFills", cancellationToken).ConfigureAwait(false);
        var fill = AreaFillReader.Read(stream, name);
        _cache.AreaFills[name] = fill;
        return fill;
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private RuleFile FindRuleFile(string ruleName)
    {
        return _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Rule '{ruleName}' not found in the S-131 portrayal catalogue.");
    }

    /// <summary>The underlying portrayal catalogue provider, for Lua script loading.</summary>
    internal PortrayalCatalogueProvider Provider => _provider;
}
