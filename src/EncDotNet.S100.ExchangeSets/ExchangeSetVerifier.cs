using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Core;
using EncDotNet.S100.ExchangeSets.Diagnostics;
using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.ExchangeSets;

/// <summary>
/// Verifies the per-file digital signatures in an S-100 exchange set.
/// </summary>
/// <remarks>
/// S-100 Edition 5.2.1 Part 15. Signatures are computed over the representation
/// selected by their data status, or over another signature's ASN.1 bytes, and
/// verified against the certificate identified by
/// <see cref="DigitalSignatureValue.CertificateRef"/>. File-based Part 15
/// signatures use ECDSA P-384 with SHA-384 (§15-8.4); legacy algorithms retain
/// their existing compatibility behavior. Each file is also independently
/// integrity-checked with SHA-256 when the catalogue declares a cryptographic
/// hash (§15-8.10).
/// </remarks>
public class ExchangeSetVerifier : IExchangeSetVerifier
{
    /// <summary>Buffer size used when streaming file content for hashing.</summary>
    private const int StreamBufferSize = 81920;

    /// <summary>The secp384r1 / NIST P-384 named-curve object identifier.</summary>
    /// <remarks>S-100 Edition 5.2.1 Part 15 §15-8.4.</remarks>
    private const string NistP384Oid = "1.3.132.0.34";

    private readonly ISignatureContentResolver _contentResolver;

    /// <summary>
    /// Creates a verifier that can resolve unprotected and stored encrypted
    /// signature content.
    /// </summary>
    public ExchangeSetVerifier()
        : this(new Part15SignatureContentResolver())
    {
    }

    /// <summary>
    /// Creates a verifier with a custom signed-content resolver.
    /// </summary>
    /// <param name="contentResolver">
    /// The resolver used to open the representation selected by a signature's
    /// <c>dataStatus</c>.
    /// </param>
    public ExchangeSetVerifier(ISignatureContentResolver contentResolver)
    {
        _contentResolver = contentResolver ?? throw new ArgumentNullException(nameof(contentResolver));
    }

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
        var certificateChain = catalogue.Certificates?.Certificates ?? [];
        var schemeAdministratorId = catalogue.Certificates?.SchemeAdministratorId;

