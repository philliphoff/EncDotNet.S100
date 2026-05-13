using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets.Diagnostics;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Verifies the per-file digital signatures in an S-100 exchange set.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15. Signatures are computed over the raw bytes of
/// each referenced file and verified against the certificate identified by
/// <see cref="DigitalSignatureValue.CertificateRef"/>.
/// </remarks>
public sealed class ExchangeSetVerifier : IExchangeSetVerifier
{
    /// <summary>Buffer size used when streaming file content for hashing.</summary>
    private const int StreamBufferSize = 81920;

    /// <inheritdoc />
    public async Task<ExchangeSetVerificationResult> VerifyAsync(
        IAssetSource source,
        ExchangeCatalogue catalogue,
        TrustAnchorOptions trustAnchors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(catalogue);
        ArgumentNullException.ThrowIfNull(trustAnchors);

        using var activity = Telemetry.ActivitySource.StartActivity("s100.exchangeset.verify");

        // Build a lookup of certificateRef → CertificateEntry from the catalogue.
        var certLookup = BuildCertificateLookup(catalogue);

        var results = new List<FileVerificationResult>();

        // Verify datasets
        foreach (var ds in catalogue.DatasetDiscoveryMetadata)
        {
            var result = await VerifyFileAsync(
                source, ds.FileName, ds.DigitalSignatureValue, ds.DigitalSignatureAlgorithm,
                certLookup, trustAnchors, cancellationToken);
            results.Add(result);
        }

        // Verify support files
        foreach (var sf in catalogue.SupportFileDiscoveryMetadata)
        {
            var result = await VerifyFileAsync(
                source, sf.FileName, sf.DigitalSignatureValue, sf.DigitalSignatureAlgorithm,
                certLookup, trustAnchors, cancellationToken);
            results.Add(result);
        }

        // Verify catalogue files
        foreach (var cf in catalogue.CatalogueDiscoveryMetadata)
        {
            var result = await VerifyFileAsync(
                source, cf.FileName, cf.DigitalSignatureValue, cf.DigitalSignatureAlgorithm,
                certLookup, trustAnchors, cancellationToken);
            results.Add(result);
        }

        activity?.SetTag("s100.exchangeset.verify.file_count", results.Count);
        activity?.SetTag("s100.exchangeset.verify.ok_count",
            results.Count(r => r.Outcome == VerificationOutcome.Ok));

        return new ExchangeSetVerificationResult { FileResults = results };
    }

