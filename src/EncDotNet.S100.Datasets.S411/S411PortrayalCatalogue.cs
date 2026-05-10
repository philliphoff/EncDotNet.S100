using System.Xml;
using System.Xml.Xsl;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.S411;

/// <summary>
/// S-411 portrayal catalogue. Extends <see cref="GmlPortrayalCatalogueBase"/>
/// with an adapter XSLT for the <c>mainRule</c>, single-rule-only rule list,
/// no multi-palette fallback, and fetch-rule resolver.
/// </summary>
public sealed class S411PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    private const string DefaultTopLevelRuleId = "mainRule";
    private bool _adapterLoaded;

    public S411PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }

    public override SpecRef Spec => new("S-411", default);

    protected override System.Xml.XmlResolver CreateXmlResolver() =>
        new FetchRuleFallbackXmlResolver(Provider);

    /// <summary>
    /// S-411 1.2.1 PC ships no day/dusk/night colour profiles with
    /// multi-palette fallback; only load explicitly named palettes.
    /// </summary>
    protected override void LoadPalettes(Dictionary<PaletteType, ColorPalette> palettes)
    {
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
            catch (Exception) { }
        }
    }

    /// <summary>
    /// Only the master <c>mainRule</c> top-level template is included; the
    /// per-class top-level templates are still loadable on demand via
    /// <see cref="GetCompiledRule"/>.
    /// </summary>
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

    /// <summary>
    /// For the <c>mainRule</c>, substitutes the library's bundled adapter XSLT
    /// which produces the display-list dialect expected by this codebase's
    /// <c>Part9DisplayListReader</c>. All other rules delegate to base.
    /// </summary>
    public override XslCompiledTransform GetCompiledRule(string ruleName)
    {
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
                $"Embedded resource '{resourceName}' not found in {asm.GetName().Name}.");

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit };
        using var reader = XmlReader.Create(stream, settings);

        var transform = new XslCompiledTransform();
        transform.Load(reader);
        return transform;
    }
}