        var resources = CreateSignatureResources(catalogue);
        var signatureIndex = BuildSignatureIndex(resources);
        var duplicateIds = signatureIndex
            .Where(entry => entry.Value.Count > 1)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var uniqueOwners = signatureIndex
            .Where(entry => entry.Value.Count == 1)
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value[0].ResourceId,
                StringComparer.Ordinal);

        var results = new List<FileVerificationResult>(resources.Count);
        foreach (var resource in resources)
        {
            var result = await VerifyFileAsync(
                source,
                resource,
                duplicateIds,
                uniqueOwners,
                certLookup,
                certificateChain,
                schemeAdministratorId,
                trustAnchors,
                cancellationToken).ConfigureAwait(false);
            results.Add(result);
        }

        activity?.SetTag("s100.exchangeset.verify.file_count", results.Count);
        activity?.SetTag("s100.exchangeset.verify.ok_count",
            results.Count(r => r.Outcome == VerificationOutcome.Ok));

        return new ExchangeSetVerificationResult { FileResults = results };
    }

    private async Task<FileVerificationResult> VerifyFileAsync(
        IAssetSource source,
        SignatureResource resource,
        IReadOnlySet<string> duplicateIds,
        IReadOnlyDictionary<string, int> uniqueOwners,
        Dictionary<string, CertificateEntry> certLookup,
        IReadOnlyCollection<CertificateEntry> certificateChain,
        string? schemeAdministratorId,
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
            var normalizedPath = ExchangeSet.NormalizeFileName(resource.RelativePath);
            await using var stream = await OpenContentForHashingAsync(
                source, normalizedPath, resource.Signatures.FirstOrDefault(), cancellationToken);
            fileHash = await ComputeSha256HashAsync(stream, cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return new FileVerificationResult
            {
                FileName = resource.RelativePath,
                Outcome = VerificationOutcome.FileMissing,
                SignatureResults = UnavailableSignatureResults(
                    resource.Signatures,
                    VerificationOutcome.FileMissing,
                    "The signed resource was not found."),
                ChecksumOutcome = VerificationOutcome.FileMissing,
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new FileVerificationResult
            {
                FileName = resource.RelativePath,
                Outcome = VerificationOutcome.Error,
                SignatureResults = UnavailableSignatureResults(
                    resource.Signatures,
                    VerificationOutcome.Error,
                    $"The signed resource could not be read: {ex.Message}"),
                ChecksumOutcome = VerificationOutcome.Error,
                Detail = $"Failed to read file: {ex.Message}",
            };
        }

        return await CompleteFileVerificationAsync(
            source,
            resource,
            duplicateIds,
            uniqueOwners,
            fileHash,
            certLookup,
            certificateChain,
            schemeAdministratorId,
            trustAnchors,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SignatureVerificationResult>> VerifySignaturesAsync(
        IAssetSource source,
        SignatureResource resource,
        IReadOnlySet<string> duplicateIds,
        IReadOnlyDictionary<string, int> uniqueOwners,
        byte[] legacyFileHash,
        Dictionary<string, CertificateEntry> certLookup,
        IReadOnlyCollection<CertificateEntry> certificateChain,
        string? schemeAdministratorId,
        TrustAnchorOptions trustAnchors,
        CancellationToken cancellationToken)
    {
        if (resource.Signatures.Count == 0)
        {
            return [];
        }

        var structuralFailures = FindStructuralFailures(resource, duplicateIds, uniqueOwners);
        var localSignatures = resource.Signatures
            .Where(signature => !duplicateIds.Contains(signature.Id))
            .ToDictionary(signature => signature.Id, StringComparer.Ordinal);
        var dataHashes = new Dictionary<SignatureDataStatus, byte[]>();
        byte[]? legacyPart15Hash = null;
        var results = new List<SignatureVerificationResult>(resource.Signatures.Count);

        foreach (var signature in resource.Signatures)
        {
            if (structuralFailures.TryGetValue(signature, out var failure))
            {
                results.Add(FailureResult(signature, failure.Reason, failure.Detail));
                continue;
            }

            if (resource.Algorithm == DigitalSignatureAlgorithm.Unknown ||
                (signature.Kind != DigitalSignatureKind.Legacy &&
                    resource.Algorithm != DigitalSignatureAlgorithm.ECDSA384SHA2))
            {
                results.Add(FailureResult(
                    signature,
                    SignatureFailureReason.UnsupportedAlgorithm,
                    signature.Kind == DigitalSignatureKind.Legacy
                        ? "The resource declares an unsupported digital signature algorithm."
                        : "Part 15 signature-on-data and signature-on-signature forms require ECDSA-384-SHA2."));
                continue;
            }

            byte[] signedHash;
            try
            {
                if (signature.Kind == DigitalSignatureKind.SignatureOnSignature)
                {
                    var signatureRef = signature.SignatureRef
                        ?? throw new InvalidOperationException(
                            $"Signature '{signature.Id}' has no signature reference.");
                    var referenced = localSignatures[signatureRef];
                    signedHash = ComputeSignatureHash(resource.Algorithm, referenced.Value);
                }
                else if (signature.Kind == DigitalSignatureKind.SignatureOnData)
                {
                    var dataStatus = signature.DataStatus
                        ?? throw new InvalidOperationException(
                            $"Signature '{signature.Id}' has no data status.");
                    if (!dataHashes.TryGetValue(dataStatus, out var cachedHash))
                    {
                        var request = new SignatureContentRequest
                        {
                            RelativePath = resource.RelativePath,
                            DataStatus = dataStatus,
                            DataProtection = resource.DataProtection,
                            CompressionFlag = resource.CompressionFlag,
                        };
                        await using var content = await _contentResolver.OpenAsync(
                            source,
                            request,
                            cancellationToken).ConfigureAwait(false);
                        signedHash = await ComputeSignatureHashAsync(
                            resource.Algorithm,
                            content,
                            cancellationToken)
                            .ConfigureAwait(false);
                        dataHashes.Add(dataStatus, signedHash);
                    }
                    else
                    {
                        signedHash = cachedHash;
                    }
                }
                else
                {
                    if (resource.Algorithm == DigitalSignatureAlgorithm.ECDSA384SHA2)
                    {
                        if (legacyPart15Hash is null)
                        {
                            var normalizedPath = ExchangeSet.NormalizeFileName(resource.RelativePath);
                            await using var content = await OpenContentForHashingAsync(
                                source,
                                normalizedPath,
                                signature,
                                cancellationToken).ConfigureAwait(false);
                            legacyPart15Hash = await ComputeSignatureHashAsync(
                                resource.Algorithm,
                                content,
                                cancellationToken).ConfigureAwait(false);
                        }

                        signedHash = legacyPart15Hash;
                    }
                    else
                    {
                        signedHash = legacyFileHash;
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(FailureResult(
                    signature,
                    SignatureFailureReason.ContentUnavailable,
                    $"Could not resolve signed content: {ex.Message}"));
                continue;
            }

            var signatureFormat = signature.Kind == DigitalSignatureKind.Legacy &&
                resource.Algorithm != DigitalSignatureAlgorithm.ECDSA384SHA2
                ? DSASignatureFormat.IeeeP1363FixedFieldConcatenation
                : DSASignatureFormat.Rfc3279DerSequence;
            var (outcome, detail) = EvaluateSignature(
                signature,
                resource.Algorithm,
                signedHash,
                certLookup,
                trustAnchors,
                certificateChain,
                signatureFormat: signatureFormat,
                requireP384: resource.Algorithm == DigitalSignatureAlgorithm.ECDSA384SHA2,
                schemeAdministratorId: schemeAdministratorId,
                schemeAdministratorCertificates: trustAnchors.TrustedRoots);
            results.Add(new SignatureVerificationResult
            {
                Id = signature.Id,
                Kind = signature.Kind,
                Outcome = outcome,
                FailureReason = MapFailureReason(outcome),
                Detail = detail,
            });
        }

        return results;
    }

    private static Dictionary<DigitalSignatureValue, StructuralFailure> FindStructuralFailures(
        SignatureResource resource,
        IReadOnlySet<string> duplicateIds,
        IReadOnlyDictionary<string, int> uniqueOwners)
    {
        var failures = new Dictionary<DigitalSignatureValue, StructuralFailure>();
        foreach (var signature in resource.Signatures)
        {
            var malformedDetail = ValidateSignatureShape(signature);
            if (malformedDetail is not null)
            {
                failures[signature] = new StructuralFailure(
                    SignatureFailureReason.MalformedSignature,
                    malformedDetail);
                continue;
            }

            if (duplicateIds.Contains(signature.Id))
            {
                failures[signature] = new StructuralFailure(
                    SignatureFailureReason.DuplicateIdentifier,
                    $"Signature identifier '{signature.Id}' is duplicated in the catalogue.");
                continue;
            }

            if (signature.Kind != DigitalSignatureKind.SignatureOnSignature)
            {
                continue;
            }

            if (signature.SignatureRef is not { } signatureRef)
            {
                continue;
            }
            if (!uniqueOwners.TryGetValue(signatureRef, out var owner))
            {
                failures[signature] = new StructuralFailure(
                    duplicateIds.Contains(signatureRef)
                        ? SignatureFailureReason.DuplicateIdentifier
                        : SignatureFailureReason.MissingReference,
                    duplicateIds.Contains(signatureRef)
                        ? $"Signature reference '{signatureRef}' is ambiguous because the identifier is duplicated."
                        : $"Referenced signature '{signatureRef}' was not found.");
            }
            else if (owner != resource.ResourceId)
            {
                failures[signature] = new StructuralFailure(
                    SignatureFailureReason.CrossResourceReference,
                    $"Referenced signature '{signatureRef}' belongs to another resource.");
            }
        }

        FindCycles(resource, duplicateIds, failures);
        return failures;
    }

    private static void FindCycles(
        SignatureResource resource,
        IReadOnlySet<string> duplicateIds,
        Dictionary<DigitalSignatureValue, StructuralFailure> failures)
    {
        var signatures = resource.Signatures
            .Where(signature => !duplicateIds.Contains(signature.Id))
            .ToDictionary(signature => signature.Id, StringComparer.Ordinal);
        var states = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new List<string>();

        foreach (var signature in signatures.Values)
        {
            Visit(signature);
        }

        void Visit(DigitalSignatureValue signature)
        {
            if (states.TryGetValue(signature.Id, out var state))
            {
                if (state == 1)
                {
                    var cycleStart = stack.IndexOf(signature.Id);
                    foreach (var id in stack.Skip(cycleStart))
                    {
                        var cycleSignature = signatures[id];
                        failures[cycleSignature] = new StructuralFailure(
                            SignatureFailureReason.CyclicReference,
                            $"Signature reference cycle includes '{id}'.");
                    }
                }

                return;
            }

            states[signature.Id] = 1;
            stack.Add(signature.Id);
            if (signature.Kind == DigitalSignatureKind.SignatureOnSignature &&
                signature.SignatureRef is { } signatureRef &&
                signatures.TryGetValue(signatureRef, out var referenced))
            {
                Visit(referenced);
            }

            stack.RemoveAt(stack.Count - 1);
            states[signature.Id] = 2;
        }
    }

    private static string? ValidateSignatureShape(DigitalSignatureValue signature)
    {
        if (string.IsNullOrWhiteSpace(signature.Id) ||
            string.IsNullOrWhiteSpace(signature.CertificateRef) ||
            signature.Value.Length == 0)
        {
            return "The signature identifier, certificate reference, and value are required.";
        }

        return signature.Kind switch
        {
            DigitalSignatureKind.Legacy when signature.DataStatus is not null ||
                signature.SignatureRef is not null =>
                "A legacy signature cannot declare dataStatus or signatureRef.",
            DigitalSignatureKind.SignatureOnData when signature.DataStatus is null ||
                signature.SignatureRef is not null =>
                "A signature on data requires dataStatus and cannot declare signatureRef.",
            DigitalSignatureKind.SignatureOnSignature when
                string.IsNullOrWhiteSpace(signature.SignatureRef) ||
                signature.DataStatus is not null =>
                "A signature on signature requires signatureRef and cannot declare dataStatus.",
            _ => null,
        };
    }

    private static SignatureVerificationResult FailureResult(
        DigitalSignatureValue signature,
        SignatureFailureReason reason,
        string detail) =>
        new()
        {
            Id = signature.Id,
            Kind = signature.Kind,
            Outcome = VerificationOutcome.Error,
            FailureReason = reason,
            Detail = detail,
        };

    private static IReadOnlyList<SignatureVerificationResult> UnavailableSignatureResults(
        IReadOnlyList<DigitalSignatureValue> signatures,
        VerificationOutcome outcome,
        string detail) =>
        signatures
            .Select(signature => new SignatureVerificationResult
            {
                Id = signature.Id,
                Kind = signature.Kind,
                Outcome = outcome,
                FailureReason = SignatureFailureReason.ContentUnavailable,
                Detail = detail,
            })
            .ToList();

    private static SignatureFailureReason MapFailureReason(VerificationOutcome outcome) =>
        outcome switch
        {
            VerificationOutcome.Ok => SignatureFailureReason.None,
            VerificationOutcome.SignatureInvalid => SignatureFailureReason.SignatureMismatch,
            VerificationOutcome.CertificateNotFound => SignatureFailureReason.CertificateNotFound,
            VerificationOutcome.CertificateUntrusted => SignatureFailureReason.CertificateUntrusted,
            VerificationOutcome.CertificateExpired => SignatureFailureReason.CertificateExpired,
            _ => SignatureFailureReason.CryptographicError,
        };

    private async Task<FileVerificationResult> CompleteFileVerificationAsync(
        IAssetSource source,
        SignatureResource resource,
        IReadOnlySet<string> duplicateIds,
        IReadOnlyDictionary<string, int> uniqueOwners,
        byte[] fileHash,
        Dictionary<string, CertificateEntry> certLookup,
        IReadOnlyCollection<CertificateEntry> certificateChain,
        string? schemeAdministratorId,
        TrustAnchorOptions trustAnchors,
        CancellationToken cancellationToken)
    {
        var computedHex = Convert.ToHexString(fileHash).ToLowerInvariant();

        // Checksum dimension: compare against the declared cryptographic hash
        // when one is present (S-100 Edition 5.2.1 Part 15 §15-8.10), otherwise
        // report that there is nothing to validate against.
        var checksumOutcome = resource.ExpectedHash is null
            ? VerificationOutcome.NoChecksum
            : resource.ExpectedHash.Matches(computedHex)
                ? VerificationOutcome.Ok
                : VerificationOutcome.ChecksumMismatch;

        var signatureResults = await VerifySignaturesAsync(
            source,
            resource,
            duplicateIds,
            uniqueOwners,
            fileHash,
            certLookup,
            certificateChain,
            schemeAdministratorId,
            trustAnchors,
            cancellationToken).ConfigureAwait(false);
        var firstFailure = signatureResults.FirstOrDefault(
            result => result.Outcome != VerificationOutcome.Ok);
        var signatureOutcome = signatureResults.Count == 0
            ? VerificationOutcome.NotSigned
            : firstFailure?.Outcome ?? VerificationOutcome.Ok;

        return new FileVerificationResult
        {
            FileName = resource.RelativePath,
            Outcome = signatureOutcome,
            SignatureResults = signatureResults,
            ChecksumOutcome = checksumOutcome,
            ComputedSha256 = computedHex,
            Detail = firstFailure?.Detail,
        };
    }

    /// <summary>
    /// Opens the file content that should be hashed for verification.
    /// </summary>
    /// <remarks>
    /// The default implementation returns the bytes exposed by the asset
    /// source. This compatibility seam is used for checksum calculation and
    /// legacy signatures. Part 15 <c>SignatureOnData</c> forms are resolved
    /// through the injected <see cref="ISignatureContentResolver"/> according
    /// to their explicit <c>dataStatus</c>.
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
    internal static (VerificationOutcome Outcome, string? Detail) EvaluateSignature(
        DigitalSignatureValue? signatureValue,
        DigitalSignatureAlgorithm algorithm,
        byte[] fileHash,
        Dictionary<string, CertificateEntry> certLookup,
        TrustAnchorOptions trustAnchors,
        IReadOnlyCollection<CertificateEntry>? certificateChain = null,
        DSASignatureFormat signatureFormat = DSASignatureFormat.IeeeP1363FixedFieldConcatenation,
        bool requireP384 = false,
        string? schemeAdministratorId = null,
        IReadOnlyCollection<X509Certificate2>? schemeAdministratorCertificates = null)
    {
        if (signatureValue is null)
        {
            return (VerificationOutcome.NotSigned, null);
        }

        // Resolve the certificate
        if (!certLookup.TryGetValue(signatureValue.CertificateRef, out var certEntry))
        {
            if (string.Equals(
                    signatureValue.CertificateRef,
                    schemeAdministratorId,
                    StringComparison.Ordinal) &&
                schemeAdministratorCertificates is { Count: > 0 })
            {
                return EvaluateSchemeAdministratorSignature(
                    signatureValue,
                    algorithm,
                    fileHash,
                    trustAnchors,
                    signatureFormat,
                    requireP384,
                    schemeAdministratorCertificates);
            }

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
            var trustOutcome = ValidateCertificateTrust(cert, trustAnchors, certificateChain);
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
                var valid = VerifySignature(
                    cert,
                    algorithm,
                    fileHash,
                    signatureValue.Value,
                    signatureFormat,
                    requireP384);
                return (valid ? VerificationOutcome.Ok : VerificationOutcome.SignatureInvalid, null);
            }
            catch (CryptographicException ex)
            {
                return (VerificationOutcome.Error, $"Signature verification error: {ex.Message}");
            }
        }
    }

    private static (VerificationOutcome Outcome, string? Detail) EvaluateSchemeAdministratorSignature(
        DigitalSignatureValue signatureValue,
        DigitalSignatureAlgorithm algorithm,
        byte[] fileHash,
        TrustAnchorOptions trustAnchors,
        DSASignatureFormat signatureFormat,
        bool requireP384,
        IReadOnlyCollection<X509Certificate2> certificates)
    {
        VerificationOutcome? failure = null;
        string? detail = null;
        foreach (var certificate in certificates)
        {
            var trustOutcome = ValidateCertificateTrust(certificate, trustAnchors, null);
            if (trustOutcome is not null)
            {
                failure = trustOutcome;
                detail = trustOutcome == VerificationOutcome.CertificateExpired
                    ? $"Scheme Administrator certificate expired on {certificate.NotAfter:O}."
                    : "Scheme Administrator certificate is not trusted.";
                continue;
            }

            try
            {
                if (VerifySignature(
                    certificate,
                    algorithm,
                    fileHash,
                    signatureValue.Value,
                    signatureFormat,
                    requireP384))
                {
                    return (VerificationOutcome.Ok, null);
                }

                failure = VerificationOutcome.SignatureInvalid;
                detail = null;
            }
            catch (CryptographicException ex)
            {
                failure = VerificationOutcome.Error;
                detail = $"Signature verification error: {ex.Message}";
            }
        }

        return (failure ?? VerificationOutcome.CertificateNotFound, detail);
    }

    /// <summary>
    /// Validates the certificate against the configured trust anchors.
    /// Returns <c>null</c> if the certificate is trusted (or trust validation is skipped),
    /// otherwise returns the appropriate <see cref="VerificationOutcome"/>.
    /// </summary>
    private static VerificationOutcome? ValidateCertificateTrust(
        X509Certificate2 cert,
        TrustAnchorOptions trustAnchors,
        IReadOnlyCollection<CertificateEntry>? certificateChain)
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

        using var chain = new X509Chain();
        var intermediates = new List<X509Certificate2>();
        try
        {
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
            foreach (var root in trustAnchors.TrustedRoots)
            {
                chain.ChainPolicy.CustomTrustStore.Add(root);
            }

            if (certificateChain is not null)
            {
                foreach (var entry in certificateChain)
                {
#if NET10_0_OR_GREATER
                    var intermediate = X509CertificateLoader.LoadCertificate(entry.Value);
#else
                    var intermediate = new X509Certificate2(entry.Value);
#endif
                    if (string.Equals(intermediate.Thumbprint, cert.Thumbprint, StringComparison.Ordinal))
                    {
                        intermediate.Dispose();
                        continue;
                    }

                    intermediates.Add(intermediate);
                    chain.ChainPolicy.ExtraStore.Add(intermediate);
                }
            }

            return chain.Build(cert) ? null : VerificationOutcome.CertificateUntrusted;
        }
        catch (CryptographicException)
        {
            return VerificationOutcome.Error;
        }
        finally
        {
            foreach (var intermediate in intermediates)
            {
                intermediate.Dispose();
            }
        }
    }

    /// <summary>
    /// Verifies a precomputed signature hash using the certificate's public key.
    /// Supports DSA and ECDSA algorithms with an explicit R/S encoding.
    /// </summary>
    private static bool VerifySignature(
        X509Certificate2 cert,
        DigitalSignatureAlgorithm algorithm,
        byte[] hash,
        byte[] signature,
        DSASignatureFormat signatureFormat,
        bool requireP384)
    {
        switch (algorithm)
        {
            case DigitalSignatureAlgorithm.ECDSA:
            case DigitalSignatureAlgorithm.ECDSA384SHA2:
                {
                    using var ecdsa = cert.GetECDsaPublicKey();
                    if (ecdsa is null)
                        throw new CryptographicException("Certificate does not contain an ECDSA public key.");
                    if (requireP384)
                    {
                        var curveOid = ecdsa.ExportParameters(includePrivateParameters: false).Curve.Oid.Value;
                        if (ecdsa.KeySize != 384 ||
                            !string.Equals(curveOid, NistP384Oid, StringComparison.Ordinal))
                        {
                            throw new CryptographicException(
                                "Part 15 standalone signatures require an ECDSA P-384 key.");
                        }

                    }

                    return ecdsa.VerifyHash(hash, signature, signatureFormat);
                }

            case DigitalSignatureAlgorithm.DSA:
                {
                    using var dsa = cert.GetDSAPublicKey();
                    if (dsa is null)
                        throw new CryptographicException("Certificate does not contain a DSA public key.");
                    return dsa.VerifySignature(hash, signature, signatureFormat);
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

    private static byte[] ComputeSignatureHash(
        DigitalSignatureAlgorithm algorithm,
        byte[] content) =>
        algorithm == DigitalSignatureAlgorithm.ECDSA384SHA2
            ? SHA384.HashData(content)
            : SHA256.HashData(content);

    private static async Task<byte[]> ComputeSignatureHashAsync(
        DigitalSignatureAlgorithm algorithm,
        Stream stream,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(
            algorithm == DigitalSignatureAlgorithm.ECDSA384SHA2
                ? HashAlgorithmName.SHA384
                : HashAlgorithmName.SHA256);
        var buffer = new byte[StreamBufferSize];
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(buffer, cancellationToken)) > 0)
        {
            hash.AppendData(buffer, 0, bytesRead);
        }

        return hash.GetHashAndReset();
    }

    private static IReadOnlyList<SignatureResource> CreateSignatureResources(
        ExchangeCatalogue catalogue)
    {
        var resources = new List<SignatureResource>();
        var resourceId = 0;
        resources.AddRange(catalogue.DatasetDiscoveryMetadata.Select(metadata =>
            new SignatureResource
            {
                ResourceId = resourceId++,
                RelativePath = metadata.RelativePath,
                DataProtection = metadata.DataProtection,
                CompressionFlag = metadata.CompressionFlag,
                Algorithm = metadata.DigitalSignatureAlgorithm,
                Signatures = GetSignatures(
                    metadata.DigitalSignatures,
                    metadata.DigitalSignatureValue),
                ExpectedHash = metadata.ExpectedHash,
            }));
        resources.AddRange(catalogue.SupportFileDiscoveryMetadata.Select(metadata =>
            new SignatureResource
            {
                ResourceId = resourceId++,
                RelativePath = metadata.RelativePath,
                CompressionFlag = metadata.CompressionFlag,
                Algorithm = metadata.DigitalSignatureAlgorithm,
                Signatures = GetSignatures(
                    metadata.DigitalSignatures,
                    metadata.DigitalSignatureValue),
                ExpectedHash = metadata.ExpectedHash,
            }));
        resources.AddRange(catalogue.CatalogueDiscoveryMetadata.Select(metadata =>
            new SignatureResource
            {
                ResourceId = resourceId++,
                RelativePath = metadata.RelativePath,
                CompressionFlag = metadata.CompressionFlag,
                Algorithm = metadata.DigitalSignatureAlgorithm,
                Signatures = GetSignatures(
                    metadata.DigitalSignatures,
                    metadata.DigitalSignatureValue),
                ExpectedHash = metadata.ExpectedHash,
            }));
        return resources;
    }

    private static IReadOnlyList<DigitalSignatureValue> GetSignatures(
        IReadOnlyList<DigitalSignatureValue> signatures,
        DigitalSignatureValue? compatibilitySignature) =>
        signatures.Count > 0
            ? signatures
            : compatibilitySignature is null
                ? []
                : [compatibilitySignature];

    private static Dictionary<string, List<SignatureLocation>> BuildSignatureIndex(
        IReadOnlyList<SignatureResource> resources)
    {
        var index = new Dictionary<string, List<SignatureLocation>>(StringComparer.Ordinal);
        foreach (var resource in resources)
        {
            foreach (var signature in resource.Signatures)
            {
                if (!index.TryGetValue(signature.Id, out var locations))
                {
                    locations = [];
                    index.Add(signature.Id, locations);
                }

                locations.Add(new SignatureLocation(resource.ResourceId));
            }
        }

        return index;
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

    private sealed class SignatureResource
    {
        public required int ResourceId { get; init; }

        public required string RelativePath { get; init; }

        public bool DataProtection { get; init; }

        public bool CompressionFlag { get; init; }

        public required DigitalSignatureAlgorithm Algorithm { get; init; }

        public required IReadOnlyList<DigitalSignatureValue> Signatures { get; init; }

        public CryptographicHash? ExpectedHash { get; init; }
    }

    private readonly record struct SignatureLocation(int ResourceId);

    private readonly record struct StructuralFailure(
        SignatureFailureReason Reason,
        string Detail);
}
