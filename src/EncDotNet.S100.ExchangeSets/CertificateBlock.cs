namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Represents the <c>certificates</c> block in an exchange catalogue, containing
/// the scheme administrator identifier and the certificate chain.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-5. The block embeds the public-key certificates
/// used to verify the per-file digital signatures in the catalogue.
/// </remarks>
public sealed class CertificateBlock
{
    /// <summary>
    /// The <c>id</c> attribute of the <c>schemeAdministrator</c> element
    /// (e.g. <c>"urn:mrn:iho:s62:iic:2C:key1"</c>).
    /// </summary>
    public string? SchemeAdministratorId { get; init; }

    /// <summary>
    /// The certificates declared in this block.
    /// </summary>
    public IReadOnlyList<CertificateEntry> Certificates { get; init; } = [];
}
