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
/// True when the signer chains to one of the trust anchors, false when it does
/// not, <see langword="null"/> when trust was not checked (no anchors were
/// supplied, or the signature is not valid).
/// </param>
/// <param name="SignerExpired">True when the signer certificate is outside its validity period now.</param>
/// <param name="Detail">Why the check failed or was not possible, if it did.</param>
public sealed record SecomSignatureCheck(
    SecomSignatureStatus Status,
    string? Signer = null,
    bool? SignerTrusted = null,
    bool SignerExpired = false,
    string? Detail = null)
{
    /// <summary>
    /// The name of the trust anchor the signer chains to (e.g. "MCP MCC") when
    /// <see cref="SignerTrusted"/> is true.
    /// </summary>
    public string? TrustAnchor { get; init; }

    /// <summary>
    /// Whether the signer's certificate, or a CA above it, has been revoked
    /// (#833). It is checked only for a trusted signer and only when a
    /// <see cref="SecomRevocation"/> is given; otherwise, and when no current
    /// CRL could be had, it is <see cref="SecomRevocationStatus.NotChecked"/>.
    /// It is independent of <see cref="SignerTrusted"/> and <see cref="SignerExpired"/>.
    /// </summary>
    public SecomRevocationStatus SignerRevocation { get; init; }
}

/// <summary>
/// Checks the data signature of a SECOM data object
/// (<c>exchangeMetadata.digitalSignatureValue</c>): a hex-encoded signature
/// over the decoded data, made with the key of the first certificate in
/// <c>publicCertificate</c>.
/// </summary>
/// <remarks>
/// Signer trust is checked against <see cref="SecomTrustAnchors"/>: SECOM
/// signers hold Maritime Connectivity Platform (MCP) certificates, whose roots
/// are not in operating-system trust stores. Without anchors the result says
/// only whether the data matches the stated certificate
/// (<see cref="SecomSignatureCheck.SignerTrusted"/> is <see langword="null"/>).
/// Revocation is checked for a trusted signer when a <see cref="SecomRevocation"/>
/// is given (<see cref="SecomSignatureCheck.SignerRevocation"/>).
/// </remarks>
public static class SecomSignatureVerifier
{
    /// <summary>Checks the signature of <paramref name="data"/>.</summary>
    /// <param name="data">The decoded data the signature covers.</param>
    /// <param name="metadata">The object's exchange metadata; <see langword="null"/> means unsigned.</param>
    /// <param name="trust">The anchors the signer must chain to (e.g. <see cref="SecomTrustAnchors.BuiltIn"/>), or <see langword="null"/> to skip the trust check.</param>
    /// <param name="now">The time expiry is judged at; defaults to now.</param>
    /// <param name="revocation">Checks a trusted signer's chain for revocation, or <see langword="null"/> to skip the check.</param>
    public static SecomSignatureCheck Verify(
        byte[] data,
        SecomExchangeMetadata? metadata,
        SecomTrustAnchors? trust = null,
        DateTimeOffset? now = null,
        SecomRevocation? revocation = null)
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

            return WithSigner(new SecomSignatureCheck(status, subject, Detail: detail), certificates, metadata.RootCertificateThumbprint, trust, now, revocation);
        }
        finally
        {
            certificates.ForEach(c => c.Dispose());
        }
    }

    /// <summary>
    /// Judges the signer of an earlier check again: its expiry now and, for a
    /// valid signature, its trust against <paramref name="trust"/> and its
    /// revocation with <paramref name="revocation"/>. A recorded
    /// download is re-judged this way each time it is read, so trust follows
    /// the current anchors without re-downloading.
    /// </summary>
    /// <param name="check">The earlier check.</param>
    /// <param name="publicCertificates">The certificates the object carried, signer first (base64 DER or PEM).</param>
    /// <param name="statedThumbprint">The object's <c>publicRootCertificateThumbprint</c>, if any.</param>
    /// <param name="trust">The anchors to judge trust against, or <see langword="null"/> to skip the trust check.</param>
    /// <param name="now">The time expiry is judged at; defaults to now.</param>
    /// <param name="revocation">
    /// Checks a trusted signer's chain for revocation, or <see langword="null"/>
    /// to skip the check. On read paths pass <see cref="SecomRevocation.CacheOnly"/>.
    /// </param>
    /// <returns>The check with its trust, revocation and expiry updated, or unchanged when the certificates cannot be read.</returns>
    public static SecomSignatureCheck Recheck(
        SecomSignatureCheck check,
        IReadOnlyList<string> publicCertificates,
        string? statedThumbprint,
        SecomTrustAnchors? trust,
        DateTimeOffset? now = null,
        SecomRevocation? revocation = null)
    {
        ArgumentNullException.ThrowIfNull(check);
        ArgumentNullException.ThrowIfNull(publicCertificates);
        if (publicCertificates.Count == 0)
            return check;

        var certificates = new List<X509Certificate2>();
        try
        {
            foreach (var text in publicCertificates)
                certificates.Add(LoadCertificate(text));
            return WithSigner(check, certificates, statedThumbprint, trust, now, revocation);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return check;
        }
        finally
        {
            certificates.ForEach(c => c.Dispose());
        }
    }

    private static SecomSignatureCheck WithSigner(
        SecomSignatureCheck check,
        List<X509Certificate2> certificates,
        string? statedThumbprint,
        SecomTrustAnchors? trust,
        DateTimeOffset? now,
        SecomRevocation? revocation)
    {
        var signer = certificates[0];
        var at = (now ?? DateTimeOffset.UtcNow).UtcDateTime;
        var expired = at < signer.NotBefore.ToUniversalTime() || at > signer.NotAfter.ToUniversalTime();
        if (trust is null || check.Status != SecomSignatureStatus.Valid)
            return check with { SignerTrusted = null, SignerExpired = expired, TrustAnchor = null, SignerRevocation = SecomRevocationStatus.NotChecked };

        var anchor = trust.FindAnchor(signer, certificates.Skip(1), statedThumbprint, out var chain);
        var revoked = anchor is not null && chain is not null && revocation is not null
            ? revocation.Check(chain).Status
            : SecomRevocationStatus.NotChecked;
        return check with { SignerTrusted = anchor is not null, SignerExpired = expired, TrustAnchor = anchor, SignerRevocation = revoked };
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
    internal static byte[] Digest(HashAlgorithmName hash, byte[] data, string? reference)
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
    internal static (string Family, HashAlgorithmName Hash) Algorithm(string? reference, X509Certificate2 signer) =>
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
}
