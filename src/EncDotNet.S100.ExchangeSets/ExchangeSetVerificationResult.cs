namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The aggregate result of verifying all files in an exchange set.
/// </summary>
public sealed class ExchangeSetVerificationResult
{
    /// <summary>Per-file verification results.</summary>
    public required IReadOnlyList<FileVerificationResult> FileResults { get; init; }

    /// <summary>
    /// The check of the catalogue against <c>CATALOG.SIGN</c>, or
    /// <see langword="null"/> when it wasn't checked.
    /// </summary>
    /// <remarks>
    /// A missing <c>CATALOG.SIGN</c> (<see cref="VerificationOutcome.NotSigned"/>)
    /// doesn't count as a failure in <see cref="AllValid"/>: many exchange sets
    /// in use don't carry one. Any other outcome but
    /// <see cref="VerificationOutcome.Ok"/> does. S-100 Edition 5.2.1 Part 15
    /// §15-8.11.2.
    /// </remarks>
    public CatalogueSignatureVerificationResult? CatalogueSignature { get; init; }

    /// <summary>
    /// Returns <c>true</c> when every file in the exchange set has
    /// <see cref="VerificationOutcome.Ok"/> as its <em>signature</em> outcome.
    /// </summary>
    /// <remarks>
    /// This is intentionally a strict <em>signature-only</em> predicate: it is
    /// <c>false</c> for an unsigned set (every file <see cref="VerificationOutcome.NotSigned"/>),
    /// and it does not consider the checksum dimension at all. It is paired with
    /// <see cref="IsUnsigned"/> by callers (e.g. the viewer) that distinguish
    /// "signed and all valid" from "unsigned".
    /// <para>
    /// It is deliberately <em>not</em> the overall integrity verdict. Because
    /// S-100 mandates no per-resource checksum (integrity is via Part 15
    /// signatures), a "no checksum present" case must not count as a failure;
    /// that non-failing semantic lives in <see cref="IntegrityVerified"/> (and in
    /// the <c>s100 validate</c> exit code). This matches the sibling S-57
    /// implementation, whose <c>AllValid</c> treats a missing CRC
    /// (<see cref="VerificationOutcome.NoChecksum"/>) as non-failing and fails
    /// only on mismatch, missing file, error, or invalid signature.
    /// </para>
    /// <para>
    /// Withdrawn records (<see cref="VerificationOutcome.Withdrawn"/>) ship no
    /// file and are left out, here and in <see cref="IsUnsigned"/>.
    /// </para>
    /// <para>
    /// A catalogue that fails its <c>CATALOG.SIGN</c> check makes this
    /// <c>false</c> too (<see cref="CatalogueSignatureFailed"/>).
    /// </para>
    /// </remarks>
    public bool AllValid =>
        Delivered.All(r => r.Outcome == VerificationOutcome.Ok) && !CatalogueSignatureFailed;

    /// <summary>
    /// Returns <c>true</c> when at least one file, or the catalogue, has
    /// <see cref="VerificationOutcome.SignatureInvalid"/>.
    /// </summary>
    public bool HasInvalidSignatures =>
        FileResults.Any(r => r.Outcome == VerificationOutcome.SignatureInvalid)
        || CatalogueSignature?.Outcome == VerificationOutcome.SignatureInvalid;

    /// <summary>
    /// Returns <c>true</c> when the exchange set has a <c>CATALOG.SIGN</c> and
    /// the catalogue didn't verify against it (see <see cref="CatalogueSignature"/>).
    /// </summary>
    public bool CatalogueSignatureFailed =>
        CatalogueSignature is { Outcome: not (VerificationOutcome.Ok or VerificationOutcome.NotSigned) };

    /// <summary>
    /// Returns <c>true</c> when no file carries a signature
    /// (all are <see cref="VerificationOutcome.NotSigned"/>), leaving out
    /// withdrawn records.
    /// </summary>
    public bool IsUnsigned => Delivered.All(r => r.Outcome == VerificationOutcome.NotSigned);

    /// <summary>
    /// Returns <c>true</c> when at least one file's computed digest did not
    /// match its declared cryptographic hash
    /// (<see cref="VerificationOutcome.ChecksumMismatch"/>).
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10.</remarks>
    public bool HasChecksumMismatches =>
        FileResults.Any(r => r.ChecksumOutcome == VerificationOutcome.ChecksumMismatch);

    /// <summary>
    /// Returns <c>true</c> when at least one referenced file was missing from
    /// the asset source (an incomplete exchange set), reported in either the
    /// signature or the checksum dimension.
    /// </summary>
    public bool HasMissingFiles =>
        FileResults.Any(r => r.Outcome == VerificationOutcome.FileMissing
            || r.ChecksumOutcome == VerificationOutcome.FileMissing);

    /// <summary>
    /// Returns <c>true</c> when the exchange set is structurally intact: no
    /// referenced file is missing and no declared checksum failed. Files with
    /// no declared checksum (<see cref="VerificationOutcome.NoChecksum"/>) do
    /// not by themselves invalidate integrity, since the specification does not
    /// mandate per-resource hashes.
    /// </summary>
    public bool IntegrityVerified => !HasMissingFiles && !HasChecksumMismatches;

    private IEnumerable<FileVerificationResult> Delivered =>
        FileResults.Where(r => r.Outcome != VerificationOutcome.Withdrawn);
}
