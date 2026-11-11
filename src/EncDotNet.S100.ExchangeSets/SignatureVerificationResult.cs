namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The verification result for one digital signature on a resource.
/// </summary>
public sealed class SignatureVerificationResult
{
    /// <summary>The signature identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The signature form.</summary>
    public required DigitalSignatureKind Kind { get; init; }

    /// <summary>The verification outcome.</summary>
    public required VerificationOutcome Outcome { get; init; }

    /// <summary>The structured reason for a failed verification.</summary>
    public SignatureFailureReason FailureReason { get; init; }

    /// <summary>Optional diagnostic detail.</summary>
    public string? Detail { get; init; }
}
