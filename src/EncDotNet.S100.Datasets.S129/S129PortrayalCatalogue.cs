using System.Xml;
using EncDotNet.S100.Portrayals;

namespace EncDotNet.S100.Datasets.S129;

/// <summary>
/// S-129 portrayal catalogue. Extends <see cref="GmlPortrayalCatalogueBase"/>
/// with a custom XML resolver that handles the S-129 PC's sub-directory
/// based XSLT include structure.
/// </summary>
public sealed class S129PortrayalCatalogue : GmlPortrayalCatalogueBase
{
    /// <summary>Initializes a new S-129 portrayal catalogue.</summary>
    public S129PortrayalCatalogue(PortrayalCatalogueProvider provider) : base(provider) { }

    /// <inheritdoc/>
    public override string ProductSpec => "S-129";

    /// <inheritdoc/>
    protected override XmlResolver CreateXmlResolver() => new S129XmlResolver(Provider);

    /// <summary>
    /// Custom resolver for S-129 that handles sub-directory based XSLT
    /// includes (e.g. <c>templates/TextTemplate.xsl</c>).
    /// </summary>
    private sealed class S129XmlResolver : AssetSourceXmlResolver
    {
        public S129XmlResolver(PortrayalCatalogueProvider provider) : base(provider) { }

        /// <inheritdoc/>
        protected override object? ResolveUnregistered(Uri absoluteUri, string fileName)
        {
            // S-129 XSLT rules reference templates in sub-directories.
            // Extract the trailing path from a "templates" segment onwards.
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

            // Last resort: try by bare filename from Rules/
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
