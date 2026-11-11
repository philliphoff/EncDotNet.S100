namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// The result of authenticating and parsing a Part 15 permit.
/// </summary>
public sealed class PermitAuthenticationResult
{
    internal PermitAuthenticationResult(
        PermitSignatureVerificationResult verification,
        PermitFile? permitFile)
    {
        Verification = verification;
        PermitFile = permitFile;
    }

    /// <summary>The standalone-signature verification result.</summary>
    public PermitSignatureVerificationResult Verification { get; }

    /// <summary>
    /// The authenticated permit, or <see langword="null"/> when verification
    /// failed.
    /// </summary>
    public PermitFile? PermitFile { get; }

    /// <summary>Whether an authenticated permit is available.</summary>
    public bool IsAuthenticated => Verification.IsValid && PermitFile is not null;
}
