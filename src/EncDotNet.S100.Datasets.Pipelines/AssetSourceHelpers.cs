using System.IO;
using System.Threading;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Shared helpers for opening dataset content from an
/// <see cref="IAssetSource"/> (e.g. <c>FileSystemAssetSource</c> or
/// <c>ZipAssetSource</c>) inside the synchronous constructors used by the
/// per-spec dataset processors.
/// </summary>
/// <remarks>
/// The current per-spec readers (S-101, S-102, S-104, S-111, S-122,
/// S-124, S-125, S-127, S-128, S-129, S-411, S-421, S-57) all read the
/// dataset fully into memory at <c>Open</c>-time, so the asset stream
/// only needs to live for the duration of the processor's constructor.
/// However, several readers — notably HDF5 via PureHDF and ISO 8211 via
/// <c>EncDotNet.Iso8211</c> — require a seekable stream. ZIP entry
/// streams are forward-only, so this helper copies non-seekable
/// streams into a <see cref="MemoryStream"/> before returning.
/// </remarks>
internal static class AssetSourceHelpers
{
    /// <summary>
    /// Opens <paramref name="relativePath"/> from <paramref name="source"/>
    /// and returns a seekable stream positioned at the start. Non-seekable
    /// streams (e.g. <c>ZipArchiveEntry.Open()</c>) are copied into a
    /// <see cref="MemoryStream"/>; the original stream is disposed.
    /// </summary>
    public static Stream OpenSeekable(
        IAssetSource source,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var stream = source.OpenAsync(relativePath, cancellationToken).GetAwaiter().GetResult();
        if (stream.CanSeek)
        {
            return stream;
        }

        try
        {
            var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            buffer.Position = 0;
            return buffer;
        }
        finally
        {
            stream.Dispose();
        }
    }

    /// <summary>
    /// Returns the file name component of a ZIP-style or file-system
    /// relative path. Used by processors to populate their
    /// <c>_fileName</c> for diagnostic strings.
    /// </summary>
    public static string GetFileName(string relativePath)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);
        var normalized = relativePath.Replace('\\', '/');
        var idx = normalized.LastIndexOf('/');
        return idx < 0 ? normalized : normalized[(idx + 1)..];
    }
}
