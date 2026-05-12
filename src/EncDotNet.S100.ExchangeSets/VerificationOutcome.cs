namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// The outcome of verifying a single file or the catalogue as a whole.
/// </summary>
/// <remarks>S-100 Edition 5.2.1 Part 15.</remarks>
public enum VerificationOutcome
{
    /// <summary>The signature was verified successfully.</summary>
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
}
