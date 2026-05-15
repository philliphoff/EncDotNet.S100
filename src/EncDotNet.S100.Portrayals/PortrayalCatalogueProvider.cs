using EncDotNet.S100.Core;

namespace EncDotNet.S100.Portrayals;

/// <summary>
/// A portrayal catalogue provider backed by an <see cref="IAssetSource"/>.
/// </summary>
/// <remarks>
/// S-100 Ed 5.2 portrayal catalogues use a well-known directory layout where
/// the <c>&lt;fileName&gt;</c> in the catalogue XML contains only the bare
/// filename. Assets are stored in conventional subdirectories relative to
/// the catalogue root:
/// <list type="bullet">
///   <item><c>Rules/</c> — rule files (Lua, XSLT)</item>
///   <item><c>ColorProfiles/</c> — colour palette definitions</item>
///   <item><c>Symbols/</c> — SVG symbol files</item>
///   <item><c>LineStyles/</c> — line style definitions</item>
///   <item><c>AreaFills/</c> — area fill definitions</item>
///   <item><c>Pixmaps/</c> — raster symbol images</item>
///   <item><c>StyleSheets/</c> — XSLT/CSS stylesheets</item>
/// </list>
/// This provider prepends the conventional subdirectory when fetching assets.
/// </remarks>
public sealed class PortrayalCatalogueProvider : IDisposable
{
    private readonly IAssetSource _source;

    /// <summary>
    /// Initializes a new instance of <see cref="PortrayalCatalogueProvider"/> with the given source and catalogue.
    /// </summary>
    /// <param name="source">The asset source used to fetch referenced assets.</param>
    /// <param name="catalogue">The parsed portrayal catalogue.</param>
    public PortrayalCatalogueProvider(IAssetSource source, PortrayalCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        _source = source;
        Catalogue = catalogue;
    }

    /// <summary>
    /// Gets the parsed portrayal catalogue metadata.
    /// </summary>
    public PortrayalCatalogue Catalogue { get; }

    private IPortrayalAssetCache? _assetCache;

    /// <summary>
    /// The decoded-asset cache that backs every catalogue wrapper
    /// (compiled XSLT, SVG symbols, line styles, area fills, palettes,
    /// Lua scripts, Lua source strings).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Defaults to a fresh per-provider <see cref="PortrayalAssetCache"/>
    /// the first time it is read. <see cref="PortrayalCatalogueManager"/>
    /// stamps a <em>shared</em> per-<c>SpecRef</c> cache here (via
    /// <see cref="AttachAssetCache"/>) before handing the provider out,
    /// so two catalogue wrappers for the same spec share asset storage
    /// (asset-caching audit §6 PR-3).
    /// </para>
    /// <para>
    /// Direct callers of <see cref="OpenAsync(IAssetSource, string, CancellationToken)"/>
    /// — primarily tests — get an isolated per-provider cache, which
    /// preserves the contract verified by
    /// <c>LuaSourceCacheTests.S101_GetLuaSource_independent_caches_per_catalogue_instance</c>.
    /// </para>
    /// </remarks>
    public IPortrayalAssetCache AssetCache => _assetCache ??= new PortrayalAssetCache();

    /// <summary>
    /// Attaches a pre-existing <see cref="IPortrayalAssetCache"/> to
    /// this provider. Called by <see cref="PortrayalCatalogueManager"/>
    /// when materialising a provider so all catalogue wrappers for the
    /// same <c>SpecRef</c> share one cache. May be called at most once
    /// before any catalogue reads <see cref="AssetCache"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="cache"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// An asset cache has already been associated with this provider
    /// (either through a prior <see cref="AttachAssetCache"/> call or
    /// through lazy default-construction on first <see cref="AssetCache"/>
    /// read).
    /// </exception>
    internal void AttachAssetCache(IPortrayalAssetCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (_assetCache is not null)
        {
            throw new InvalidOperationException(
                "An asset cache has already been associated with this provider.");
        }
        _assetCache = cache;
    }

    /// <summary>
    /// Opens a <see cref="PortrayalCatalogueProvider"/> by reading the catalogue from the given source.
    /// </summary>
    /// <param name="source">The asset source containing the portrayal catalogue and assets.</param>
    /// <param name="cataloguePath">The relative path to the catalogue XML file within the source.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task<PortrayalCatalogueProvider> OpenAsync(IAssetSource source, string cataloguePath = "portrayal_catalogue.xml", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);

        await using var stream = await source.OpenAsync(cataloguePath, cancellationToken);
        var catalogue = PortrayalCatalogueReader.Read(stream);
        return new PortrayalCatalogueProvider(source, catalogue);
    }

    /// <summary>
    /// Fetches the content of a portrayal asset referenced by a catalogue item,
    /// looking in the given conventional subdirectory.
    /// </summary>
    /// <param name="item">The catalogue item whose asset to fetch.</param>
    /// <param name="subdirectory">
    /// The S-100 conventional subdirectory for this item type
    /// (e.g. <c>"ColorProfiles"</c>, <c>"Symbols"</c>).
    /// </param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public Task<Stream> FetchAssetAsync(CatalogItem item, string subdirectory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentException.ThrowIfNullOrEmpty(subdirectory);
        return _source.OpenAsync(ResolvePath(item.FileName, subdirectory), cancellationToken);
    }

    /// <summary>
    /// Fetches the content of a portrayal asset referenced by a rule file.
    /// Rule files are resolved under the <c>Rules/</c> subdirectory.
    /// </summary>
    public Task<Stream> FetchAssetAsync(RuleFile ruleFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ruleFile);
        return _source.OpenAsync(ResolvePath(ruleFile.FileName, "Rules"), cancellationToken);
    }

    /// <summary>
    /// Fetches a rule file by bare filename from the <c>Rules/</c> subdirectory.
    /// </summary>
    public Task<Stream> FetchRuleAsync(string fileName, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);
        return _source.OpenAsync(ResolvePath(fileName, "Rules"), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _source.Dispose();

    /// <summary>
    /// If <paramref name="fileName"/> is a bare filename (no path separators),
    /// prepend <paramref name="subdirectory"/>. If it already contains a path,
    /// use it as-is for backwards compatibility.
    /// </summary>
    private static string ResolvePath(string fileName, string subdirectory)
    {
        if (fileName.Contains('/') || fileName.Contains('\\'))
        {
            return fileName;
        }

        return $"{subdirectory}/{fileName}";
    }
}
