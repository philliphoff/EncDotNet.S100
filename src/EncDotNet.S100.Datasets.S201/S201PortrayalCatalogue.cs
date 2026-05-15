using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S201;

/// <summary>
/// S-201 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/>.
/// </summary>
/// <remarks>
/// The S-201 Edition 2.0.0 Portrayal Catalogue (Annex D) ships a single
/// top-level template <c>main_PaperChart.xsl</c> declared with
/// <c>ruleType="TopLevelTemplate"</c>; <see cref="GmlPortrayalCatalogueBase.BuildRules"/>
/// auto-discovers it. Sub-templates (per-feature rules under
/// <c>Rules/</c>) are loaded on demand via
/// <see cref="GmlPortrayalCatalogueBase.FetchRuleFallbackXmlResolver"/>.
/// </remarks>
public sealed class S201PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    /// <summary>Initializes a new <see cref="S201PortrayalCatalogue"/>.</summary>
    public S201PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }

    /// <inheritdoc/>
    public override SpecRef Spec => new("S-201", default);

    /// <inheritdoc/>
    protected override System.Xml.XmlResolver CreateXmlResolver() =>
        new FetchRuleFallbackXmlResolver(Provider);
}
