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
/// <see cref="DigitalSignatureValue.CertificateRef"/>. Each file is also
/// integrity-checked: its SHA-256 digest is computed and, when the catalogue
/// declares a cryptographic hash (Part 15 §15-8.10), compared against it. This
/// integrity dimension is reported independently of the signature dimension
/// (see <see cref="FileVerificationResult.ChecksumOutcome"/>), so even an
/// unsigned exchange set can be checked for missing or corrupt files.
/// </remarks>
public class ExchangeSetVerifier : IExchangeSetVerifier
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
                source, ds.RelativePath, ds.DigitalSignatureValue, ds.DigitalSignatureAlgorithm,
                ds.ExpectedHash, certLookup, trustAnchors, cancellationToken);
            results.Add(result);
        }

        // Verify support files
        foreach (var sf in catalogue.SupportFileDiscoveryMetadata)
        {
            var result = await VerifyFileAsync(
                source, sf.RelativePath, sf.DigitalSignatureValue, sf.DigitalSignatureAlgorithm,
                sf.ExpectedHash, certLookup, trustAnchors, cancellationToken);
            results.Add(result);
        }

        // Verify catalogue files
        foreach (var cf in catalogue.CatalogueDiscoveryMetadata)
        {
            var result = await VerifyFileAsync(
                source, cf.RelativePath, cf.DigitalSignatureValue, cf.DigitalSignatureAlgorithm,
                cf.ExpectedHash, certLookup, trustAnchors, cancellationToken);
            results.Add(result);
        }

        activity?.SetTag("s100.exchangeset.verify.file_count", results.Count);
        activity?.SetTag("s100.exchangeset.verify.ok_count",
            results.Count(r => r.Outcome == VerificationOutcome.Ok));

        return new ExchangeSetVerificationResult { FileResults = results };
    }

    private async Task<FileVerificationResult> VerifyFileAsync(
        IAssetSource source,
        string fileName,
        DigitalSignatureValue? signatureValue,
        DigitalSignatureAlgorithm algorithm,
        CryptographicHash? expectedHash,
        Dictionary<string, CertificateEntry> certLookup,
        TrustAnchorOptions trustAnchors,
        CancellationToken cancellationToken)
    {
        // Hash the file content once, regardless of whether it is signed, so
        // that an unsigned exchange set can still be integrity-checked. The
        // same SHA-256 digest feeds both the checksum dimension and (when a
        // signature is present) the signature verification below.
        byte[] fileHash;
        try
        {
            var normalizedPath = ExchangeSet.NormalizeFileName(fileName);
            await using var stream = await OpenContentForHashingAsync(
                source, normalizedPath, signatureValue, cancellationToken);
            fileHash = await ComputeSha256HashAsync(stream, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return new FileVerificationResult
            {
                FileName = fileName,
                Outcome = VerificationOutcome.FileMissing,
                ChecksumOutcome = VerificationOutcome.FileMissing,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FileVerificationResult
            {
                FileName = fileName,
                Outcome = VerificationOutcome.Error,
                ChecksumOutcome = VerificationOutcome.Error,
                Detail = $"Failed to read file: {ex.Message}",
            };
        }

        var computedHex = Convert.ToHexString(fileHash).ToLowerInvariant();

        // Checksum dimension: compare against the declared cryptographic hash
        // when one is present (S-100 Edition 5.2.1 Part 15 §15-8.10), otherwise
        // report that there is nothing to validate against.
        var checksumOutcome = expectedHash is null
            ? VerificationOutcome.NoChecksum
            : expectedHash.Matches(computedHex)
                ? VerificationOutcome.Ok
                : VerificationOutcome.ChecksumMismatch;

        // Signature dimension: independent of the checksum result.
        var (signatureOutcome, detail) = EvaluateSignature(
            signatureValue, algorithm, fileHash, certLookup, trustAnchors);

        return new FileVerificationResult
        {
            FileName = fileName,
            Outcome = signatureOutcome,
            ChecksumOutcome = checksumOutcome,
            ComputedSha256 = computedHex,
            Detail = detail,
        };
    }

    /// <summary>
    /// Opens the file content that should be hashed for verification.
    /// </summary>
    /// <remarks>
    /// The default implementation returns the raw bytes from the asset source.
    /// This is the seam where Part 15 decryption would slot in: a future
    /// override (or injected decrypted-content provider) could return the
    /// decrypted, decompressed bytes for a <c>dataStatus="encrypted"</c>
    /// signature (S-100 Edition 5.2.1 Part 15 §15-8.8, Table 15-10) so that the
    /// computed digest matches the signature, which is produced over the
    /// unencrypted resource. The <paramref name="signatureValue"/> is supplied
    /// so an override can decide whether decryption is required.
    /// </remarks>
    /// <param name="source">The asset source containing the file.</param>
    /// <param name="normalizedPath">The normalized, source-relative file path.</param>
    /// <param name="signatureValue">The file's signature value, if any.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A readable stream over the bytes to hash.</returns>
    protected virtual Task<Stream> OpenContentForHashingAsync(
        IAssetSource source,
        string normalizedPath,
        DigitalSignatureValue? signatureValue,
        CancellationToken cancellationToken)
    {
        return source.OpenAsync(normalizedPath, cancellationToken);
    }

    /// <summary>
    /// Evaluates the digital-signature dimension for a file whose content has
    /// already been hashed. Returns <see cref="VerificationOutcome.NotSigned"/>
    /// when the file carries no signature.
    /// </summary>
    private static (VerificationOutcome Outcome, string? Detail) EvaluateSignature(
        DigitalSignatureValue? signatureValue,
        DigitalSignatureAlgorithm algorithm,
        byte[] fileHash,
        Dictionary<string, CertificateEntry> certLookup,
        TrustAnchorOptions trustAnchors)
    {
        if (signatureValue is null)
        {
            return (VerificationOutcome.NotSigned, null);
        }

        // Resolve the certificate
        if (!certLookup.TryGetValue(signatureValue.CertificateRef, out var certEntry))
        {
            return (VerificationOutcome.CertificateNotFound,
                $"Certificate '{signatureValue.CertificateRef}' not found in catalogue.");
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
            return (VerificationOutcome.Error,
                $"Failed to parse certificate '{signatureValue.CertificateRef}': {ex.Message}");
        }

        using (cert)
        {
            // Check certificate trust
            var trustOutcome = ValidateCertificateTrust(cert, trustAnchors);
            if (trustOutcome is not null)
            {
                var detail = trustOutcome.Value == VerificationOutcome.CertificateExpired
                    ? $"Certificate '{signatureValue.CertificateRef}' expired on {cert.NotAfter:O}."
                    : $"Certificate '{signatureValue.CertificateRef}' is not trusted.";
                return (trustOutcome.Value, detail);
            }

            // Verify the signature
            try
            {
                var valid = VerifySignature(cert, algorithm, fileHash, signatureValue.Value);
                return (valid ? VerificationOutcome.Ok : VerificationOutcome.SignatureInvalid, null);
            }
            catch (CryptographicException ex)
            {
                return (VerificationOutcome.Error, $"Signature verification error: {ex.Message}");
            }
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
