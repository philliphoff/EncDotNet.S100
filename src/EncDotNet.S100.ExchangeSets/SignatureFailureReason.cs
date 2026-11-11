namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Describes why an individual digital signature could not be verified.
/// </summary>
/// <remarks>
/// Members are append-only so serialized validation results remain stable.
/// </remarks>
public enum SignatureFailureReason
{
    /// <summary>The signature verified successfully.</summary>
    None,

    /// <summary>The signature model has an invalid combination of fields.</summary>
    MalformedSignature,

    /// <summary>The signature identifier is not unique in the catalogue.</summary>
    DuplicateIdentifier,

    /// <summary>The referenced signature identifier does not exist.</summary>
    MissingReference,

    /// <summary>The referenced signature belongs to another resource.</summary>
    CrossResourceReference,

    /// <summary>The signature-reference graph contains a cycle.</summary>
    CyclicReference,

    /// <summary>The declared digital signature algorithm is unsupported.</summary>
    UnsupportedAlgorithm,

    /// <summary>The requested signed resource representation could not be opened.</summary>
    ContentUnavailable,

    /// <summary>The signing certificate was not found.</summary>
    CertificateNotFound,

    /// <summary>The signing certificate is not trusted.</summary>
    CertificateUntrusted,

    /// <summary>The signing certificate has expired.</summary>
    CertificateExpired,

    /// <summary>The signature does not match the signed bytes.</summary>
    SignatureMismatch,

    /// <summary>A cryptographic or certificate parsing error occurred.</summary>
    CryptographicError,
}
