using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// S-411 portrayal catalogue. Extends <see cref="GmlPortrayalCatalogueBase"/>
/// with S-411-specific behaviour: an adapter XSLT for the main rule,
/// restricted rule selection, and simplified palette loading.
/// </summary>
/// <remarks>
/// S-411 uses purely XSLT-based portrayal rules (no Lua). The catalogue ships
/// several top-level templates — one master <c>mainRule</c> (<c>main.xsl</c>)
/// plus per-ice-class entry points. Only the <c>mainRule</c> is exposed as a
/// pipeline rule by default; others are still loadable via
/// <see cref="GetCompiledRule"/>.
/// </remarks>
public sealed class S411PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    private const string DefaultTopLevelRuleId = "mainRule";
    private bool _adapterLoaded;

    /// <summary>Initializes a new S-411 portrayal catalogue.</summary>
    public S411PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }

    /// <inheritdoc/>
    public override string ProductSpec => "S-411";

    /// <inheritdoc/>
    protected override XmlResolver CreateXmlResolver() =>
        new FetchRuleFallbackXmlResolver(Provider);

    /// <inheritdoc/>
    protected override void LoadPalettes(Dictionary<PaletteType, ColorPalette> palettes)
    {
        // S-411 1.2.1 PC ships no multi-palette colour profiles; only load
        // explicitly named Day/Dusk/Night palettes.
        foreach (var item in Provider.Catalogue.ColorProfiles)
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

            if (paletteType is null) continue;

            try
            {
                using var stream = Provider.FetchAssetAsync(item, "ColorProfiles").GetAwaiter().GetResult();
                var palette = ColorProfileReader.Read(stream, paletteName);
                palettes[paletteType.Value] = palette;
            }
            catch (Exception)
            {
                // Skip gracefully — palette assets are optional in S-411.
            }
        }
    }

    /// <inheritdoc/>
    protected override IReadOnlyList<PortrayalRule> BuildRules()
    {
        var mainRule = Provider.Catalogue.RuleFiles
            .FirstOrDefault(r => r.Id.Equals(DefaultTopLevelRuleId, StringComparison.OrdinalIgnoreCase)
                                 && r.RuleType.Equals("TopLevelTemplate", StringComparison.OrdinalIgnoreCase));

        if (mainRule is null) return [];

        return [new PortrayalRule
        {
            Name = mainRule.Id,
            Type = PortrayalRuleType.Xslt,
            ExecutionOrder = 0,
            AppliesTo = [],
            AlwaysApply = true,
        }];
    }

    /// <inheritdoc/>
    public override XslCompiledTransform GetCompiledRule(string ruleName)
    {
        // The official S-411 1.2.1 catalogue's mainRule emits a display-list
        // dialect incompatible with this codebase's Part9DisplayListReader.
        // Substitute the library's bundled adapter on first access.
        if (!_adapterLoaded && ruleName.Equals(DefaultTopLevelRuleId, StringComparison.OrdinalIgnoreCase))
        {
            CacheCompiledRule(ruleName, LoadAdapterRule());
            _adapterLoaded = true;
        }

        return base.GetCompiledRule(ruleName);
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
}
