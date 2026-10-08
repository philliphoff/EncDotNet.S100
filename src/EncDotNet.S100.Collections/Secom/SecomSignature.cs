using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>The outcome of checking a SECOM data signature.</summary>
public enum SecomSignatureStatus
{
    /// <summary>The object carries no signature.</summary>
    Unsigned = 0,

    /// <summary>The signature matches the data and the signer's certificate.</summary>
    Valid,

    /// <summary>The signature does not match the data, or the certificate is unreadable.</summary>
    Invalid,

    /// <summary>The signature algorithm is not one this library knows.</summary>
    Unsupported,
}

/// <summary>The result of <see cref="SecomSignatureVerifier.Verify"/>.</summary>
/// <param name="Status">Whether the signature matches.</param>
/// <param name="Signer">The signer certificate's subject name, when it could be read.</param>
/// <param name="SignerTrusted">
/// True when the signer chains to one of the supplied trusted roots, false when
/// it does not, <see langword="null"/> when no roots were supplied.
/// </param>
/// <param name="SignerExpired">True when the signer certificate is outside its validity period now.</param>
/// <param name="Detail">Why the check failed or was not possible, if it did.</param>
public sealed record SecomSignatureCheck(
    SecomSignatureStatus Status,
    string? Signer = null,
    bool? SignerTrusted = null,
    bool SignerExpired = false,
    string? Detail = null);

