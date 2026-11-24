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

    /// <summary>
    /// Every signature matches its content, but at least one signer doesn't
    /// chain to a trusted Scheme Administrator.
    /// </summary>
    Unverified,

    /// <summary>
    /// The signatures couldn't be checked: S-63 signature checking isn't
    /// supported yet, so the set may be signed or not.
    /// </summary>
    NotChecked,

    /// <summary>
    /// Every signature is valid, but at least one signer chains only to the
    /// IHO testing root: test data, not for navigation.
    /// </summary>
    VerifiedTest,
}
