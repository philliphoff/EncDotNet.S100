using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Resolves raw, compressed, and unencrypted resource representations for
/// S-100 Part 15 signature verification.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-5, §15-6, §15-8.8, and §15-8.11.6.
/// Encrypted signatures cover stored ciphertext and therefore require no
/// permit. Other representations use the supplied dataset key provider when
/// the discovery metadata declares data protection.
/// </remarks>
public sealed class Part15SignatureContentResolver : ISignatureContentResolver
{
    private readonly IDatasetKeyProvider? _keyProvider;

    /// <summary>
    /// Creates a resolver for unprotected resources.
    /// </summary>
    public Part15SignatureContentResolver()
    {
    }

    /// <summary>
    /// Creates a resolver that can also decrypt protected resources.
    /// </summary>
    /// <param name="keyProvider">The authenticated Part 15 dataset key provider.</param>
    public Part15SignatureContentResolver(IDatasetKeyProvider keyProvider)
    {
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
    }

    /// <inheritdoc />
    public async Task<Stream> OpenAsync(
        IAssetSource source,
        SignatureContentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RelativePath);

        ValidateRequest(request);

        if (CanUseStoredStream(request))
        {
            return await source.OpenAsync(request.RelativePath, cancellationToken)
                .ConfigureAwait(false);
        }

        var content = await ProtectedContentReader.ReadAllBytesAsync(
            source,
            request.RelativePath,
            cancellationToken).ConfigureAwait(false);

        if (request.DataProtection)
        {
            if (_keyProvider is null)
            {
                throw new InvalidOperationException(
                    $"A dataset key provider is required to resolve '{request.DataStatus}' bytes " +
                    $"for protected resource '{request.RelativePath}'.");
            }

            content = ProtectedContentReader.Decrypt(content, _keyProvider, request.RelativePath);
        }

        if (request.DataStatus == SignatureDataStatus.Unencrypted && request.CompressionFlag)
        {
            content = ProtectedContentReader.ExtractSingleEntry(content);
        }

        return new MemoryStream(content, writable: false);
    }

    private static bool CanUseStoredStream(SignatureContentRequest request) =>
        request.DataStatus == SignatureDataStatus.Encrypted ||
        (!request.DataProtection &&
            (request.DataStatus == SignatureDataStatus.Compressed ||
                !request.CompressionFlag));

    private static void ValidateRequest(SignatureContentRequest request)
    {
        if (request.DataStatus == SignatureDataStatus.Encrypted &&
            (!request.DataProtection || !request.CompressionFlag))
        {
            throw new InvalidDataException(
                "An encrypted signature requires a resource declared as compressed and protected.");
        }

        if (request.DataStatus == SignatureDataStatus.Compressed && !request.CompressionFlag)
        {
            throw new InvalidDataException(
                "A compressed signature requires a resource with compressionFlag set.");
        }
    }
}
