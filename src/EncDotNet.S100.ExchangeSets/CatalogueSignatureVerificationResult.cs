using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The result of checking an exchange catalogue against its
/// <c>CATALOG.SIGN</c> signature.
/// </summary>
/// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.11.2.</remarks>
public sealed class CatalogueSignatureVerificationResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="outcome">The outcome of the check.</param>
    /// <param name="detail">Additional detail when the check didn't succeed.</param>
    /// <param name="signature">The parsed <c>CATALOG.SIGN</c>, when it could be read.</param>
    public CatalogueSignatureVerificationResult(
        VerificationOutcome outcome,
        string? detail = null,
        StandaloneDigitalSignature? signature = null)
    {
        Outcome = outcome;
        Detail = detail;
        Signature = signature;
    }

    /// <summary>
    /// The outcome: <see cref="VerificationOutcome.Ok"/> when the catalogue
    /// matches its signature, <see cref="VerificationOutcome.NotSigned"/> when
    /// the exchange set has no <c>CATALOG.SIGN</c>, and
    /// <see cref="VerificationOutcome.FileMissing"/> when the signed catalogue
    /// isn't there.
    /// </summary>
    public VerificationOutcome Outcome { get; }

    /// <summary>Additional detail when the check didn't succeed.</summary>
    public string? Detail { get; }

    /// <summary>The parsed <c>CATALOG.SIGN</c>, when it could be read.</summary>
    public StandaloneDigitalSignature? Signature { get; }

    /// <summary>Whether the catalogue signature and its certificate are valid.</summary>
    public bool IsValid => Outcome == VerificationOutcome.Ok;
}
