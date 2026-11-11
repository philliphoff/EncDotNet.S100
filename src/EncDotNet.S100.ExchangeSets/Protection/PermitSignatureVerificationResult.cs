namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// The result of authenticating a <c>PERMIT.XML</c> file with its
/// <c>PERMIT.SIGN</c> signature.
/// </summary>
public sealed class PermitSignatureVerificationResult
{
    internal PermitSignatureVerificationResult(
        VerificationOutcome outcome,
        string? detail = null,
        StandaloneDigitalSignature? signature = null)
    {
        Outcome = outcome;
        Detail = detail;
        Signature = signature;
    }

    /// <summary>The signature-verification outcome.</summary>
    public VerificationOutcome Outcome { get; }

    /// <summary>Additional detail when authentication did not succeed.</summary>
    public string? Detail { get; }

    /// <summary>The parsed standalone signature, when parsing succeeded.</summary>
    public StandaloneDigitalSignature? Signature { get; }

    /// <summary>Whether the permit signature and certificate trust are valid.</summary>
    public bool IsValid => Outcome == VerificationOutcome.Ok;
}
