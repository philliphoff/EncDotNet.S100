using EncDotNet.S100.Core;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Verifies the digital signatures in an S-100 exchange set catalogue.
/// </summary>
/// <remarks>S-100 Edition 5.2.1 Part 15.</remarks>
public interface IExchangeSetVerifier
{
    /// <summary>
    /// Verifies the per-file digital signatures declared in <paramref name="catalogue"/>
    /// against the files accessible through <paramref name="source"/>.
    /// </summary>
    /// <param name="source">The asset source containing the exchange set files.</param>
    /// <param name="catalogue">The parsed exchange catalogue whose signatures to verify.</param>
    /// <param name="trustAnchors">Trust anchor options controlling certificate chain validation.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A result enumerating per-file verification outcomes without throwing.</returns>
    Task<ExchangeSetVerificationResult> VerifyAsync(
        IAssetSource source,
        ExchangeCatalogue catalogue,
        TrustAnchorOptions trustAnchors,
        CancellationToken cancellationToken = default);
}
