using System.Security.Cryptography.X509Certificates;

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

    /// <summary>
    /// The certificate from <see cref="TrustAnchorOptions.TrustedRoots"/> that
    /// the signer chained to, when <see cref="Outcome"/> is
    /// <see cref="VerificationOutcome.Ok"/>; otherwise <see langword="null"/>.
    /// It's the caller's own instance, so the caller still owns it.
    /// </summary>
    public X509Certificate2? TrustedRoot { get; init; }
}
