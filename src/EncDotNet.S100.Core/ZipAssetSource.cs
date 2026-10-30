using System.Diagnostics;
using System.IO.Compression;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Core;

/// <summary>
/// An asset source backed by a ZIP archive.
/// </summary>
/// <remarks>
/// Safe to read from several threads at once. A <see cref="ZipArchive"/> reads
/// every entry through one underlying stream, so each entry is read whole
/// under a lock and returned as a <see cref="MemoryStream"/>.
/// </remarks>
public sealed class ZipAssetSource : IAssetSource
{
    private readonly ZipArchive _archive;
    private readonly string _basePath;
    private readonly object _gate = new();

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

        using var activity = Telemetry.ActivitySource.StartActivity("s100.asset.read");
        activity?.SetTag(TelemetryTags.AssetKind, "zip");
        var start = Stopwatch.GetTimestamp();

        string entryName = relativePath.Replace('\\', '/');

        // Prevent path traversal
        if (entryName.Contains(".."))
        {
            throw new ArgumentException("Path must not navigate outside the archive.", nameof(relativePath));
        }

        string fullEntryName = _basePath + entryName;

        MemoryStream stream;
        lock (_gate)
        {
            var entry = _archive.GetEntry(fullEntryName)
                ?? throw new FileNotFoundException($"Entry '{fullEntryName}' not found in the archive.");

            // Entries opened together interleave reads of the shared archive
            // stream and come out corrupt, so read this one before releasing it.
            stream = new MemoryStream(entry.Length is > 0 and <= int.MaxValue ? (int)entry.Length : 0);
            using (var entryStream = entry.Open())
            {
                entryStream.CopyTo(stream);
            }
        }

        stream.Position = 0;

        AssetMetrics.ReadDuration.Record(
            (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency,
            new KeyValuePair<string, object?>(TelemetryTags.AssetKind, "zip"));

        return Task.FromResult<Stream>(stream);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            _archive.Dispose();
        }
    }
}
