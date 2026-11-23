namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The outcome of verifying a single file or the catalogue as a whole.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15. The same enumeration is used for two
/// independent dimensions on <see cref="FileVerificationResult"/>: the
/// digital-signature dimension (<see cref="FileVerificationResult.Outcome"/>)
/// and the checksum/integrity dimension
/// (<see cref="FileVerificationResult.ChecksumOutcome"/>). Members are
/// append-only so downstream consumers (including the S-57 exchange-set
/// bridge) can mirror the names and ordinals; <see cref="Withdrawn"/> is
/// S-100 only.
/// </remarks>
public enum VerificationOutcome
{
    /// <summary>
    /// The signature was verified and its certificate chains to one of
    /// <see cref="TrustAnchorOptions.TrustedRoots"/>, or (in the checksum
    /// dimension) the computed digest matched the declared digest.
    /// </summary>
    /// <remarks>
    /// A correct signature whose signer couldn't be traced to a trusted root
    /// is <see cref="SignatureValidUntrusted"/>, never <see cref="Ok"/>.
    /// </remarks>
    Ok,

    /// <summary>The file or catalogue carries no digital signature.</summary>
    NotSigned,

    /// <summary>The signature does not match the file content.</summary>
    SignatureInvalid,

    /// <summary>The signing certificate is not trusted by the configured trust anchors.</summary>
    CertificateUntrusted,

    /// <summary>The signing certificate has expired.</summary>
    CertificateExpired,

    /// <summary>The referenced file was not found in the asset source.</summary>
    FileMissing,

    /// <summary>The referenced certificate was not found in the catalogue.</summary>
    CertificateNotFound,

    /// <summary>An unexpected error occurred during verification.</summary>
    Error,

    /// <summary>
    /// Checksum dimension only: the file is present and readable but the
    /// catalogue declares no cryptographic hash (<c>urn:mrn:iho:s100:hash:…</c>,
    /// S-100 Edition 5.2.1 Part 15 §15-8.10) to compare against, so its
    /// content integrity could not be independently verified.
    /// </summary>
    NoChecksum,

    /// <summary>
    /// Checksum dimension only: the computed digest of the file does not
    /// match the cryptographic hash declared in the catalogue.
    /// </summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.10.</remarks>
    ChecksumMismatch,

    /// <summary>
    /// The record withdraws a cancelled dataset
    /// (<see cref="DatasetDiscoveryMetadata.IsWithdrawal"/>), so it is not
    /// verified: nothing loads it, and its file is usually absent. Not a failure.
    /// </summary>
    Withdrawn,

    /// <summary>
    /// Signature dimension only: the signature matches the content and its
    /// certificate is within its validity period, but the certificate doesn't
    /// chain to any of <see cref="TrustAnchorOptions.TrustedRoots"/>, so who
    /// signed it isn't verified. Reported only when
    /// <see cref="TrustAnchorOptions.AllowUntrustedCertificates"/> is set;
    /// otherwise the same signature is <see cref="CertificateUntrusted"/>.
    /// </summary>
    /// <remarks>
    /// The content hasn't changed since the holder of the signing key signed
    /// it, but anyone can make such a key. Not a failure, and not
    /// <see cref="Ok"/>. S-100 Edition 5.2.1 Part 15 §15-8.11.
    /// </remarks>
    SignatureValidUntrusted,

    /// <summary>
    /// Signature dimension only: the verifier can't check this kind of
    /// signature, so whether the file is signed, and by whom, is unknown. The
    /// S-57 bridge reports it for every file, because S-63 signatures aren't
    /// checked yet. Not a failure, and not <see cref="NotSigned"/>.
    /// </summary>
    SignatureNotChecked,
}
