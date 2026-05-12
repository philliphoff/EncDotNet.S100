namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// A single certificate entry from the <c>certificates</c> block in an exchange catalogue.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-5.2. Each entry contains an X.509 certificate
/// (DER-encoded, base64-wrapped) with an <c>id</c> attribute that digital signatures
/// reference via <see cref="DigitalSignatureValue.CertificateRef"/>.
/// </remarks>
public sealed class CertificateEntry
{
    /// <summary>
    /// The <c>id</c> attribute — a URI that signatures use to reference this certificate
    /// (e.g. <c>"urn:mrn:iho:s62:iic:2C:key1"</c>).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The <c>issuer</c> attribute — the issuing authority name
    /// (e.g. <c>"IHO Scretariat"</c>).
    /// </summary>
    public string? Issuer { get; init; }

    /// <summary>
    /// The raw base64-decoded certificate bytes (DER-encoded X.509).
    /// </summary>
    public required byte[] Value { get; init; }
}
