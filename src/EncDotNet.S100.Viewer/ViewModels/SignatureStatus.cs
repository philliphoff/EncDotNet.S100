namespace EncDotNet.S100.Viewer.ViewModels;

/// <summary>
/// Aggregate signature verification status for an exchange set,
/// surfaced as a non-blocking badge in the Datasets panel.
/// </summary>
internal enum SignatureStatus
{
    /// <summary>Verification has not yet been attempted.</summary>
    Unknown,

    /// <summary>Verification is in progress.</summary>
    Checking,

    /// <summary>All files verified successfully.</summary>
    Verified,

    /// <summary>The exchange set carries no digital signatures.</summary>
    Unsigned,

    /// <summary>At least one file's signature is invalid.</summary>
    Invalid,

    /// <summary>At least one certificate is not trusted.</summary>
    Untrusted,

    /// <summary>Mixed results across files (some ok, some not).</summary>
    Mixed,

    /// <summary>An error occurred during verification.</summary>
    Error,
}
