using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Options that configure the trust anchors used during exchange set signature verification.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15. The IHO Scheme Administrator (SA) public key is the
/// root of trust. For interoperability testing, the IHO publishes test SA keys.
/// </remarks>
public sealed class TrustAnchorOptions
{
    /// <summary>
    /// Trusted root certificates (IHO Scheme Administrator public keys).
    /// When empty, certificate chain validation is skipped and
    /// <see cref="AllowUntrustedCertificates"/> controls behaviour.
    /// </summary>
    public IReadOnlyList<X509Certificate2> TrustedRoots { get; init; } = [];

    /// <summary>
    /// When <c>true</c>, signature verification proceeds even when the
    /// signing certificate cannot be chained to a trusted root. Useful
    /// for development and inspection workflows.
    /// </summary>
    public bool AllowUntrustedCertificates { get; init; }
}
