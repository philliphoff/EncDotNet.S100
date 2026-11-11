using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.Collections.Net;

/// <summary>
/// Public keys this library refuses in any certificate chain, whatever the
/// operating system's trust store says: CA keys known to be compromised.
/// Each is matched by the SHA-256 of its DER-encoded SubjectPublicKeyInfo,
/// so every certificate carrying the key is refused, including cross-signed
/// copies under another root.
/// </summary>
/// <remarks>
/// <para>
/// eMudhra intermediates (October 2026): the private keys of eight intermediates
/// from one April 2025 key ceremony were generated with trivially small scalars
/// and can be recovered by anyone. Each key was certified twice, under
/// emSign Root TLS/CS CA - G3 and cross-signed under Sectigo's AAA Certificate
/// Services. Browsers block them through CRLSets and OneCRL, but a .NET client
/// on Linux trusts the distro's CA bundle without checking revocation or
/// Certificate Transparency until that bundle is updated. The list matches
/// Go's <c>crypto/x509</c> <c>distrustedSPKIHashes</c> and
/// Mozilla's <c>impacted-intermediates.json</c>; see Mozilla bug 2083449.
/// </para>
/// <para>
/// Entries stay: a compromised key is never trustworthy again.
/// </para>
/// </remarks>
public static class DistrustedKeys
{
    private static readonly FrozenSet<string> Hashes = new[]
    {
        // eMudhra, Mozilla bug 2083449 (TLS)
        "ebed6475c2c2d80fcb199f3fe8dd6e6b3ce60b77eb15d4e42c3dec0c6f5b5105", // EM DV TLS CA - G3B
        "43bffa19d6ef651451f6841e019931e96f6f2eaeff28ab4b6f1ce4576de2717b", // EM OV TLS CA - G3B
        "e0254c7fcdc265e2f0f8a428dabb75ab4ae039b4872d62f5ee57f0877d5a8a41", // EM EV TLS CA - G3B
        "49dd353dc2b919a3770645117d0e60c16115e7d1bc259eefe734b00ea434fe5f", // EM DV TLS CA - G3C
        "63ecefee431d49523bc1136c4e72d0206a477086caf606cd5cf025686c109cf8", // EM OV TLS CA - G3C
        "5c0660a2b164910d8ff589c18e14552e9e0ec237dcefe2cb736d41a4811f81a0", // EM EV TLS CA - G3C

        // eMudhra, Mozilla bug 2083449 (code signing)
        "ec4ffa1ad0ac88081314b7aaffef6a91f97cd57291bb5ba686c5415f5eb4f6b6", // EM OV CS CA - G3B
        "92c063d67e8f14db71d288e11266ffdaf69cbf115f7bab14f3bc2f68d25e0e30", // EM OV CS CA - G3C
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The distrusted keys, as lower-case hex SHA-256 hashes of their SubjectPublicKeyInfo.</summary>
    public static IReadOnlyCollection<string> SpkiSha256 => Hashes;

    /// <summary>The lower-case hex SHA-256 of <paramref name="certificate"/>'s SubjectPublicKeyInfo.</summary>
    public static string SpkiSha256Of(X509Certificate2 certificate)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        var hash = SHA256.HashData(certificate.PublicKey.ExportSubjectPublicKeyInfo());
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Whether <paramref name="certificate"/> carries a distrusted key.</summary>
    public static bool IsDistrusted(X509Certificate2 certificate) =>
        Hashes.Contains(SpkiSha256Of(certificate));

    /// <summary>The first of <paramref name="certificates"/> that carries a distrusted key, or <see langword="null"/>.</summary>
    public static X509Certificate2? FindDistrusted(IEnumerable<X509Certificate2?> certificates)
    {
        ArgumentNullException.ThrowIfNull(certificates);
        foreach (var certificate in certificates)
        {
            if (certificate is not null && IsDistrusted(certificate))
                return certificate;
        }

        return null;
    }

    /// <summary>
    /// The first certificate in <paramref name="chain"/>'s elements, or
    /// <paramref name="certificate"/> itself, that carries a distrusted key;
    /// <see langword="null"/> when none does.
    /// </summary>
    public static X509Certificate2? FindDistrusted(X509Certificate2? certificate, X509Chain? chain)
    {
        IEnumerable<X509Certificate2?> elements = chain?.ChainElements.Select(e => e.Certificate) ?? [];
        return FindDistrusted(elements.Prepend(certificate));
    }
}
