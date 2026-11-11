namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Identifies the digital signature algorithm used to sign an exchange set file.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-8.11.7. The <c>digitalSignatureReference</c> element
/// in <c>S100_DatasetDiscoveryMetadata</c> and related types uses these values to
/// indicate which algorithm was used to produce the accompanying signature.
/// </remarks>
public enum DigitalSignatureAlgorithm
{
    /// <summary>The algorithm is not recognised.</summary>
    Unknown = 0,

    /// <summary>Digital Signature Algorithm (DSA) — legacy, S-63 derived.</summary>
    DSA = 1,

    /// <summary>
    /// Legacy ECDSA compatibility mode using the existing SHA-256/P1363 verifier.
    /// </summary>
    ECDSA = 2,

    /// <summary>
    /// ECDSA using NIST P-384 and SHA-384 for S-100 file-based authentication.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.4 and §15-8.7.</remarks>
    ECDSA384SHA2 = 3,
}
