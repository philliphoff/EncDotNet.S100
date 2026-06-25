using System;
using System.IO;
using System.Text;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Resolves the textual content of an externally referenced text file (named
/// by an S-100 <c>fileReference</c> attribute — S-101 Feature Catalogue, alias
/// <c>TXTDSC</c> / <c>NTXTDS</c>) from the dataset's exchange-set
/// <see cref="IAssetSource"/>. The referenced file is looked up relative to
/// the dataset's own directory first (the common co-location convention), then
/// at the exchange-set root, mirroring how an ECDIS locates the support file
/// that backs a feature's textual description.
/// </summary>
/// <remarks>
/// <para>
/// Resolution is synchronous because the pick / object-info path that consumes
/// it is synchronous; support text files are small and the underlying
/// <c>FileSystemAssetSource</c> / <c>ZipAssetSource</c> reads are memory-backed
/// (no network I/O), so the awaiter-result bridge is bounded.
/// </para>
/// <para>
/// Content is decoded as UTF-8 when valid, otherwise as ISO/IEC 8859-1
/// (Latin-1), which is the legacy character set most S-57/S-101 producers use
/// for support text files. Missing, oversized, or unreadable files resolve to
/// <c>null</c> so the pick report keeps showing the bare file name.
/// </para>
/// </remarks>
public sealed class ExternalTextFileResolver
{
    /// <summary>
    /// Upper bound on the size of a referenced text file that will be read
    /// into memory. Support text files are short; anything larger is treated
    /// as unresolvable to bound the cost of a pick gesture.
    /// </summary>
    public const long MaxFileSizeBytes = 4 * 1024 * 1024;

    private readonly IAssetSource _source;
    private readonly string? _baseDirectory;

    /// <summary>
    /// Initializes a new <see cref="ExternalTextFileResolver"/>.
    /// </summary>
    /// <param name="source">
    /// The asset source backing the dataset's exchange set (folder or ZIP).
    /// </param>
    /// <param name="datasetRelativePath">
    /// The source-relative path of the dataset whose features carry the file
    /// references; its directory is used as the primary lookup location. May
    /// be <c>null</c> or a bare file name, in which case only the exchange-set
    /// root is searched.
    /// </param>
    public ExternalTextFileResolver(IAssetSource source, string? datasetRelativePath = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _baseDirectory = GetDirectory(datasetRelativePath);
    }

    /// <summary>
    /// Returns a <see cref="Func{T,TResult}"/> view of this resolver, suitable
    /// for passing to <see cref="FeatureInfoBuilder.ResolveFileReferences"/>.
    /// </summary>
    public Func<string, string?> AsDelegate() => Resolve;

    /// <summary>
    /// Resolves the textual content of the file named <paramref name="fileName"/>,
    /// or <c>null</c> when it cannot be located or read within the size bound.
    /// </summary>
    public string? Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return null;

        string normalized;
        try
        {
            normalized = ExchangeSets.ExchangeSet.NormalizeFileName(fileName);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (normalized.Length == 0)
            return null;

        // Prefer the dataset's own directory, then fall back to the exchange
        // set root. Skip the second probe when the file name already carries a
        // path or no base directory is known.
        if (!string.IsNullOrEmpty(_baseDirectory)
            && !normalized.Contains('/'))
        {
            var candidate = _baseDirectory + "/" + normalized;
            if (TryRead(candidate, out var text))
                return text;
        }

        return TryRead(normalized, out var rootText) ? rootText : null;
    }

    private bool TryRead(string relativePath, out string? text)
    {
        text = null;
        try
        {
            using var stream = AssetSourceHelpers.OpenSeekable(_source, relativePath);
            if (stream.CanSeek && stream.Length > MaxFileSizeBytes)
                return false;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            if (buffer.Length > MaxFileSizeBytes)
                return false;

            text = Decode(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or FileNotFoundException
            or DirectoryNotFoundException
            or InvalidOperationException
            or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// Decodes <paramref name="bytes"/> as UTF-8 when valid (honoring a BOM),
    /// otherwise as ISO/IEC 8859-1 (Latin-1).
    /// </summary>
    internal static string Decode(ReadOnlySpan<byte> bytes)
    {
        // Honor a UTF-8 BOM if present.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes[3..]);

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return strict.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    private static string? GetDirectory(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var normalized = relativePath.Replace('\\', '/').TrimEnd('/');
        var idx = normalized.LastIndexOf('/');
        return idx <= 0 ? null : normalized[..idx];
    }
}
