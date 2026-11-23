using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Options that configure the trust anchors used during exchange set signature verification.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15. The IHO Scheme Administrator (SA) public key is the
/// root of trust. For interoperability testing, the IHO publishes test SA keys.
/// Only these roots are trusted: the operating system's certificate store is never
/// consulted.
/// </remarks>
public sealed class TrustAnchorOptions
{
    /// <summary>
    /// Trusted root certificates (IHO Scheme Administrator public keys). A
    /// signature is <see cref="VerificationOutcome.Ok"/> only when its signing
    /// certificate chains to one of these. When none chains, or the list is
    /// empty, <see cref="AllowUntrustedCertificates"/> decides the outcome.
    /// </summary>
    public IReadOnlyList<X509Certificate2> TrustedRoots { get; init; } = [];

    /// <summary>
    /// When <c>true</c>, a correct signature whose certificate doesn't chain to
    /// any of <see cref="TrustedRoots"/> is reported as
    /// <see cref="VerificationOutcome.SignatureValidUntrusted"/> rather than
    /// failing as <see cref="VerificationOutcome.CertificateUntrusted"/>. The
    /// chain is still checked against <see cref="TrustedRoots"/>, so a signer
    /// that does chain is still <see cref="VerificationOutcome.Ok"/>. Useful
    /// for inspection workflows that show data whatever its signer.
    /// </summary>
    public bool AllowUntrustedCertificates { get; init; }
}
