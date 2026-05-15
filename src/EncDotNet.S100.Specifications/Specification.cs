using System.Reflection;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Specifications;

/// <summary>
/// Provides access to bundled S-100 specification assets (Feature Catalogues and Portrayal Catalogues).
/// </summary>
public static class Specification
{
    private static readonly Assembly ResourceAssembly = typeof(Specification).Assembly;
    private const string ContentPrefix = "EncDotNet.S100.Specifications.content";

    /// <summary>
    /// Product specifications that have bundled assets in this assembly.
    /// </summary>
    public static IReadOnlyList<string> AvailableSpecs { get; } = ["S-101", "S-102", "S-104", "S-111", "S-122", "S-124", "S-125", "S-127", "S-128", "S-129", "S-131", "S-201", "S-411", "S-421"];

    /// <summary>
    /// Strongly-typed view of <see cref="AvailableSpecs"/>. Each entry has its
    /// canonical name (e.g. "S-101") and a default <see cref="SpecVersion"/>
    /// of <c>0.0.0</c>, signaling "edition unspecified" — no per-bundle manifest
    /// currently records which spec edition the bundled assets target. To
    /// determine the actual catalogue version, parse the bundled Feature or
    /// Portrayal Catalogue and inspect its <c>CatalogueRef</c>.
    /// </summary>
    public static IReadOnlyList<SpecRef> AvailableSpecRefs { get; } =
        AvailableSpecs.Select(name => new SpecRef(name, default)).ToList().AsReadOnly();

    /// <summary>
    /// Opens the bundled Feature Catalogue XML for the given product specification as a readable stream.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101", "S-102", "S-104", "S-111").</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A stream containing the Feature Catalogue XML.</returns>
    public static Task<Stream> OpenFeatureCatalogueAsync(string productSpec, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);

        using var source = CreateAssetSource(productSpec, "fc");
        return source.OpenAsync("FeatureCatalogue.xml", cancellationToken);
    }

    /// <summary>
    /// Tries to open the bundled Feature Catalogue XML for the given product specification.
    /// Returns null if no bundled catalogue is available.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101", "S-102", "S-104", "S-111").</param>
    /// <returns>A stream containing the Feature Catalogue XML, or null if not found.</returns>
    public static Stream? TryOpenFeatureCatalogue(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);

        try
        {
            var source = CreateAssetSource(productSpec, "fc");
            return source.OpenAsync("FeatureCatalogue.xml").GetAwaiter().GetResult();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true if a bundled Feature Catalogue exists for the given product specification.
    /// </summary>
    public static bool HasFeatureCatalogue(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);

        string normalized = productSpec.Replace("-", "");
        string resourceName = $"{ContentPrefix}.{normalized}.fc.FeatureCatalogue.xml";
        return ResourceAssembly.GetManifestResourceInfo(resourceName) is not null;
    }

    /// <summary>
    /// Creates an <see cref="IAssetSource"/> rooted at the bundled Feature
    /// Catalogue directory for the given product specification. The source
    /// is wrapped in a <see cref="CachingAssetSource"/> so repeated reads
    /// of <c>FeatureCatalogue.xml</c> are served from memory after the
    /// first read.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101", "S-102").</param>
    /// <returns>An asset source for the bundled Feature Catalogue contents.</returns>
    /// <remarks>
    /// This parallels <see cref="CreatePortrayalCatalogueSource(string)"/>
    /// and lets callers register the bundled FC with
    /// <c>FeatureCatalogueManager.SetSource</c> so the parse cache can be
    /// preceded by an asset-bytes cache. See S-100 Edition 5.2.1 Part 4 §6
    /// for the Feature Catalogue model.
    /// </remarks>
    public static IAssetSource CreateFeatureCatalogueSource(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);
        return new CachingAssetSource(CreateAssetSource(productSpec, "fc"));
    }

    /// <summary>
    /// Returns true if a bundled Portrayal Catalogue exists for the given product specification.
    /// </summary>
    public static bool HasPortrayalCatalogue(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);

        string normalized = productSpec.Replace("-", "");
        string resourceName = $"{ContentPrefix}.{normalized}.pc.portrayal_catalogue.xml";
        return ResourceAssembly.GetManifestResourceInfo(resourceName) is not null;
    }

    /// <summary>
    /// Creates an <see cref="IAssetSource"/> rooted at the bundled Portrayal Catalogue directory
    /// for the given product specification.
    /// </summary>
    /// <param name="productSpec">The product specification identifier (e.g. "S-101", "S-102", "S-111").</param>
    /// <returns>An asset source for the Portrayal Catalogue contents.</returns>
    public static IAssetSource CreatePortrayalCatalogueSource(string productSpec)
    {
        ArgumentException.ThrowIfNullOrEmpty(productSpec);

        // Wrap the embedded source in a caching decorator so repeated
        // reads of the same Portrayal Catalogue asset (Lua source, XSLT
        // templates, SVG symbols, palette XML; see S-100 Edition 5.2.1
        // Part 9 §3) are served from memory after the first read.
        return new CachingAssetSource(CreateAssetSource(productSpec, "pc"));
    }

    private static EmbeddedAssetSource CreateAssetSource(string productSpec, string subfolder)
    {
        // Normalize "S-111" → "S111" to match the content directory names.
        string normalized = productSpec.Replace("-", "");
        string prefix = $"{ContentPrefix}.{normalized}.{subfolder}";
        return EmbeddedAssetSource.Create(ResourceAssembly, prefix);
    }
}
