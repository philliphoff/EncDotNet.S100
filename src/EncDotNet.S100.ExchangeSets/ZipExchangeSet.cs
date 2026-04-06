using System.IO.Compression;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// An exchange set provider backed by a ZIP archive.
/// </summary>
public sealed class ZipExchangeSet : ExchangeSetBase
{
    private readonly ZipArchive _archive;
    private readonly string _basePath;

    private ZipExchangeSet(ZipArchive archive, string basePath, ExchangeCatalogue catalogue)
        : base(catalogue)
    {
        _archive = archive;
        _basePath = basePath;
    }

    /// <summary>
    /// Creates a <see cref="ZipExchangeSet"/> from a ZIP file on disk.
    /// </summary>
    /// <param name="zipPath">The path to the ZIP archive.</param>
    /// <param name="catalogueEntryName">The name of the catalogue XML entry. Defaults to <c>CATALOG.XML</c>.</param>
    public static ZipExchangeSet Create(string zipPath, string catalogueEntryName = "CATALOG.XML")
    {
        ArgumentException.ThrowIfNullOrEmpty(zipPath);

        var archive = ZipFile.OpenRead(zipPath);
        try
        {
            return CreateFromArchive(archive, catalogueEntryName);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates a <see cref="ZipExchangeSet"/> from a stream containing a ZIP archive.
    /// </summary>
    /// <param name="stream">A readable stream containing the ZIP data. The provider takes ownership of the stream.</param>
    /// <param name="catalogueEntryName">The name of the catalogue XML entry. Defaults to <c>CATALOG.XML</c>.</param>
    public static ZipExchangeSet Create(Stream stream, string catalogueEntryName = "CATALOG.XML")
    {
        ArgumentNullException.ThrowIfNull(stream);

        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        try
        {
            return CreateFromArchive(archive, catalogueEntryName);
        }
        catch
        {
            archive.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    protected override Task<Stream> OpenFileAsync(string relativePath, CancellationToken cancellationToken)
    {
        // Normalize path separators to forward slashes (ZIP standard)
        string entryName = relativePath.Replace('\\', '/');

        // Prevent path traversal
        if (entryName.Contains(".."))
        {
            throw new ArgumentException("File name must not navigate outside the exchange set archive.", nameof(relativePath));
        }

        // Prepend the base path so entry lookup matches the archive layout
        string fullEntryName = _basePath + entryName;

        var entry = _archive.GetEntry(fullEntryName)
            ?? throw new FileNotFoundException($"Entry '{fullEntryName}' not found in the archive.");

        Stream stream = entry.Open();
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _archive.Dispose();
        }

        base.Dispose(disposing);
    }

    private static ZipExchangeSet CreateFromArchive(ZipArchive archive, string catalogueEntryName)
    {
        var entry = archive.GetEntry(catalogueEntryName)
            ?? throw new FileNotFoundException($"Catalogue entry '{catalogueEntryName}' not found in the archive.");

        // Derive the base path prefix from the catalogue entry (e.g. "S101/" from "S101/CATALOG.XML")
        int lastSlash = catalogueEntryName.LastIndexOf('/');
        string basePath = lastSlash >= 0 ? catalogueEntryName[..(lastSlash + 1)] : "";

        using var stream = entry.Open();
        var catalogue = ExchangeCatalogueReader.Read(stream);
        return new ZipExchangeSet(archive, basePath, catalogue);
    }
}
