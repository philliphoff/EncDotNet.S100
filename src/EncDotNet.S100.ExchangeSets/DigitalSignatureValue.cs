namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Represents a parsed <c>S100_SE_DigitalSignature</c> element from an exchange catalogue.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-4.2. Each dataset, support file, and catalogue
/// discovery metadata entry may carry a digital signature value that can be verified
/// against the certificate identified by <see cref="CertificateRef"/>.
/// </remarks>
public sealed class DigitalSignatureValue
{
    /// <summary>
    /// The <c>id</c> attribute of the <c>S100_SE_DigitalSignature</c> element
    /// (e.g. <c>"SIG101AA0000DS0009"</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The <c>certificateRef</c> attribute — a URI that identifies the certificate
    /// whose public key should be used to verify this signature
    /// (e.g. <c>"urn:mrn:iho:s62:iic:2C:key1"</c>).
    /// </summary>
    public required string CertificateRef { get; init; }

    /// <summary>
    /// The raw base64-decoded signature bytes.
    /// </summary>
    public required byte[] Value { get; init; }
}
