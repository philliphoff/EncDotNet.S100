using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;

namespace EncDotNet.S100.Datasets.S101;

/// <summary>
/// S-101 portrayal catalogue implementing <see cref="IVectorPortrayalCatalogue"/>.
/// Loads and compiles XSLT rules, caches Lua scripts, and resolves symbols,
/// line styles, and area fills from a <see cref="PortrayalCatalogueProvider"/>.
/// </summary>
public sealed class S101PortrayalCatalogue : IVectorPortrayalCatalogue
{
    private readonly PortrayalCatalogueProvider _provider;
    private readonly ILuaEngine? _luaEngine;

    private IReadOnlyList<PortrayalRule>? _rules;
    private readonly Dictionary<string, XslCompiledTransform> _compiledXslt = new();
    private readonly Dictionary<string, Script> _luaScripts = new();
    private readonly Dictionary<string, SvgSymbol> _symbols = new();
    private readonly Dictionary<string, LineStyle> _lineStyles = new();
    private readonly Dictionary<string, AreaFill> _areaFills = new();

    public S101PortrayalCatalogue(PortrayalCatalogueProvider provider, ILuaEngine? luaEngine = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _luaEngine = luaEngine;
    }

    public string ProductSpec => "S-101";
    public string Edition => _provider.Catalogue.Version;
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    public void SwitchPalette(PaletteType type)
    {
        ActivePalette = ColorPalette.FromType(type);
    }

    public ViewingGroupController ViewingGroups { get; } = new();

    // ── Rules ──────────────────────────────────────────────────────────

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
            var ruleType = ruleFile.RuleType.Equals("Lua", StringComparison.OrdinalIgnoreCase)
                ? PortrayalRuleType.Lua
                : PortrayalRuleType.Xslt;

            // Map the rule description name to feature type codes
            // Convention: rule filename prefix corresponds to the feature type
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
        // S-101 PC convention: rule files are named after their target feature type
        // e.g. "DepthContour.xsl" → ["DepthContour"], "LIGHTS05.lua" → ["LIGHTS"]
        var name = Path.GetFileNameWithoutExtension(ruleFile.FileName);

        // Strip trailing numeric suffixes (e.g. "LIGHTS05" → "LIGHTS")
        int i = name.Length - 1;
        while (i >= 0 && char.IsDigit(name[i])) i--;
        var featureType = name[..(i + 1)];

        return string.IsNullOrEmpty(featureType) ? [] : [featureType];
    }

    // ── XSLT ───────────────────────────────────────────────────────────

    public XslCompiledTransform GetCompiledRule(string ruleName)
    {
        if (_compiledXslt.TryGetValue(ruleName, out var cached)) return cached;

        var ruleFile = FindRuleFile(ruleName);
        var transform = LoadXsltRule(ruleFile);
        _compiledXslt[ruleName] = transform;
        return transform;
    }

    private XslCompiledTransform LoadXsltRule(RuleFile ruleFile)
    {
        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
        using var reader = XmlReader.Create(stream);

        var transform = new XslCompiledTransform();
        transform.Load(reader);
        return transform;
    }

    // ── Lua ────────────────────────────────────────────────────────────

    public Script GetLuaScript(string scriptName)
    {
        if (_luaScripts.TryGetValue(scriptName, out var cached)) return cached;

        var ruleFile = FindRuleFile(scriptName);
        var script = LoadLuaScript(ruleFile);
        _luaScripts[scriptName] = script;
        return script;
    }

    private Script LoadLuaScript(RuleFile ruleFile)
    {
        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();

        return new Script
        {
            Name = ruleFile.Id,
            Source = source,
        };
    }

    // ── Symbols ────────────────────────────────────────────────────────

    public SvgSymbol GetSymbol(string symbolName)
    {
        if (_symbols.TryGetValue(symbolName, out var cached)) return cached;

        var symbol = LoadSymbol(symbolName);
        _symbols[symbolName] = symbol;
        return symbol;
    }

    private SvgSymbol LoadSymbol(string symbolName)
    {
        var catalogItem = _provider.Catalogue.Symbols
            .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase));

        if (catalogItem is null)
        {
            // Return a placeholder for missing symbols
            return new SvgSymbol
            {
                Name = symbolName,
                SvgContent = $"<svg id=\"{symbolName}\" />",
            };
        }

        using var stream = _provider.FetchAssetAsync(catalogItem, "Symbols").GetAwaiter().GetResult();
        using var reader = new StreamReader(stream);
        var svgContent = reader.ReadToEnd();

        return new SvgSymbol
        {
            Name = symbolName,
            SvgContent = svgContent,
        };
    }

    // ── Line styles ────────────────────────────────────────────────────

    public LineStyle GetLineStyle(string name)
    {
        if (_lineStyles.TryGetValue(name, out var cached)) return cached;

        var style = LoadLineStyle(name);
        _lineStyles[name] = style;
        return style;
    }

    private LineStyle LoadLineStyle(string name)
    {
        // Line styles are typically XML-defined in the PC.
        // For now, return a default style; real implementation parses the XML.
        return new LineStyle
        {
            Name = name,
            Width = 1.0f,
            Color = "#000000",
        };
    }

    // ── Area fills ─────────────────────────────────────────────────────

    public AreaFill GetAreaFill(string name)
    {
        if (_areaFills.TryGetValue(name, out var cached)) return cached;

        var fill = LoadAreaFill(name);
        _areaFills[name] = fill;
        return fill;
    }

    private AreaFill LoadAreaFill(string name)
    {
        // Area fills are typically XML-defined in the PC.
        // For now, return a default fill; real implementation parses the XML.
        return new AreaFill
        {
            Name = name,
            Color = "#C8C8C8",
        };
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private RuleFile FindRuleFile(string ruleName)
    {
        return _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Rule '{ruleName}' not found in the portrayal catalogue.");
    }
}
