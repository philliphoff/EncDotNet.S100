using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
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

    private IReadOnlyList<PortrayalRule>? _rules;
    private readonly Dictionary<string, XslCompiledTransform> _compiledXslt = new();
    private readonly Dictionary<string, Script> _luaScripts = new();
    private readonly Dictionary<string, SvgSymbol> _symbols = new();
    private readonly Dictionary<string, LineStyle> _lineStyles = new();
    private readonly Dictionary<string, AreaFill> _areaFills = new();
    private readonly Dictionary<PaletteType, ColorPalette> _palettes = new();
    private bool _palettesLoaded;

    /// <summary>
    /// Initialises a new <see cref="S131PortrayalCatalogue"/> from the given
    /// portrayal catalogue provider.
    /// </summary>
    public S131PortrayalCatalogue(PortrayalCatalogueProvider provider, ILuaEngine? luaEngine = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _luaEngine = luaEngine;
        DisplayModeMembership.Bind(DisplayModes, ViewingGroups, _provider.Catalogue);
    }

    /// <inheritdoc/>
    public SpecRef Spec => new("S-131", default);

    /// <inheritdoc/>
    public string Edition => _provider.Catalogue.Version;

    /// <summary>The identity of the underlying portrayal catalogue XML, when available.</summary>
    public CatalogueRef? CatalogueRef => _provider.Catalogue.CatalogueRef;

    /// <inheritdoc/>
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <inheritdoc/>
    public void SwitchPalette(PaletteType type)
    {
        EnsurePalettesLoaded();

        if (!_palettes.TryGetValue(type, out var palette))
            throw new KeyNotFoundException($"Color palette '{type}' not found in the S-131 portrayal catalogue.");

        ActivePalette = palette;
    }

    /// <inheritdoc/>
    public ViewingGroupController ViewingGroups { get; } = new();

    /// <inheritdoc/>
    public DisplayModeController DisplayModes { get; } = new();

    /// <summary>Controls which S-100 Part 9 §11.6 display planes are visible.</summary>
    public DisplayPlaneController DisplayPlanes { get; } = new();

    // ── Palettes ───────────────────────────────────────────────────────

    private void EnsurePalettesLoaded()
    {
        if (_palettesLoaded) return;
        _palettesLoaded = true;

        foreach (var item in _provider.Catalogue.ColorProfiles)
        {
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
                    using var stream = _provider.FetchAssetAsync(item, "ColorProfiles").GetAwaiter().GetResult();
                    var palette = ColorProfileReader.Read(stream, paletteName);
                    _palettes[paletteType.Value] = palette;
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
                    if (_palettes.ContainsKey(type)) continue;
                    try
                    {
                        using var stream = _provider.FetchAssetAsync(item, "ColorProfiles").GetAwaiter().GetResult();
                        var palette = ColorProfileReader.Read(stream, name);
                        if (palette.Colors.Count > 0)
                            _palettes[type] = palette;
                    }
                    catch { }
                }
            }
        }

        if (_palettes.TryGetValue(PaletteType.Day, out var dayPalette))
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
    public XslCompiledTransform GetCompiledRule(string ruleName)
    {
        if (_compiledXslt.TryGetValue(ruleName, out var cached)) return cached;

        var ruleFile = FindRuleFile(ruleName);
        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
        using var reader = XmlReader.Create(stream);
        var transform = new XslCompiledTransform();
        transform.Load(reader);
        _compiledXslt[ruleName] = transform;
        return transform;
    }

    // ── Lua ────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public Script GetLuaScript(string scriptName)
    {
        if (_luaScripts.TryGetValue(scriptName, out var cached)) return cached;

        var ruleFile = FindRuleFile(scriptName);
        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();
        var script = new Script { Name = ruleFile.Id, Source = source };
        _luaScripts[scriptName] = script;
        return script;
    }

    // ── Symbols ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public SvgSymbol GetSymbol(string symbolName)
    {
        if (_symbols.TryGetValue(symbolName, out var cached))
            return cached;

        var catalogItem = _provider.Catalogue.Symbols
            .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Symbol '{symbolName}' not found in the S-131 portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "Symbols").GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        var svgContent = reader.ReadToEnd();
        var symbol = new SvgSymbol { Name = symbolName, SvgContent = svgContent };
        _symbols[symbolName] = symbol;
        return symbol;
    }

    /// <inheritdoc/>
    public LineStyle GetLineStyle(string name)
    {
        if (_lineStyles.TryGetValue(name, out var cached))
            return cached;

        var catalogItem = _provider.Catalogue.LineStyles
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Line style '{name}' not found in the S-131 portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "LineStyles").GetAwaiter().GetResult();
        var lineStyle = LineStyleReader.Read(stream, name);
        _lineStyles[name] = lineStyle;
        return lineStyle;
    }

    /// <inheritdoc/>
    public AreaFill GetAreaFill(string name)
    {
        if (_areaFills.TryGetValue(name, out var cached))
            return cached;

        var catalogItem = _provider.Catalogue.AreaFills
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Area fill '{name}' not found in the S-131 portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "AreaFills").GetAwaiter().GetResult();
        var fill = AreaFillReader.Read(stream, name);
        _areaFills[name] = fill;
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
