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
    /// <exception cref="DatasetPermitException">
    /// The requested protected dataset is not authorized by its permit.
    /// </exception>
    public async Task<Stream> OpenAsync(string relativePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(relativePath);

        var fileName = GetFileName(relativePath);
        if (!_keyProvider.TryGetCellKey(fileName, out var cellKey) || cellKey is null)
        {
            // No key for this file: it is not protected (e.g. the catalogue).
            return await _inner.OpenAsync(relativePath, cancellationToken).ConfigureAwait(false);
        }

        var ciphertext = await ProtectedContentReader.ReadAllBytesAsync(
            _inner,
            relativePath,
            cancellationToken).ConfigureAwait(false);
        byte[] plaintext;
        try
        {
            plaintext = S100Cipher.DecryptDataset(ciphertext, cellKey);
        }
        finally
        {
            Array.Clear(cellKey);
        }

        if (_decompress && ProtectedContentReader.IsZipArchive(plaintext))
        {
            plaintext = ProtectedContentReader.ExtractSingleEntry(plaintext);
        }

        return new MemoryStream(plaintext, writable: false);
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
