using System.IO.Compression;

namespace EncDotNet.S100.Core;

/// <summary>
/// An asset source backed by a ZIP archive.
/// </summary>
public sealed class ZipAssetSource : IAssetSource
{
    private readonly ZipArchive _archive;
    private readonly string _basePath;

    private ZipAssetSource(ZipArchive archive, string basePath)
    {
        _archive = archive;
        _basePath = basePath;
    }

    /// <summary>
    /// Creates a <see cref="ZipAssetSource"/> wrapping an existing archive.
    /// The source takes ownership and will dispose it.
    /// </summary>
    /// <param name="archive">The ZIP archive to read from.</param>
    /// <param name="basePath">
    /// An optional path prefix prepended to all relative paths when locating entries
    /// (e.g. <c>"S101/"</c> if entries are stored under that folder in the archive).
    /// </param>
    public static ZipAssetSource Create(ZipArchive archive, string basePath = "")
    {
        ArgumentNullException.ThrowIfNull(archive);
        return new ZipAssetSource(archive, basePath ?? "");
    }

    /// <summary>
    /// Creates a <see cref="ZipAssetSource"/> from a ZIP file on disk.
    /// </summary>
    /// <param name="zipPath">The path to the ZIP file.</param>
    /// <param name="basePath">An optional entry path prefix.</param>
    public static ZipAssetSource Create(string zipPath, string basePath = "")
    {
        ArgumentException.ThrowIfNullOrEmpty(zipPath);
        var archive = ZipFile.OpenRead(zipPath);
        return new ZipAssetSource(archive, basePath ?? "");
    }

    /// <summary>
    /// Creates a <see cref="ZipAssetSource"/> from a stream containing ZIP data.
    /// The source takes ownership of the stream.
    /// </summary>
    /// <param name="stream">A readable stream containing the ZIP data.</param>
    /// <param name="basePath">An optional entry path prefix.</param>
    public static ZipAssetSource Create(Stream stream, string basePath = "")
    {
        ArgumentNullException.ThrowIfNull(stream);
        var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        return new ZipAssetSource(archive, basePath ?? "");
    }

    /// <inheritdoc />
    public Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        string entryName = relativePath.Replace('\\', '/');

        // Prevent path traversal
        if (entryName.Contains(".."))
        {
            throw new ArgumentException("Path must not navigate outside the archive.", nameof(relativePath));
        }

        string fullEntryName = _basePath + entryName;

        var entry = _archive.GetEntry(fullEntryName)
            ?? throw new FileNotFoundException($"Entry '{fullEntryName}' not found in the archive.");

        Stream stream = entry.Open();
        return Task.FromResult(stream);
    }

    /// <inheritdoc />
    public void Dispose() => _archive.Dispose();
}
