using System.Diagnostics;
using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Scripting;
using EncDotNet.S100.Core;

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

    // PR-3 (asset-caching audit §6): decoded-asset storage lives on the
    // provider's IPortrayalAssetCache, so two S101PortrayalCatalogue
    // instances sharing a provider — or two providers sharing a
    // PortrayalCatalogueManager-owned cache for SpecRef("S-101", _) —
    // pay each XSLT compile / SVG read / line style / area fill /
    // palette / Lua source decode at most once.
    //
    // Thread-safety: PortrayalAssetCache uses non-concurrent
    // dictionaries. Today the S-101 dataset processor reads and writes
    // these slots on a single pipeline thread per dataset, so the only
    // race risk is two pipelines running concurrently against
    // S-101 catalogues that share a manager-owned cache. PR-6 of the
    // audit tracks hardening to ConcurrentDictionary.
    private readonly IPortrayalAssetCache _cache;

    private IReadOnlyList<PortrayalRule>? _rules;

    public S101PortrayalCatalogue(PortrayalCatalogueProvider provider, ILuaEngine? luaEngine = null)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _luaEngine = luaEngine;
        _cache = provider.AssetCache;
        DisplayModeMembership.Bind(DisplayModes, ViewingGroups, _provider.Catalogue);
    }

    public SpecRef Spec => new("S-101", default);
    public string Edition => _provider.Catalogue.Version;

    /// <summary>The identity of the underlying portrayal catalogue XML, when available.</summary>
    public CatalogueRef? CatalogueRef => _provider.Catalogue.CatalogueRef;
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    public void SwitchPalette(PaletteType type)
    {
        EnsurePalettesLoaded();

        if (!_cache.Palettes.TryGetValue(type, out var palette))
        {
            throw new KeyNotFoundException($"Color palette '{type}' not found in the portrayal catalogue.");
        }

        ActivePalette = palette;
    }

    public ViewingGroupController ViewingGroups { get; } = new();

    public DisplayModeController DisplayModes { get; } = new();

    /// <summary>Controls which S-100 Part 9 §11.6 display planes are visible.</summary>
    public DisplayPlaneController DisplayPlanes { get; } = new();

    // ── Palettes ───────────────────────────────────────────────────────

    private void EnsurePalettesLoaded()
    {
        if (_cache.PalettesLoaded)
        {
            if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayPalette))
            {
                ActivePalette = dayPalette;
            }
            return;
        }
        _cache.PalettesLoaded = true;

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
                    _cache.Palettes[paletteType.Value] = palette;
                }
                catch (Exception)
                {
                    // If a color profile cannot be loaded, skip it gracefully.
                }
            }
            else
            {
                // The manifest entry name does not indicate a specific palette.
                // The file may contain multiple palettes (Day, Dusk, Night) —
                // try loading each one from the same file.
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

        // Set Day palette as active if available
        if (_cache.Palettes.TryGetValue(PaletteType.Day, out var dayFinal))
        {
            ActivePalette = dayFinal;
        }
    }

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
            // Determine type from file extension first; the catalogue ruleType field
            // (e.g. "TopLevelTemplate", "SubTemplate") describes the rule's role,
            // not its format.
            var ruleType = Path.GetExtension(ruleFile.FileName).Equals(".lua", StringComparison.OrdinalIgnoreCase)
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
        // S-101 PC convention: rule files are named after their target feature type.
        // Use the Description.Name first (more reliable than filename), falling back to filename.
        var name = !string.IsNullOrEmpty(ruleFile.Description.Name)
            ? ruleFile.Description.Name
            : Path.GetFileNameWithoutExtension(ruleFile.FileName);

        // Top-level / utility rules that apply to all features
        if (name.Contains("TopLevel", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("PlainBoundaries", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("TopOfChart", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("main", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("template", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Updates", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        // Strip trailing numeric suffixes (e.g. "LIGHTS05" → "LIGHTS")
        int i = name.Length - 1;
        while (i >= 0 && char.IsDigit(name[i])) i--;
        var featureType = name[..(i + 1)];

        return string.IsNullOrEmpty(featureType) ? [] : [featureType];
    }

    // ── XSLT ───────────────────────────────────────────────────────────

    public XslCompiledTransform GetCompiledRule(string ruleName)
    {
        if (_cache.CompiledXslt.TryGetValue(ruleName, out var cached)) return cached;

        var ruleFile = FindRuleFile(ruleName);
        var transform = LoadXsltRule(ruleFile);
        _cache.CompiledXslt[ruleName] = transform;
        return transform;
    }

    private XslCompiledTransform LoadXsltRule(RuleFile ruleFile)
    {
        using var activity = Diagnostics.Telemetry.ActivitySource.StartActivity("s100.xslt.compile");
        activity?.SetTag(TelemetryTags.XsltRule, ruleFile.Id);
        var start = Stopwatch.GetTimestamp();

        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
        using var reader = XmlReader.Create(stream);

        var transform = new XslCompiledTransform();
        transform.Load(reader);

        var elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        activity?.SetTag("s100.xslt.compile.duration_ms", elapsedMs);

        return transform;
    }

    // ── Lua ────────────────────────────────────────────────────────────

    public Script GetLuaScript(string scriptName)
    {
        if (_cache.LuaScripts.TryGetValue(scriptName, out var cached)) return cached;

        var ruleFile = FindRuleFile(scriptName);
        var script = LoadLuaScript(ruleFile);
        _cache.LuaScripts[scriptName] = script;
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

    /// <summary>
    /// Returns the raw Lua source for the given bare filename inside the
    /// portrayal catalogue's <c>Rules/</c> directory (e.g. <c>"main.lua"</c>,
    /// <c>"S100Scripting.lua"</c>), caching the decoded string so subsequent
    /// reads do not re-open the underlying <see cref="Core.IAssetSource"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Returns <see langword="null"/> (and caches <see langword="null"/>) if
    /// the file cannot be fetched, so the MoonSharp module loader's
    /// "missing module → return null" contract is preserved without retrying
    /// failed lookups on every <c>require()</c> call.
    /// </para>
    /// <para>
    /// This caches only the immutable <see cref="string"/> source. The
    /// compiled Lua <c>Script</c> instance is intentionally constructed
    /// per execution to preserve sandbox isolation (S-100 Part 9A).
    /// </para>
    /// </remarks>
    public string? GetLuaSource(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        if (_cache.LuaSources.TryGetValue(fileName, out var cached))
            return cached;

        string? source;
        try
        {
            using var stream = _provider.FetchRuleAsync(fileName)
                .GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            source = reader.ReadToEnd();
        }
        catch
        {
            source = null;
        }

        _cache.LuaSources[fileName] = source;
        return source;
    }

    /// <summary>The underlying portrayal catalogue provider.</summary>
    internal PortrayalCatalogueProvider Provider => _provider;

    // ── Symbols ────────────────────────────────────────────────────────

    public SvgSymbol GetSymbol(string symbolName)
    {
        if (_cache.Symbols.TryGetValue(symbolName, out var cached)) return cached;

        var symbol = LoadSymbol(symbolName);
        _cache.Symbols[symbolName] = symbol;
        return symbol;
    }

    private SvgSymbol LoadSymbol(string symbolName)
    {
        var catalogItem = _provider.Catalogue.Symbols
            .FirstOrDefault(s => s.Id.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Symbol '{symbolName}' not found in the portrayal catalogue.");

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
        if (_cache.LineStyles.TryGetValue(name, out var cached)) return cached;

        var style = LoadLineStyle(name);
        _cache.LineStyles[name] = style;
        return style;
    }

    private LineStyle LoadLineStyle(string name)
    {
        var catalogItem = _provider.Catalogue.LineStyles
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Line style '{name}' not found in the portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "LineStyles").GetAwaiter().GetResult();
        return LineStyleReader.Read(stream, name);
    }

    // ── Area fills ─────────────────────────────────────────────────────

    public AreaFill GetAreaFill(string name)
    {
        if (_cache.AreaFills.TryGetValue(name, out var cached)) return cached;

        var fill = LoadAreaFill(name);
        _cache.AreaFills[name] = fill;
        return fill;
    }

    private AreaFill LoadAreaFill(string name)
    {
        var catalogItem = _provider.Catalogue.AreaFills
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Area fill '{name}' not found in the portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "AreaFills").GetAwaiter().GetResult();
        return AreaFillReader.Read(stream, name);
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private RuleFile FindRuleFile(string ruleName)
    {
        return _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Rule '{ruleName}' not found in the portrayal catalogue.");
    }
}