    private static async Task<FileVerificationResult> VerifyFileAsync(
        IAssetSource source,
        string fileName,
        DigitalSignatureValue? signatureValue,
        DigitalSignatureAlgorithm algorithm,
        Dictionary<string, CertificateEntry> certLookup,
        TrustAnchorOptions trustAnchors,
        CancellationToken cancellationToken)
    {
        if (signatureValue is null)
        {
            return new FileVerificationResult
            {
                FileName = fileName,
                Outcome = VerificationOutcome.NotSigned,
            };
        }

        // Resolve the certificate
        if (!certLookup.TryGetValue(signatureValue.CertificateRef, out var certEntry))
        {
            return new FileVerificationResult
            {
                FileName = fileName,
                Outcome = VerificationOutcome.CertificateNotFound,
                Detail = $"Certificate '{signatureValue.CertificateRef}' not found in catalogue.",
            };
        }

        X509Certificate2 cert;
        try
        {
#if NET10_0_OR_GREATER
            cert = X509CertificateLoader.LoadCertificate(certEntry.Value);
#else
            cert = new X509Certificate2(certEntry.Value);
#endif
        }
        catch (CryptographicException ex)
        {
            return new FileVerificationResult
            {
                FileName = fileName,
                Outcome = VerificationOutcome.Error,
                Detail = $"Failed to parse certificate '{signatureValue.CertificateRef}': {ex.Message}",
            };
        }

        using (cert)
        {
            // Check certificate trust
            var trustOutcome = ValidateCertificateTrust(cert, trustAnchors);
            if (trustOutcome is not null)
            {
                return new FileVerificationResult
                {
                    FileName = fileName,
                    Outcome = trustOutcome.Value,
                    Detail = trustOutcome.Value == VerificationOutcome.CertificateExpired
                        ? $"Certificate '{signatureValue.CertificateRef}' expired on {cert.NotAfter:O}."
                        : $"Certificate '{signatureValue.CertificateRef}' is not trusted.",
                };
            }

            // Hash the file content
            byte[] fileHash;
            try
            {
                var normalizedPath = ExchangeSet.NormalizeFileName(fileName);
                await using var stream = await source.OpenAsync(normalizedPath, cancellationToken);
                fileHash = await ComputeSha256HashAsync(stream, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                return new FileVerificationResult
                {
                    FileName = fileName,
                    Outcome = VerificationOutcome.FileMissing,
                };
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return new FileVerificationResult
                {
                    FileName = fileName,
                    Outcome = VerificationOutcome.Error,
                    Detail = $"Failed to read file: {ex.Message}",
                };
            }

            // Verify the signature
            bool valid;
            try
            {
                valid = VerifySignature(cert, algorithm, fileHash, signatureValue.Value);
            }
            catch (CryptographicException ex)
            {
                return new FileVerificationResult
                {
                    FileName = fileName,
                    Outcome = VerificationOutcome.Error,
                    Detail = $"Signature verification error: {ex.Message}",
                };
            }

            return new FileVerificationResult
            {
                FileName = fileName,
                Outcome = valid ? VerificationOutcome.Ok : VerificationOutcome.SignatureInvalid,
            };
        }
    }

    /// <summary>
    /// Validates the certificate against the configured trust anchors.
    /// Returns <c>null</c> if the certificate is trusted (or trust validation is skipped),
    /// otherwise returns the appropriate <see cref="VerificationOutcome"/>.
    /// </summary>
    private static VerificationOutcome? ValidateCertificateTrust(
        X509Certificate2 cert,
        TrustAnchorOptions trustAnchors)
    {
        // Check expiry
        var now = DateTimeOffset.UtcNow;
        if (now < cert.NotBefore || now > cert.NotAfter)
        {
            return VerificationOutcome.CertificateExpired;
        }

        // If caller allows untrusted certs, skip chain validation
        if (trustAnchors.AllowUntrustedCertificates)
        {
            return null;
        }

        // If no trusted roots configured, we cannot validate
        if (trustAnchors.TrustedRoots.Count == 0)
        {
            return VerificationOutcome.CertificateUntrusted;
        }

        // Check if the certificate was issued by one of the trusted roots.
        // S-100 Part 15 uses a simple two-level hierarchy:
        //   SA root → Data Server certificate.
        // We check if the cert's issuer matches any trusted root's subject.
        foreach (var root in trustAnchors.TrustedRoots)
        {
            if (cert.Issuer == root.Subject ||
                cert.Thumbprint == root.Thumbprint)
            {
                return null;
            }
        }

        return VerificationOutcome.CertificateUntrusted;
    }

    /// <summary>
    /// Verifies a signature over a SHA-256 file hash using the certificate's public key.
    /// Supports both DSA and ECDSA algorithms.
    /// </summary>
    private static bool VerifySignature(
        X509Certificate2 cert,
        DigitalSignatureAlgorithm algorithm,
        byte[] hash,
        byte[] signature)
    {
        switch (algorithm)
        {
            case DigitalSignatureAlgorithm.ECDSA:
            {
                using var ecdsa = cert.GetECDsaPublicKey();
                if (ecdsa is null)
                    throw new CryptographicException("Certificate does not contain an ECDSA public key.");
                return ecdsa.VerifyHash(hash, signature);
            }

            case DigitalSignatureAlgorithm.DSA:
            {
                using var dsa = cert.GetDSAPublicKey();
                if (dsa is null)
                    throw new CryptographicException("Certificate does not contain a DSA public key.");
                return dsa.VerifySignature(hash, signature);
            }

            default:
                throw new CryptographicException(
                    $"Unsupported digital signature algorithm: {algorithm}");
        }
    }

    /// <summary>
    /// Computes a SHA-256 hash of the stream content, reading in chunks to
    /// avoid loading the entire file into memory.
    /// </summary>
    private static async Task<byte[]> ComputeSha256HashAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[StreamBufferSize];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            sha256.AppendData(buffer, 0, bytesRead);
        }

        return sha256.GetHashAndReset();
    }

    /// <summary>
    /// Builds a lookup from certificate <c>id</c> to <see cref="CertificateEntry"/>.
    /// </summary>
    private static Dictionary<string, CertificateEntry> BuildCertificateLookup(ExchangeCatalogue catalogue)
    {
        var lookup = new Dictionary<string, CertificateEntry>(StringComparer.Ordinal);

        if (catalogue.Certificates is { } block)
        {
            foreach (var cert in block.Certificates)
            {
                lookup[cert.Id] = cert;
            }
        }

        return lookup;
    }
}
