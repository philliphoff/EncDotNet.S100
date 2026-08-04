using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Opens the resource representation covered by an S-100 Part 15 signature.
/// </summary>
public interface ISignatureContentResolver
{
    /// <summary>
    /// Opens the requested raw, compressed, or unencrypted resource bytes.
    /// </summary>
    /// <param name="source">The asset source containing the stored resource.</param>
    /// <param name="request">The requested representation and discovery flags.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A readable stream containing the requested representation.</returns>
    Task<Stream> OpenAsync(
        IAssetSource source,
        SignatureContentRequest request,
        CancellationToken cancellationToken = default);
}
