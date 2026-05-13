namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The aggregate result of verifying all files in an exchange set.
/// </summary>
public sealed class ExchangeSetVerificationResult
{
    /// <summary>Per-file verification results.</summary>
    public required IReadOnlyList<FileVerificationResult> FileResults { get; init; }

    /// <summary>
    /// Returns <c>true</c> when every file in the exchange set has
    /// <see cref="VerificationOutcome.Ok"/> as its outcome.
    /// </summary>
    public bool AllValid => FileResults.All(r => r.Outcome == VerificationOutcome.Ok);

    /// <summary>
    /// Returns <c>true</c> when at least one file has
    /// <see cref="VerificationOutcome.SignatureInvalid"/>.
    /// </summary>
    public bool HasInvalidSignatures => FileResults.Any(r => r.Outcome == VerificationOutcome.SignatureInvalid);

    /// <summary>
    /// Returns <c>true</c> when no file carries a signature
    /// (all are <see cref="VerificationOutcome.NotSigned"/>).
    /// </summary>
    public bool IsUnsigned => FileResults.All(r => r.Outcome == VerificationOutcome.NotSigned);
}
