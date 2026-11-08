using System.Text;
using System.Xml;
using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S412;

/// <summary>
/// S-412 portrayal catalogue. Uses the XSLT infrastructure of
/// <see cref="GmlPortrayalCatalogueBase"/> and corrects one defect in the
/// draft upstream catalogue at load time (see <see cref="WaveHeightRulePatch"/>).
/// </summary>
public sealed class S412PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    /// <summary>
    /// Creates an S-412 portrayal catalogue backed by the given provider.
    /// </summary>
    /// <param name="provider">The portrayal catalogue provider that supplies rule files and assets.</param>
    /// <exception cref="ArgumentNullException"><paramref name="provider"/> is <c>null</c>.</exception>
    public S412PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }

    /// <inheritdoc/>
    public override SpecRef Spec => new("S-412", default);

    /// <inheritdoc/>
    protected override XmlResolver CreateXmlResolver(IReadOnlyDictionary<string, byte[]> registeredBytes) =>
        new PatchingXmlResolver(Provider, registeredBytes);

    /// <summary>
    /// The draft catalogue's significant wave height rule (upstream commit
    /// <c>1e4fe55</c>): <c>main.xsl</c> and the manifest name
    /// <c>SignificantWaveHeightWarning.xsl</c>, but the file is
    /// <c>WaveHeightWarning.xsl</c>, and it matches <c>WaveHeightWarning</c>
    /// features by <c>waveHeightWarningThreshold</c>, names the Feature
    /// Catalogue doesn't use. When the catalogue has no
    /// <c>SignificantWaveHeightWarning.xsl</c>, the resolver serves
    /// <c>WaveHeightWarning.xsl</c> with the Feature Catalogue's names in their
    /// place; once the catalogue ships the file, this does nothing.
    /// </summary>
    internal static class WaveHeightRulePatch
    {
        /// <summary>The rule file <c>main.xsl</c> includes.</summary>
        public const string IncludedFileName = "SignificantWaveHeightWarning.xsl";

        /// <summary>The upstream file that holds the rule.</summary>
        public const string UpstreamFileName = "WaveHeightWarning.xsl";

        /// <summary>
        /// Rewrites the upstream rule's feature and attribute names to the
        /// Feature Catalogue's. Names that are already right are untouched.
        /// </summary>
        /// <param name="upstream">The upstream rule's bytes.</param>
        /// <returns>The corrected rule's bytes.</returns>
        public static byte[] Apply(byte[] upstream)
        {
            // The upstream rules declare encoding="ISO-8859-1" but are ASCII.
            var text = Encoding.Latin1.GetString(upstream);
            var patched = text
                .Replace("match=\"WaveHeightWarning[", "match=\"SignificantWaveHeightWarning[", StringComparison.Ordinal)
                .Replace(" waveHeightWarningThreshold=", " significantWaveHeightWarningThreshold=", StringComparison.Ordinal);
            return Encoding.Latin1.GetBytes(patched);
        }
    }

    private sealed class PatchingXmlResolver : AssetSourceXmlResolver
    {
        public PatchingXmlResolver(PortrayalCatalogueProvider provider, IReadOnlyDictionary<string, byte[]> registeredBytes)
            : base(provider, registeredBytes)
        {
        }

        protected override object? ResolveUnregistered(Uri absoluteUri, string fileName)
        {
            if (!fileName.Equals(WaveHeightRulePatch.IncludedFileName, StringComparison.OrdinalIgnoreCase))
                return null;

            try
            {
                // SYNC BRIDGE: XmlResolver.GetEntity is synchronous and runs
                // during XSLT compile, once per compile of main.xsl; the
                // provider's embedded-resource fetch has no real async work.
                using var stream = Provider.FetchRuleAsync(WaveHeightRulePatch.UpstreamFileName).GetAwaiter().GetResult();
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                return new MemoryStream(WaveHeightRulePatch.Apply(buffer.ToArray()), writable: false);
            }
            catch (FileNotFoundException)
            {
                return null;
            }
        }
    }
}
