namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Identifies the XML form used for an exchange-catalogue digital signature.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-8.11.3 through §15-8.11.5.
/// </remarks>
public enum DigitalSignatureKind
{
    /// <summary>
    /// The legacy <c>S100_SE_DigitalSignature</c> form.
    /// </summary>
    Legacy,

    /// <summary>
    /// A signature over a resource representation selected by
    /// <see cref="DigitalSignatureValue.DataStatus"/>.
    /// </summary>
    SignatureOnData,

    /// <summary>
    /// A signature over the decoded ASN.1 R/S bytes of another signature.
    /// </summary>
    SignatureOnSignature,
}
