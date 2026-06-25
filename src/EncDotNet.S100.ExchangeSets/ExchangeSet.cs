using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// An exchange set backed by an <see cref="IAssetSource"/>.
/// </summary>
public sealed class ExchangeSet : IDisposable
{
    private readonly IAssetSource _source;

    /// <summary>
    /// Gets the asset source backing this exchange set. Exposed so callers
    /// (e.g. bulk loaders) can pass it directly to per-spec dataset
    /// processors that accept an <see cref="IAssetSource"/>.
    /// </summary>
    public IAssetSource Source => _source;

    /// <summary>
    /// Initializes a new instance of <see cref="ExchangeSet"/> with the given source and catalogue.
    /// </summary>
    /// <param name="source">The asset source used to fetch referenced files.</param>
    /// <param name="catalogue">The parsed exchange catalogue.</param>
    public ExchangeSet(IAssetSource source, ExchangeCatalogue catalogue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        _source = source;
        Catalogue = catalogue;
    }

    /// <summary>
    /// Gets the parsed exchange catalogue metadata.
    /// </summary>
    public ExchangeCatalogue Catalogue { get; }

    /// <summary>
    /// Opens an <see cref="ExchangeSet"/> by reading the catalogue from the given source.
    /// </summary>
    /// <param name="source">The asset source containing the exchange set.</param>
    /// <param name="cataloguePath">The relative path to the catalogue XML file within the source.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    public static async Task<ExchangeSet> OpenAsync(IAssetSource source, string cataloguePath = "CATALOG.XML", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(cataloguePath);

        await using var stream = await source.OpenAsync(cataloguePath, cancellationToken);
        var catalogue = ExchangeCatalogueReader.Read(stream);
        return new ExchangeSet(source, catalogue);
    }

    /// <summary>
    /// Fetches the content of a dataset file referenced by the catalogue.
    /// </summary>
    public Task<Stream> FetchDatasetAsync(DatasetDiscoveryMetadata dataset, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return _source.OpenAsync(ResolveRelativePath(dataset.FilePath, dataset.FileName), cancellationToken);
    }

    /// <summary>
    /// Fetches the content of a support file referenced by the catalogue.
    /// </summary>
    public Task<Stream> FetchSupportFileAsync(SupportFileDiscoveryMetadata supportFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(supportFile);
        return _source.OpenAsync(ResolveRelativePath(supportFile.FilePath, supportFile.FileName), cancellationToken);
    }

    /// <summary>
    /// Fetches the content of a sub-catalogue file referenced by the catalogue.
    /// </summary>
    public Task<Stream> FetchCatalogueFileAsync(CatalogueDiscoveryMetadata catalogueFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(catalogueFile);
        return _source.OpenAsync(ResolveRelativePath(catalogueFile.FilePath, catalogueFile.FileName), cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose() => _source.Dispose();

    /// <summary>
    /// Normalizes a catalogue-declared file name into a source-relative path:
    /// strips the <c>file:/</c> URI prefix, converts Windows-style
    /// backslash separators to forward slashes, and removes any leading
    /// slashes (which S-100 catalogues occasionally use to denote
    /// "from the exchange set root" but which would otherwise be treated
    /// as an absolute path by the underlying asset source).
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 17.</remarks>
    public static string NormalizeFileName(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var name = fileName;

        if (name.StartsWith("file:/", StringComparison.OrdinalIgnoreCase))
        {
            name = name["file:/".Length..];
        }

        name = name.Replace('\\', '/').TrimStart('/');

        return name;
    }

    /// <summary>
    /// Resolves the source-relative path of a catalogue entry from its
    /// optional <c>filePath</c> directory and its <c>fileName</c>.
    /// </summary>
    /// <param name="filePath">
    /// The directory of the file relative to the exchange set root, as
    /// declared by the catalogue's <c>filePath</c> element. May be
    /// <see langword="null"/>, empty, Windows-separated, or carry a
    /// leading slash. When absent, the file is assumed to live at the
    /// root (or the <paramref name="fileName"/> itself carries the path).
    /// </param>
    /// <param name="fileName">The catalogue-declared file name.</param>
    /// <returns>A normalized, forward-slashed, root-relative path.</returns>
    /// <remarks>
    /// S-100 Edition 5.2.1 Part 17. Some producers (e.g. UKHO S-101,
    /// NOAA S-102) place datasets in sub-directories and declare the
    /// directory separately via <c>filePath</c> while <c>fileName</c>
    /// carries only the bare file name; others fold the whole path into
    /// <c>fileName</c>. Both forms resolve here.
    /// </remarks>
    public static string ResolveRelativePath(string? filePath, string fileName)
    {
        var normalizedName = NormalizeFileName(fileName);

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return normalizedName;
        }

        var directory = filePath.Replace('\\', '/').Trim().Trim('/');
        if (directory.Length == 0)
        {
            return normalizedName;
        }

        // Guard against double-prefixing when the file name already
        // carries the directory portion.
        if (normalizedName.StartsWith(directory + "/", StringComparison.OrdinalIgnoreCase))
        {
            return normalizedName;
        }

        // Guard against double-suffixing when the directory already carries
        // the file name (some producers fold the whole path into the
        // <fileLocation> element). S-100 Edition 5.2.1 Part 17.
        if (directory.Equals(normalizedName, StringComparison.OrdinalIgnoreCase)
            || directory.EndsWith("/" + normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            return directory;
        }

        return directory + "/" + normalizedName;
    }
}
