using System.Xml;
using EncDotNet.S100.Core;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S129;

/// <summary>
/// S-129 portrayal catalogue. Extends <see cref="GmlPortrayalCatalogueBase"/>
/// with a custom XML resolver that handles S-129's sub-directory-based
/// XSLT template references.
/// </summary>
public sealed class S129PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    public S129PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }
    public override SpecRef Spec => new("S-129", default);

    protected override XmlResolver CreateXmlResolver(IReadOnlyDictionary<string, byte[]> registeredBytes) =>
        new S129XmlResolver(Provider, registeredBytes);

    private sealed class S129XmlResolver : AssetSourceXmlResolver
    {
        public S129XmlResolver(PortrayalCatalogueProvider provider, IReadOnlyDictionary<string, byte[]> registeredBytes)
            : base(provider, registeredBytes) { }

        protected override object? ResolveUnregistered(Uri absoluteUri, string fileName)
        {
            // SYNC BRIDGE: see GmlPortrayalCatalogueBase.FetchRuleFallbackXmlResolver
            // — XmlResolver.GetEntity is sync .NET API contract during XSLT
            // compile; the unregistered sub-paths cannot be enumerated upfront.
            var segments = absoluteUri.LocalPath.Replace('\\', '/').Split('/');
            for (int i = 0; i < segments.Length; i++)
            {
                if (segments[i].Equals("templates", StringComparison.OrdinalIgnoreCase))
                {
                    var relativePath = "Rules/" + string.Join("/", segments, i, segments.Length - i);
                    try
                    {
                        return Provider.FetchRuleAsync(relativePath).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        break;
                    }
                }
            }

            // Last resort: try by bare filename
            try
            {
                return Provider.FetchRuleAsync(fileName).GetAwaiter().GetResult();
            }
            catch
            {
                Console.Error.WriteLine($"[S129] Failed to resolve XSLT reference: {absoluteUri}");
                return null;
            }
        }
    }
}
