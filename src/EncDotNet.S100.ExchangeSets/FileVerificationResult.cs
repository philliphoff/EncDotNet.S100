namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The verification result for a single file in an exchange set, carrying two
/// independent dimensions: the digital-signature outcome
/// (<see cref="Outcome"/>) and the checksum/integrity outcome
/// (<see cref="ChecksumOutcome"/>).
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15. A file may, for example, report a valid
/// checksum while being unsigned, so the two dimensions are reported
/// separately rather than collapsed into a single value.
/// </remarks>
public sealed class FileVerificationResult
{
    /// <summary>The file name as declared in the catalogue discovery metadata.</summary>
    public required string FileName { get; init; }

    /// <summary>
    /// The digital-signature verification outcome for this file
    /// (S-100 Edition 5.2.1 Part 15 §15-8.9).
    /// </summary>
    public required VerificationOutcome Outcome { get; init; }

    /// <summary>
    /// Results for every digital signature declared for this resource, in
    /// catalogue order. Empty for unsigned resources and compatibility adapters
    /// that expose only an aggregate outcome.
    /// </summary>
    public IReadOnlyList<SignatureVerificationResult> SignatureResults { get; init; } = [];

    /// <summary>
    /// The checksum/integrity outcome for this file, independent of
    /// <see cref="Outcome"/>. Defaults to <see cref="VerificationOutcome.NoChecksum"/>
    /// when the file carries no declared cryptographic hash to compare against.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10.</remarks>
    public VerificationOutcome ChecksumOutcome { get; init; } = VerificationOutcome.NoChecksum;

    /// <summary>
    /// The SHA-256 digest computed over the file content, expressed as a
    /// lower-case hexadecimal string; <see langword="null"/> when the file
    /// was missing or could not be read. Surfaced so an unsigned exchange set
    /// can still be integrity-checked against an externally supplied digest.
    /// </summary>
    public string? ComputedSha256 { get; init; }

    /// <summary>Optional detail message (e.g. exception message on <see cref="VerificationOutcome.Error"/>).</summary>
    public string? Detail { get; init; }
}
