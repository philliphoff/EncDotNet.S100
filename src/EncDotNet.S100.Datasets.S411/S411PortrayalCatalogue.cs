using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// S-411 portrayal catalogue implementing <see cref="IVectorPortrayalCatalogue"/>.
/// Loads XSLT rules, colour palette, symbols, line styles, and area fills
/// from a <see cref="PortrayalCatalogueProvider"/>.
/// </summary>
/// <remarks>
/// S-411 uses purely XSLT-based portrayal rules (no Lua). The catalogue ships
/// several top-level templates — one master <c>mainRule</c> (<c>main.xsl</c>)
/// plus per-ice-class entry points (<c>seaice_class_1A.xsl</c>, etc.). To avoid
/// rendering the same feature with multiple class profiles simultaneously, only
/// the <c>mainRule</c> top-level template is exposed as a rule by default; the
/// class-specific rules can still be resolved by name via
/// <see cref="GetCompiledRule"/> if a host wants to switch profiles.
/// </remarks>
public sealed class S411PortrayalCatalogue : IVectorPortrayalCatalogue
{
    private const string DefaultTopLevelRuleId = "mainRule";

    private readonly PortrayalCatalogueProvider _provider;

    private IReadOnlyList<PortrayalRule>? _rules;
    private readonly Dictionary<string, XslCompiledTransform> _compiledXslt = new();
    private readonly Dictionary<string, SvgSymbol> _symbols = new();
    private readonly Dictionary<string, LineStyle> _lineStyles = new();
    private readonly Dictionary<string, AreaFill> _areaFills = new();
    private readonly Dictionary<PaletteType, ColorPalette> _palettes = new();
    private bool _palettesLoaded;

    /// <summary>
    /// Initializes a new <see cref="S411PortrayalCatalogue"/> backed by the given provider.
    /// </summary>
    public S411PortrayalCatalogue(PortrayalCatalogueProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <summary>Gets the S-100 product specification identifier for this catalogue.</summary>
    public string ProductSpec => "S-411";

    /// <summary>Gets the edition of the portrayal catalogue.</summary>
    public string Edition => _provider.Catalogue.Version;

    /// <summary>Gets the currently active color palette.</summary>
    public ColorPalette ActivePalette { get; private set; } = ColorPalette.Default;

    /// <summary>Switches the active color palette to the given type.</summary>
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

    // ── Palettes ───────────────────────────────────────────────────────

    private void EnsurePalettesLoaded()
    {
        if (_palettesLoaded) return;
        _palettesLoaded = true;

        // The S-411 1.2.1 PC ships no day/dusk/night colour profiles; fall
        // back to the default palette so symbol token references still resolve.
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

            if (paletteType is null) continue;

            try
            {
                using var stream = _provider.FetchAssetAsync(item, "ColorProfiles").GetAwaiter().GetResult();
                var palette = ColorProfileReader.Read(stream, paletteName);
                _palettes[paletteType.Value] = palette;
            }
            catch (Exception)
            {
                // Skip gracefully — palette assets are optional in S-411.
            }
        }

        if (_palettes.TryGetValue(PaletteType.Day, out var dayPalette))
        {
            ActivePalette = dayPalette;
        }
    }

    // ── Rules ──────────────────────────────────────────────────────────

    /// <summary>Gets the ordered list of portrayal rules used by the S-411 portrayal pipeline.</summary>
    /// <remarks>
    /// Only the master <c>mainRule</c> top-level template is included; the
    /// per-class top-level templates are still loadable on demand via
    /// <see cref="GetCompiledRule"/>.
    /// </remarks>
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

        var mainRule = _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(DefaultTopLevelRuleId, StringComparison.OrdinalIgnoreCase)
                                 && r.RuleType.Equals("TopLevelTemplate", StringComparison.OrdinalIgnoreCase));

        if (mainRule is not null)
        {
            rules.Add(new PortrayalRule
            {
                Name = mainRule.Id,
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

        // The official S-411 1.2.1 catalogue's mainRule emits a display-list
        // dialect incompatible with this codebase's Part9DisplayListReader.
        // Substitute the library's bundled adapter, which produces the
        // expected display-list shape and handles both real-world S-411 GML
        // shapes (JCOMM ice: namespace and IHO 1.2.1 sample bare-Dataset).
        if (ruleName.Equals(DefaultTopLevelRuleId, StringComparison.OrdinalIgnoreCase))
        {
            var adapter = LoadAdapterRule();
            _compiledXslt[ruleName] = adapter;
            return adapter;
        }

        var ruleFile = _provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(ruleName, StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Rule '{ruleName}' not found in the portrayal catalogue.");

        var transform = LoadXsltRule(ruleFile);
        _compiledXslt[ruleName] = transform;
        return transform;
    }

    private static XslCompiledTransform LoadAdapterRule()
    {
        var asm = typeof(S411PortrayalCatalogue).Assembly;
        const string resourceName = "EncDotNet.S100.Datasets.S411.Adapter.main.xsl";
        using var stream = asm.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{resourceName}' not found in {asm.GetName().Name}. " +
                "Verify Adapter/main.xsl is registered as an EmbeddedResource in the .csproj.");

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
        using var reader = XmlReader.Create(stream, settings);

        var transform = new XslCompiledTransform();
        transform.Load(reader);
        return transform;
    }

    private XslCompiledTransform LoadXsltRule(RuleFile ruleFile)
    {
        using var stream = _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();

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

    /// <summary>
    /// The S-411 portrayal catalogue ships only XSLT rules, so this method
    /// always throws <see cref="KeyNotFoundException"/>.
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
    /// <remarks>
    /// First tries to match the requested filename against the catalogue's
    /// registered <c>RuleFiles</c>. If no match is found, falls back to a
    /// bare-filename lookup against the <c>Rules/</c> subdirectory of the
    /// asset source — this lets shared sub-templates that the upstream IHO
    /// portrayal catalogue does not register as <c>&lt;ruleFile&gt;</c>
    /// entries (such as <c>pointSimpleSymbolTemplate.xsl</c>) resolve the
    /// same way they would on a filesystem-relative <c>xsl:import</c>.
    /// </remarks>
    private sealed class AssetSourceXmlResolver : XmlResolver
    {
        private readonly PortrayalCatalogueProvider _provider;

        public AssetSourceXmlResolver(PortrayalCatalogueProvider provider)
        {
            _provider = provider;
        }

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var fileName = Path.GetFileName(absoluteUri.LocalPath);

            var ruleFile = _provider.Catalogue.RuleFiles
                .FirstOrDefault(r => r.FileName.Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (ruleFile is not null)
            {
                return _provider.FetchAssetAsync(ruleFile).GetAwaiter().GetResult();
            }

            try
            {
                return _provider.FetchRuleAsync(fileName).GetAwaiter().GetResult();
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }
}
