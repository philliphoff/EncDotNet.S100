using System.Security.Cryptography;
using System.Xml;
using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Checks an exchange catalogue against the <c>CATALOG.SIGN</c> file beside it.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15 §15-8.11.2. <c>CATALOG.SIGN</c> is a standalone
/// signature that names the file it signs; the signature is ECDSA P-384 over
/// the SHA-384 of that file's exact bytes (§15-8.7), checked against the
/// certificates the signature file carries.
/// </remarks>
public static class CatalogueSignatureVerifier
{
    /// <summary>The standalone catalogue signature file name.</summary>
    public const string SignatureFileName = "CATALOG.SIGN";

    /// <summary>
    /// Checks the catalogue in <paramref name="source"/> against its
    /// <c>CATALOG.SIGN</c>. Never throws for a missing, malformed or invalid
    /// signature; the result says what was found.
    /// </summary>
    /// <param name="source">The exchange set, rooted at the folder that holds the catalogue.</param>
    /// <param name="trustAnchors">The Scheme Administrator trust configuration.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The outcome of the check.</returns>
    public static async Task<CatalogueSignatureVerificationResult> VerifyAsync(
        IAssetSource source,
        TrustAnchorOptions trustAnchors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(trustAnchors);

        StandaloneDigitalSignature standalone;
        try
        {
            await using var signatureStream = await source.OpenAsync(SignatureFileName, cancellationToken)
                .ConfigureAwait(false);
            standalone = StandaloneDigitalSignatureReader.Read(signatureStream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(VerificationOutcome.NotSigned, $"The exchange set has no {SignatureFileName}.");
        }
        catch (Exception ex) when (ex is XmlException or FormatException)
        {
            return new(VerificationOutcome.Error, $"{SignatureFileName} is malformed: {ex.Message}");
        }
        catch (IOException ex)
        {
            return new(VerificationOutcome.Error, $"{SignatureFileName} couldn't be read: {ex.Message}");
        }

        var signedFileName = Path.GetFileName(standalone.FileName);
        ReadOnlyMemory<byte> catalogueBytes;
        try
        {
            catalogueBytes = (await source.ReadAllBytesAsync(signedFileName, cancellationToken)
                .ConfigureAwait(false)).Bytes;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new(
                VerificationOutcome.FileMissing,
                $"{SignatureFileName} signs '{signedFileName}', which isn't in the exchange set.",
                standalone);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return new(
                VerificationOutcome.Error,
                $"'{signedFileName}' couldn't be read: {ex.Message}",
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
            return new(
                VerificationOutcome.Error,
                $"{SignatureFileName} contains duplicate certificate identifiers.",
                standalone);
        }

        var (outcome, detail) = ExchangeSetVerifier.EvaluateSignature(
            standalone.Signature,
            DigitalSignatureAlgorithm.ECDSA,
            SHA384.HashData(catalogueBytes.Span),
            certificates,
            trustAnchors,
            standalone.Certificates.Certificates,
            DSASignatureFormat.Rfc3279DerSequence,
            requireP384: true,
            schemeAdministratorId: standalone.Certificates.SchemeAdministratorId,
            schemeAdministratorCertificates: trustAnchors.TrustedRoots);
        if (outcome == VerificationOutcome.SignatureInvalid)
        {
            detail ??= $"'{signedFileName}' doesn't match {SignatureFileName}.";
        }

        return new(outcome, detail, standalone);
    }
}
