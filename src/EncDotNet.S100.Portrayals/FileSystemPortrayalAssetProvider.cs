namespace EncDotNet.S100.Portrayals;

/// <summary>
/// A portrayal catalogue provider backed by a local file system directory.
/// </summary>
public sealed class FileSystemPortrayalCatalogue : IPortrayalCatalogueProvider
{
    private readonly string _basePath;

    private FileSystemPortrayalCatalogue(string basePath, PortrayalCatalogue catalogue)
    {
        _basePath = basePath;
        Catalogue = catalogue;
    }

    /// <inheritdoc />
    public PortrayalCatalogue Catalogue { get; }

    /// <summary>
    /// Creates a <see cref="FileSystemPortrayalCatalogue"/> from a directory containing a
    /// <c>portrayal_catalogue.xml</c> file and its referenced assets.
    /// </summary>
    /// <param name="directoryPath">The path to the portrayal catalogue directory.</param>
    /// <param name="catalogueFileName">The name of the catalogue XML file. Defaults to <c>portrayal_catalogue.xml</c>.</param>
    public static FileSystemPortrayalCatalogue Create(string directoryPath, string catalogueFileName = "portrayal_catalogue.xml")
    {
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);

        string fullDir = Path.GetFullPath(directoryPath);
        string cataloguePath = Path.Combine(fullDir, catalogueFileName);
        var catalogue = PortrayalCatalogueReader.Read(cataloguePath);

        return new FileSystemPortrayalCatalogue(fullDir, catalogue);
    }

    /// <inheritdoc />
    public Task<Stream> FetchAssetAsync(CatalogItem item, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(item);
        return FetchFileAsync(item.FileName);
    }

    /// <inheritdoc />
    public Task<Stream> FetchAssetAsync(RuleFile ruleFile, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ruleFile);
        return FetchFileAsync(ruleFile.FileName);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // No unmanaged resources; present for future extensibility (e.g. file locks).
    }

    private Task<Stream> FetchFileAsync(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        string fullPath = Path.GetFullPath(Path.Combine(_basePath, fileName));

        // Prevent path traversal
        if (!fullPath.StartsWith(_basePath, StringComparison.Ordinal))
        {
            throw new ArgumentException("File name must not navigate outside the catalogue directory.", nameof(fileName));
        }

        Stream stream = File.OpenRead(fullPath);
        return Task.FromResult(stream);
    }
}