/// <summary>
/// Checks the data signature of a SECOM data object
/// (<c>exchangeMetadata.digitalSignatureValue</c>): a hex-encoded signature
/// over the decoded data, made with the key of the first certificate in
/// <c>publicCertificate</c>.
/// </summary>
/// <remarks>
/// Signer trust is optional: SECOM signers hold Maritime Connectivity Platform
/// (MCP) certificates, whose roots are not in operating-system trust stores.
/// Without trusted roots the result says only whether the data matches the
/// stated certificate (<see cref="SecomSignatureCheck.SignerTrusted"/> is
/// <see langword="null"/>). Revocation is not checked.
/// </remarks>
public static class SecomSignatureVerifier
{
    /// <summary>Checks the signature of <paramref name="data"/>.</summary>
    /// <param name="data">The decoded data the signature covers.</param>
    /// <param name="metadata">The object's exchange metadata; <see langword="null"/> means unsigned.</param>
    /// <param name="trustedRoots">Roots the signer must chain to, or <see langword="null"/> to skip the trust check.</param>
    /// <param name="now">The time expiry is judged at; defaults to now.</param>
    public static SecomSignatureCheck Verify(
        byte[] data,
        SecomExchangeMetadata? metadata,
        IReadOnlyCollection<X509Certificate2>? trustedRoots = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (metadata is not { Signature: { Length: > 0 } signatureHex } || metadata.PublicCertificates.Count == 0)
            return new SecomSignatureCheck(SecomSignatureStatus.Unsigned);

        byte[] signature;
        try
        {
            signature = Convert.FromHexString(signatureHex.Trim());
        }
        catch (FormatException)
        {
            return new SecomSignatureCheck(SecomSignatureStatus.Invalid, Detail: "The signature is not hex.");
        }

        var certificates = new List<X509Certificate2>();
        try
        {
            foreach (var text in metadata.PublicCertificates)
                certificates.Add(LoadCertificate(text));
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            certificates.ForEach(c => c.Dispose());
            return new SecomSignatureCheck(SecomSignatureStatus.Invalid, Detail: "The signer certificate cannot be read.");
        }

        try
        {
            var signer = certificates[0];
            var subject = signer.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
            var at = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
            var expired = at < signer.NotBefore.ToUniversalTime() || at > signer.NotAfter.ToUniversalTime();

            SecomSignatureStatus status;
            string? detail = null;
            try
            {
                status = VerifyData(signer, metadata.SignatureReference, data, signature)
                    ? SecomSignatureStatus.Valid
                    : SecomSignatureStatus.Invalid;
            }
            catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or NotSupportedException)
            {
                status = SecomSignatureStatus.Unsupported;
                detail = ex.Message;
            }

            var trusted = trustedRoots is null || status != SecomSignatureStatus.Valid
                ? (bool?)null
                : ChainsToTrustedRoot(signer, certificates.Skip(1), trustedRoots, metadata.RootCertificateThumbprint);
            return new SecomSignatureCheck(status, subject, trusted, expired, detail);
        }
        finally
        {
            certificates.ForEach(c => c.Dispose());
        }
    }

    /// <summary>
    /// Loads a certificate sent as base64 DER or as (possibly single-line)
    /// PEM, the "minified" form SECOM services use.
    /// </summary>
    /// <exception cref="FormatException">The text is not base64.</exception>
    /// <exception cref="CryptographicException">The bytes are not a certificate.</exception>
    public static X509Certificate2 LoadCertificate(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var body = text
            .Replace("-----BEGIN CERTIFICATE-----", string.Empty, StringComparison.Ordinal)
            .Replace("-----END CERTIFICATE-----", string.Empty, StringComparison.Ordinal);
        var der = Convert.FromBase64String(string.Concat(body.Where(c => !char.IsWhiteSpace(c))));
#if NET10_0_OR_GREATER
        return X509CertificateLoader.LoadCertificate(der);
#else
        return new X509Certificate2(der);
#endif
    }

    private static bool VerifyData(X509Certificate2 signer, string? reference, byte[] data, byte[] signature)
    {
        var (family, hash) = Algorithm(reference, signer);
        var digest = Digest(hash, data, reference);

        // Signatures come DER-encoded (Java's default) or as raw r‖s; accept either.
        DSASignatureFormat[] formats = signature.Length > 0 && signature[0] == 0x30
            ? [DSASignatureFormat.Rfc3279DerSequence, DSASignatureFormat.IeeeP1363FixedFieldConcatenation]
            : [DSASignatureFormat.IeeeP1363FixedFieldConcatenation, DSASignatureFormat.Rfc3279DerSequence];

        switch (family)
        {
            case "ecdsa":
                {
                    using var ecdsa = signer.GetECDsaPublicKey()
                        ?? throw new CryptographicException("The signer certificate has no ECDSA key.");
                    foreach (var format in formats)
                    {
                        if (TryVerify(() => ecdsa.VerifyHash(digest, signature, format)))
                            return true;
                    }

                    return false;
                }

            case "dsa":
                {
                    using var dsa = signer.GetDSAPublicKey()
                        ?? throw new CryptographicException("The signer certificate has no DSA key.");
                    foreach (var format in formats)
                    {
                        if (TryVerify(() => dsa.VerifySignature(digest, signature, format)))
                            return true;
                    }

                    return false;
                }

            default:
                throw new NotSupportedException($"The signature algorithm '{reference}' is not supported.");
        }
    }

    /// <summary>
    /// Hashes <paramref name="data"/> for verification against the digest.
    /// SHA3 falls back to <see cref="Sha3"/> where the platform has none (macOS
    /// as of .NET 10), so every algorithm verifies on every platform (#806).
    /// </summary>
    private static byte[] Digest(HashAlgorithmName hash, byte[] data, string? reference)
    {
        if (hash == HashAlgorithmName.SHA256)
            return SHA256.HashData(data);
        if (hash == HashAlgorithmName.SHA384)
            return SHA384.HashData(data);
        if (hash == HashAlgorithmName.SHA3_256)
            return SHA3_256.IsSupported ? SHA3_256.HashData(data) : Sha3.HashData256(data);
        if (hash == HashAlgorithmName.SHA3_384)
            return SHA3_384.IsSupported ? SHA3_384.HashData(data) : Sha3.HashData384(data);
        throw new NotSupportedException($"The signature algorithm '{reference}' is not supported.");
    }

    /// <summary>A malformed signature in one encoding throws; treat that as "does not verify".</summary>
    private static bool TryVerify(Func<bool> verify)
    {
        try
        {
            return verify();
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Maps a SECOM <c>digitalSignatureReference</c> to a key family and hash
    /// (IEC 63173-2; names as SECOMLib uses them). When absent, the signer's
    /// key decides: ECDSA with SHA-384 for P-384, SHA-256 otherwise.
    /// </summary>
    private static (string Family, HashAlgorithmName Hash) Algorithm(string? reference, X509Certificate2 signer) =>
        reference?.Trim().ToLowerInvariant() switch
        {
            "ecdsa-256-sha2-256" => ("ecdsa", HashAlgorithmName.SHA256),
            "ecdsa-256-sha3-256" => ("ecdsa", HashAlgorithmName.SHA3_256),
            "ecdsa-384-sha2" => ("ecdsa", HashAlgorithmName.SHA384),
            "ecdsa-384-sha3" => ("ecdsa", HashAlgorithmName.SHA3_384),
            "dsa" => ("dsa", HashAlgorithmName.SHA3_384),
            null or "" => signer.GetECDsaPublicKey() is { } key
                ? ("ecdsa", DisposeAndPick(key))
                : ("dsa", HashAlgorithmName.SHA3_384),
            var other => (other, default),
        };

    private static HashAlgorithmName DisposeAndPick(ECDsa key)
    {
        using (key)
            return key.KeySize >= 384 ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256;
    }

    private static bool ChainsToTrustedRoot(
        X509Certificate2 signer,
        IEnumerable<X509Certificate2> intermediates,
        IReadOnlyCollection<X509Certificate2> roots,
        string? statedRootThumbprint)
    {
        if (roots.Count == 0)
            return false;

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        // Expiry is reported separately; old objects stay attributable to their signer.
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
        chain.ChainPolicy.CustomTrustStore.AddRange(roots.ToArray());
        chain.ChainPolicy.ExtraStore.AddRange(intermediates.ToArray());
        try
        {
            if (!chain.Build(signer))
                return false;
            if (statedRootThumbprint is not { Length: > 0 } stated)
                return true;

            var root = chain.ChainElements[^1].Certificate;
            return string.Equals(stated, root.Thumbprint, StringComparison.OrdinalIgnoreCase)
                || string.Equals(stated, Convert.ToHexString(SHA256.HashData(root.RawData)), StringComparison.OrdinalIgnoreCase);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
