using System.Security.Cryptography;
using System.Xml;

namespace EncDotNet.S100.ExchangeSets.Protection;

/// <summary>
/// Authenticates <c>PERMIT.XML</c> content with its Part 15
/// <c>PERMIT.SIGN</c> file.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-7.4.5 and §15-8.11.2. The signature is
/// ECDSA P-384 over SHA-384 as required by §15-8.7.
/// </remarks>
public static class PermitSignatureVerifier
{
    /// <summary>
    /// Verifies and parses a permit without exposing it when authentication
    /// fails.
    /// </summary>
    /// <param name="permitContent">The exact bytes of the <c>PERMIT.XML</c> file.</param>
    /// <param name="signatureContent">
    /// The <c>PERMIT.SIGN</c> XML stream, or <see langword="null"/> when absent.
    /// </param>
    /// <param name="permitFileName">The actual permit file name.</param>
    /// <param name="trustAnchors">The Scheme Administrator trust configuration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>An authentication result containing a permit only on success.</returns>
    public static async Task<PermitAuthenticationResult> AuthenticateAsync(
        Stream permitContent,
        Stream? signatureContent,
        string permitFileName,
        TrustAnchorOptions trustAnchors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permitContent);
        ArgumentException.ThrowIfNullOrWhiteSpace(permitFileName);
        ArgumentNullException.ThrowIfNull(trustAnchors);

        if (trustAnchors.AllowUntrustedCertificates)
        {
            return Failure(
                VerificationOutcome.CertificateUntrusted,
                "Permit authentication requires a trusted Scheme Administrator certificate.");
        }

        if (signatureContent is null)
        {
            return Failure(
                VerificationOutcome.NotSigned,
                $"The permit '{permitFileName}' has no accompanying signature.");
        }

        byte[] permitBytes;
        try
        {
            using var buffer = new MemoryStream();
            await permitContent.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            permitBytes = buffer.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Failure(VerificationOutcome.Error, $"Failed to read permit content: {ex.Message}");
        }

        StandaloneDigitalSignature standalone;
        try
        {
            standalone = StandaloneDigitalSignatureReader.Read(signatureContent);
        }
        catch (Exception ex) when (ex is XmlException or FormatException)
        {
            return Failure(
                VerificationOutcome.Error,
                $"The permit signature file is malformed: {ex.Message}");
        }

        if (!string.Equals(
                Path.GetFileName(standalone.FileName),
                Path.GetFileName(permitFileName),
                StringComparison.OrdinalIgnoreCase))
        {
            return Failure(
                VerificationOutcome.Error,
                $"The signature is for '{standalone.FileName}', not '{permitFileName}'.",
                standalone);
        }

        Dictionary<string, CertificateEntry> certificates;
        try
        {
            certificates = standalone.Certificates.Certificates
                .ToDictionary(certificate => certificate.Id, StringComparer.Ordinal);
        }
        catch (ArgumentException)
        {
            return Failure(
                VerificationOutcome.Error,
                "The permit signature file contains duplicate certificate identifiers.",
                standalone);
        }

        var hash = SHA384.HashData(permitBytes);
        var (outcome, detail) = ExchangeSetVerifier.EvaluateSignature(
            standalone.Signature,
            DigitalSignatureAlgorithm.ECDSA,
            hash,
            certificates,
            trustAnchors,
            standalone.Certificates.Certificates,
            DSASignatureFormat.Rfc3279DerSequence,
            requireP384: true);
        var verification = new PermitSignatureVerificationResult(outcome, detail, standalone);
        if (!verification.IsValid)
        {
            return new PermitAuthenticationResult(verification, null);
        }

        try
        {
            using var permitStream = new MemoryStream(permitBytes, writable: false);
            var permit = PermitFile.ReadAuthenticated(permitStream);
            return new PermitAuthenticationResult(verification, permit);
        }
        catch (Exception ex) when (ex is XmlException or FormatException)
        {
            return Failure(
                VerificationOutcome.Error,
                $"The authenticated permit file is malformed: {ex.Message}",
                standalone);
        }
    }

    private static PermitAuthenticationResult Failure(
        VerificationOutcome outcome,
        string detail,
        StandaloneDigitalSignature? signature = null) =>
        new(new PermitSignatureVerificationResult(outcome, detail, signature), null);
}
