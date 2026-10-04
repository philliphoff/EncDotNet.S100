using System.Xml.Xsl;
using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S128;

/// <summary>
/// S-128 portrayal catalogue. Delegates all XSLT-based portrayal
/// infrastructure to <see cref="GmlPortrayalCatalogueBase"/>, and wraps the
/// upstream <c>main</c> rule in an adapter that draws product coverages as
/// outlines only.
/// </summary>
/// <remarks>
/// The bundled catalogue is byte-identical to the IHO upstream, which fills
/// each product coverage 70 % opaque on <c>OVERRADAR</c>. Over an ENC, nested
/// products (a harbour cell inside an approach cell) stack to near-opaque and
/// hide the chart, which S-98 Main §9.2.1 forbids. Upstream issue #51 and the
/// upstream Lua port (PR #56) drop the fills; the adapter
/// (<c>Adapter/outlineOnly.xsl</c>) does the same until the bundled catalogue
/// is refreshed.
/// </remarks>
public sealed class S128PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    private const string MainRuleId = "main";
    private const string AdapterResourceName = "EncDotNet.S100.Datasets.S128.Adapter.outlineOnly.xsl";

    private bool _adapterLoaded;

    /// <summary>
    /// Creates an S-128 portrayal catalogue backed by the given provider.
    /// </summary>
    /// <param name="provider">The portrayal catalogue provider that supplies rule files and assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
    public S128PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }

    /// <inheritdoc/>
    public override SpecRef Spec => new("S-128", default);

    /// <summary>
    /// For the <c>main</c> rule, returns the outline-only adapter compiled
    /// over the upstream rule files. All other rules delegate to base.
    /// </summary>
    public override async ValueTask<XslCompiledTransform> GetCompiledRuleAsync(
        string ruleName,
        CancellationToken cancellationToken = default)
    {
        if (!_adapterLoaded && ruleName.Equals(MainRuleId, StringComparison.OrdinalIgnoreCase))
        {
            var asm = typeof(S128PortrayalCatalogue).Assembly;
            using var adapter = asm.GetManifestResourceStream(AdapterResourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded resource '{AdapterResourceName}' not found in {asm.GetName().Name}.");
            CacheCompiledRule(
                ruleName,
                await LoadAdapterXsltAsync(adapter, "outlineOnly.xsl", cancellationToken).ConfigureAwait(false));
            _adapterLoaded = true;
        }

        return await base.GetCompiledRuleAsync(ruleName, cancellationToken).ConfigureAwait(false);
    }
}
