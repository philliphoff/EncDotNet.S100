using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>A root certificate SECOM signers may chain to, with the name shown for it.</summary>
/// <param name="Name">The name shown when a signer chains to this root, e.g. "MCP MCC".</param>
/// <param name="Certificate">The root certificate.</param>
public sealed record SecomTrustAnchor(string Name, X509Certificate2 Certificate);

/// <summary>
/// The roots SECOM data signers are trusted against (issue #823), plus any
/// intermediate CA certificates needed to reach them. SECOM signers hold
/// Maritime Connectivity Platform (MCP) certificates, whose roots are not in
/// operating-system trust stores, and services send the signer certificate
/// without its chain.
/// </summary>
/// <remarks>
/// <see cref="BuiltIn"/> trusts the MCP MCC instance. Hosts add their own
/// roots with <see cref="With(SecomTrustAnchor, IEnumerable{X509Certificate2}?)"/>.
/// Revocation is not checked.
/// </remarks>
public sealed partial class SecomTrustAnchors
{
    private static readonly Lazy<SecomTrustAnchors> BuiltInAnchors = new(LoadBuiltIn);

    /// <summary>Creates a set of trust anchors.</summary>
    /// <param name="roots">The trusted roots.</param>
    /// <param name="intermediates">CA certificates that may sit between a signer and a root.</param>
    public SecomTrustAnchors(IEnumerable<SecomTrustAnchor> roots, IEnumerable<X509Certificate2>? intermediates = null)
    {
        ArgumentNullException.ThrowIfNull(roots);
        Roots = roots.ToArray();
        Intermediates = intermediates?.ToArray() ?? [];
    }

    /// <summary>No trusted roots: every signer is reported as not trusted.</summary>
    public static SecomTrustAnchors None { get; } = new([]);

    /// <summary>
    /// The roots built into this library: the MCP MCC instance's CA chain
    /// (named "MCP MCC"). The source and fingerprints are recorded in
    /// <c>Secom/TrustAnchors/README.md</c>.
    /// </summary>
    public static SecomTrustAnchors BuiltIn => BuiltInAnchors.Value;

    /// <summary>The trusted roots.</summary>
    public IReadOnlyList<SecomTrustAnchor> Roots { get; }

    /// <summary>CA certificates that may sit between a signer and a root.</summary>
    public IReadOnlyList<X509Certificate2> Intermediates { get; }

    /// <summary>This set plus <paramref name="root"/> and its <paramref name="intermediates"/>.</summary>
    public SecomTrustAnchors With(SecomTrustAnchor root, IEnumerable<X509Certificate2>? intermediates = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        return new SecomTrustAnchors([.. Roots, root], [.. Intermediates, .. intermediates ?? Enumerable.Empty<X509Certificate2>()]);
    }

    /// <summary>
    /// Reads a PEM bundle as trust anchors named <paramref name="name"/>:
    /// self-signed certificates become roots, the rest intermediates.
    /// </summary>
    /// <exception cref="FormatException">A certificate block is not base64.</exception>
    /// <exception cref="CryptographicException">A block is not a certificate.</exception>
    public static SecomTrustAnchors FromPem(string name, string pem)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(pem);
        var roots = new List<SecomTrustAnchor>();
        var intermediates = new List<X509Certificate2>();
        foreach (Match block in PemBlock().Matches(pem))
        {
            var certificate = SecomSignatureVerifier.LoadCertificate(block.Value);
            if (certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData))
                roots.Add(new SecomTrustAnchor(name, certificate));
            else
                intermediates.Add(certificate);
        }

        return new SecomTrustAnchors(roots, intermediates);
    }

    /// <summary>
    /// Whether <paramref name="signer"/> chains to one of these roots, using
    /// the certificates the object carried and <see cref="Intermediates"/>.
    /// Expiry is ignored here (it is reported separately, so old objects stay
    /// attributable to their signer).
    /// </summary>
    /// <param name="signer">The signer certificate.</param>
    /// <param name="carried">Further certificates the object carried.</param>
    /// <param name="statedThumbprint">
    /// The object's <c>publicRootCertificateThumbprint</c>, if any. It must
    /// name a CA in the chain: services state the root or, as DMA's does,
    /// the issuing intermediate, by SHA-1 or SHA-256.
    /// </param>
    /// <returns>The name of the root reached, or <see langword="null"/> when the signer is not trusted.</returns>
    public string? FindAnchor(X509Certificate2 signer, IEnumerable<X509Certificate2> carried, string? statedThumbprint)
    {
        ArgumentNullException.ThrowIfNull(signer);
        ArgumentNullException.ThrowIfNull(carried);
        if (Roots.Count == 0)
            return null;

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
        chain.ChainPolicy.CustomTrustStore.AddRange(Roots.Select(r => r.Certificate).ToArray());
        chain.ChainPolicy.ExtraStore.AddRange(carried.Concat(Intermediates).ToArray());
        try
        {
            if (!chain.Build(signer))
                return null;

            // The CAs above the signer; a self-signed signer that is itself a root is its own CA.
            var elements = chain.ChainElements.Select(e => e.Certificate).ToArray();
            var authorities = elements.Length > 1 ? elements[1..] : elements;
            if (statedThumbprint is { Length: > 0 } stated && !authorities.Any(c => Names(c, stated.Trim())))
                return null;

            var root = authorities[^1];
            return Roots.FirstOrDefault(r => r.Certificate.RawData.AsSpan().SequenceEqual(root.RawData))?.Name;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private static bool Names(X509Certificate2 certificate, string thumbprint) =>
        string.Equals(thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)
        || string.Equals(thumbprint, Convert.ToHexString(SHA256.HashData(certificate.RawData)), StringComparison.OrdinalIgnoreCase);

    private static SecomTrustAnchors LoadBuiltIn()
    {
        using var stream = typeof(SecomTrustAnchors).Assembly.GetManifestResourceStream("EncDotNet.S100.Collections.Secom.TrustAnchors.mcp-mcc.pem")
            ?? throw new InvalidOperationException("The built-in SECOM trust anchors are missing.");
        using var reader = new StreamReader(stream);
        return FromPem("MCP MCC", reader.ReadToEnd());
    }

    [GeneratedRegex("-----BEGIN CERTIFICATE-----[^-]+-----END CERTIFICATE-----")]
    private static partial Regex PemBlock();
}
