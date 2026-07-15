using System.IO.Compression;
using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// An <see cref="IAssetSource"/> decorator that transparently decrypts (and
/// optionally decompresses) the protected files of an S-100 Part 15 exchange
/// set as they are read, so downstream dataset readers and the
/// <see cref="ExchangeSetVerifier"/> can consume plaintext through the ordinary
/// <see cref="IAssetSource"/> contract.
/// </summary>
/// <remarks>
/// <para>
/// S-100 Edition 5.2.1 Part 15 §15-5, §15-6. A file is decrypted only when the
/// supplied <see cref="IDatasetKeyProvider"/> yields a cell key for it; files
/// with no key (for example the exchange catalogue or unencrypted support files)
/// are passed through unchanged. Because the digital signature is computed over
/// the unencrypted resource (§15-8.9), wrapping the source in this decorator
/// lets the existing signature verifier validate an encrypted set without
/// modification.
/// </para>
/// <para>
/// When <c>decompress</c> is enabled, a decrypted file that begins with the ZIP
/// local-file-header signature is treated as the single-file DEFLATE archive
/// mandated by §15-5.2 and its sole entry is returned; other content is returned
/// as-is.
/// </para>
/// </remarks>
public sealed class DecryptingAssetSource : IAssetSource
{
    private static readonly byte[] ZipLocalFileHeader = [0x50, 0x4B, 0x03, 0x04];

    private readonly IAssetSource _inner;
    private readonly IDatasetKeyProvider _keyProvider;
    private readonly bool _decompress;
    private readonly bool _ownsInner;

    /// <summary>
    /// Creates a decrypting asset source.
    /// </summary>
    /// <param name="inner">The underlying asset source to read encrypted bytes from.</param>
    /// <param name="keyProvider">The provider that resolves cell keys per dataset.</param>
    /// <param name="decompress">
    /// Whether to decompress decrypted files that are single-file ZIP archives
    /// (set when the exchange set declares <c>compressionFlag</c>, §15-5.3).
    /// </param>
    /// <param name="ownsInner">
    /// Whether disposing this instance should also dispose <paramref name="inner"/>.
    /// Defaults to <c>true</c>.
    /// </param>
    public DecryptingAssetSource(
        IAssetSource inner,
        IDatasetKeyProvider keyProvider,
        bool decompress = false,
        bool ownsInner = true)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _decompress = decompress;
        _ownsInner = ownsInner;
    }

    /// <inheritdoc />
    public async Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        string fileName = GetFileName(relativePath);
        if (!_keyProvider.TryGetCellKey(fileName, out byte[]? cellKey) || cellKey is null)
        {
            // No key for this file: it is not protected (e.g. the catalogue).
            return await _inner.OpenAsync(relativePath, cancellationToken).ConfigureAwait(false);
        }

        byte[] ciphertext = await ReadAllBytesAsync(relativePath, cancellationToken).ConfigureAwait(false);
        byte[] plaintext = S100Cipher.DecryptDataset(ciphertext, cellKey);
        Array.Clear(cellKey);

        if (_decompress && IsZipArchive(plaintext))
        {
            plaintext = ExtractSingleEntry(plaintext);
        }

        return new MemoryStream(plaintext, writable: false);
    }

    private async Task<byte[]> ReadAllBytesAsync(string relativePath, CancellationToken cancellationToken)
    {
        await using Stream stream = await _inner.OpenAsync(relativePath, cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream(
            capacity: stream.CanSeek ? (int)Math.Min(stream.Length, int.MaxValue) : 4096);
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static bool IsZipArchive(byte[] content) =>
        content.Length >= ZipLocalFileHeader.Length &&
        content.AsSpan(0, ZipLocalFileHeader.Length).SequenceEqual(ZipLocalFileHeader);

    private static byte[] ExtractSingleEntry(byte[] zipBytes)
    {
        using var zipStream = new MemoryStream(zipBytes, writable: false);
        using var archive = new ZipArchive(zipStream, ZipArchiveMode.Read);
        ZipArchiveEntry entry = archive.Entries.Count == 1
            ? archive.Entries[0]
            : throw new InvalidDataException(
                $"A compressed Part 15 resource must contain exactly one entry but contained {archive.Entries.Count}.");

        using Stream entryStream = entry.Open();
        using var output = new MemoryStream(
            capacity: entry.Length > 0 && entry.Length <= int.MaxValue ? (int)entry.Length : 4096);
        entryStream.CopyTo(output);
        return output.ToArray();
    }

    private static string GetFileName(string relativePath)
    {
        int slash = relativePath.LastIndexOfAny(['/', '\\']);
        return slash >= 0 ? relativePath[(slash + 1)..] : relativePath;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsInner)
        {
            _inner.Dispose();
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() =>
        _ownsInner ? _inner.DisposeAsync() : ValueTask.CompletedTask;
}
