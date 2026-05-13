namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Identifies the digital signature algorithm used to sign an exchange set file.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-4.1. The <c>digitalSignatureReference</c> element
/// in <c>S100_DatasetDiscoveryMetadata</c> and related types uses these values to
/// indicate which algorithm was used to produce the accompanying signature.
/// </remarks>
public enum DigitalSignatureAlgorithm
{
    /// <summary>The algorithm is not recognised.</summary>
    Unknown = 0,

    /// <summary>Digital Signature Algorithm (DSA) — legacy, S-63 derived.</summary>
    DSA = 1,

    /// <summary>Elliptic Curve Digital Signature Algorithm (ECDSA P-256) — S-100 Part 15.</summary>
    ECDSA = 2,
}
