using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S122;

/// <summary>
/// S-122 portrayal catalogue implementing <see cref="IVectorPortrayalCatalogue"/>.
/// Loads XSLT rules, colour palette, symbols, line styles, and area fills
/// from a <see cref="PortrayalCatalogueProvider"/>.
/// </summary>
/// <remarks>
/// S-122 uses purely XSLT-based portrayal rules (no Lua).
/// The main.xsl rule includes all sub-templates via xsl:include.
/// </remarks>
public sealed class S122PortrayalCatalogue : IVectorPortrayalCatalogue
{
    private readonly PortrayalCatalogueProvider _provider;

    private IReadOnlyList<PortrayalRule>? _rules;
    private readonly Dictionary<string, XslCompiledTransform> _compiledXslt = new();
    private readonly Dictionary<string, SvgSymbol> _symbols = new();
    private readonly Dictionary<string, LineStyle> _lineStyles = new();
    private readonly Dictionary<string, AreaFill> _areaFills = new();
    private readonly Dictionary<PaletteType, ColorPalette> _palettes = new();
    private bool _palettesLoaded;

    /// <summary>
    /// Initializes a new <see cref="S122PortrayalCatalogue"/> backed by the given provider.
    /// </summary>
    public S122PortrayalCatalogue(PortrayalCatalogueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        DisplayModeMembership.Bind(DisplayModes, ViewingGroups, _provider.Catalogue);
    }

    /// <summary>Gets the S-100 product specification identifier for this catalogue.</summary>
    public string ProductSpec => "S-122";

    /// <summary>Gets the edition of the portrayal catalogue.</summary>
    public string Edition => _provider.Catalogue.Version;

    /// <summary>Gets the currently active color palette.</summary>
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <summary>Switches the active color palette to the given type.</summary>
    /// <remarks>
    /// The S-122 v2.0.0 Portrayal Catalogue ships only a <c>Day</c>
    /// <c>&lt;palette&gt;</c> block in <c>colorProfile.xml</c>, so requests
    /// for <see cref="PaletteType.Dusk"/> or <see cref="PaletteType.Night"/>
    /// currently leave <see cref="ActivePalette"/> unchanged (set to Day).
    /// TODO: synthesise Dusk/Night palettes locally until upstream
    /// publishes the missing blocks. See the project README.
    /// </remarks>
    public void SwitchPalette(PaletteType type)
    {
        EnsurePalettesLoaded();

        if (_palettes.TryGetValue(type, out var palette))
        {
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
        if (_palettesLoaded) return;
        _palettesLoaded = true;

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
                    _palettes[paletteType.Value] = palette;
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
                    if (_palettes.ContainsKey(type)) continue;
                    try
                    {
                        using var stream = _provider.FetchAssetAsync(item, "ColorProfiles").GetAwaiter().GetResult();
                        var palette = ColorProfileReader.Read(stream, name);
                        if (palette.Colors.Count > 0)
                        {
                            _palettes[type] = palette;
                        }
                    }
                    catch (Exception)
                    {
                        // Skip gracefully.
                    }
                }
            }
        }

        if (_palettes.TryGetValue(PaletteType.Day, out var dayPalette))
        {
            ActivePalette = dayPalette;
        }
    }

    // ── Rules ──────────────────────────────────────────────────────────

    /// <summary>Gets the ordered list of portrayal rules defined by the S-122 Portrayal Catalogue.</summary>
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
        // S-122 uses a single top-level XSLT rule (main.xsl) that includes all sub-templates.
        // Only the TopLevelTemplate rule should be executed; sub-templates are resolved via xsl:include.
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

    /// <summary>Returns the compiled XSLT transform for the given rule name, loading and caching it on first access.</summary>
    public XslCompiledTransform GetCompiledRule(string ruleName)
    {
        if (_compiledXslt.TryGetValue(ruleName, out var cached)) return cached;

        var ruleFile = _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Rule '{ruleName}' not found in the portrayal catalogue.");

        var transform = LoadXsltRule(ruleFile);
        _compiledXslt[ruleName] = transform;
        return transform;
    }

    private XslCompiledTransform LoadXsltRule(RuleFile ruleFile)
    {
        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();

        // Use a custom XmlResolver so that xsl:include directives resolve
        // against the embedded portrayal catalogue assets.
        var resolver = new AssetSourceXmlResolver(_provider);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
        };

        using var reader = XmlReader.Create(stream, settings, ruleFile.FileName);

        var transform = new XslCompiledTransform();
        transform.Load(reader, XsltSettings.TrustedXslt, resolver);
        return transform;
    }

    // ── Lua ────────────────────────────────────────────────

    /// <summary>
    /// The current S-122 portrayal catalogue ships only XSLT rules, so this
    /// method always throws <see cref="KeyNotFoundException"/>. Future editions
    /// or vendor catalogues may include Lua rules; in that case this method
    /// will resolve them via the loaded rule files.
    /// </summary>
    /// <exception cref="KeyNotFoundException">No Lua script with the given name is loaded.</exception>
    public Script GetLuaScript(string scriptName)
    {
        throw new KeyNotFoundException(
            $"Lua script '{scriptName}' not found in the portrayal catalogue.");
    }

    // ── Symbols ────────────────────────────────────────────────────────

    /// <summary>Returns the SVG symbol with the given name, loading and caching it on first access.</summary>
    public SvgSymbol GetSymbol(string symbolName)
    {
        if (_symbols.TryGetValue(symbolName, out var cached)) return cached;

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

        _symbols[symbolName] = symbol;
        return symbol;
    }

    // ── Line styles ────────────────────────────────────────────────────

    /// <summary>Returns the line style with the given name, loading and caching it on first access.</summary>
    public LineStyle GetLineStyle(string name)
    {
        if (_lineStyles.TryGetValue(name, out var cached)) return cached;

        var catalogItem = _provider.Catalogue.LineStyles
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Line style '{name}' not found in the portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "LineStyles").GetAwaiter().GetResult();
        var style = LineStyleReader.Read(stream, name);

        _lineStyles[name] = style;
        return style;
    }

    // ── Area fills ─────────────────────────────────────────────────────

    /// <summary>Returns the area fill with the given name, loading and caching it on first access.</summary>
    public AreaFill GetAreaFill(string name)
    {
        if (_areaFills.TryGetValue(name, out var cached)) return cached;

        var catalogItem = _provider.Catalogue.AreaFills
            .FirstOrDefault(s => s.Id.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Area fill '{name}' not found in the portrayal catalogue.");

        using var stream = _provider.FetchAssetAsync(catalogItem, "AreaFills").GetAwaiter().GetResult();
        var fill = AreaFillReader.Read(stream, name);

        _areaFills[name] = fill;
        return fill;
    }

    /// <summary>
    /// An <see cref="XmlResolver"/> that resolves xsl:include/import URIs
    /// against the portrayal catalogue's embedded assets.
    /// </summary>
    private sealed class AssetSourceXmlResolver : XmlResolver
    {
        private readonly PortrayalCatalogueProvider _provider;

        public AssetSourceXmlResolver(PortrayalCatalogueProvider provider)
        {
            _provider = provider;
        }

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            // The URI will be something like "file:///NavwarnPart.xsl" or a relative resolved URI.
            // Extract the filename and look it up in the Rules directory.
            var fileName = Path.GetFileName(absoluteUri.LocalPath);

            var ruleFile = _provider.Catalogue.RuleFiles
                .FirstOrDefault(r => r.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (ruleFile is not null)
            {
                return _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
            }

            return null;
        }
    }
}
