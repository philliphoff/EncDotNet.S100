namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// A parsed Part 15 standalone signature document used by
/// <c>PERMIT.SIGN</c> and <c>CATALOG.SIGN</c>.
/// </summary>
/// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.11.2.</remarks>
public sealed class StandaloneDigitalSignature
{
    /// <summary>The name of the signed resource.</summary>
    public required string FileName { get; init; }

    /// <summary>The certificates needed to authenticate the signature.</summary>
    public required CertificateBlock Certificates { get; init; }

    /// <summary>The signature over the resource bytes.</summary>
    public required DigitalSignatureValue Signature { get; init; }
}
