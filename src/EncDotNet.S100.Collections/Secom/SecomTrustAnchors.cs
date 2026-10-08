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
        if (BuildChain(signer, carried) is not { } elements)
            return null;

        // The CAs above the signer; a self-signed signer that is itself a root is its own CA.
        var authorities = elements.Length > 1 ? elements[1..] : elements;
        if (statedThumbprint is { Length: > 0 } stated && !authorities.Any(c => Names(c, stated.Trim())))
            return null;

        return AnchorNamed(authorities[^1]);
    }

    /// <summary>
    /// Whether a TLS server certificate chains to one of these roots (#829),
    /// using the certificates the server presented and <see cref="Intermediates"/>.
    /// Unlike a signer, a server must be within its validity period, as must
    /// every CA above it.
    /// </summary>
    /// <param name="server">The server's certificate.</param>
    /// <param name="presented">Further certificates the server presented.</param>
    /// <param name="at">The time validity is judged at.</param>
    /// <returns>
    /// The name of the root reached (<see langword="null"/> when the chain
    /// reaches none), and whether a certificate in that chain is outside its
    /// validity period at <paramref name="at"/>.
    /// </returns>
    public (string? Anchor, bool Expired) FindServerAnchor(X509Certificate2 server, IEnumerable<X509Certificate2> presented, DateTimeOffset at)
    {
        ArgumentNullException.ThrowIfNull(server);
        ArgumentNullException.ThrowIfNull(presented);
        if (BuildChain(server, presented) is not { } elements)
            return (null, false);

        var utc = at.UtcDateTime;
        var expired = elements.Any(c => utc < c.NotBefore.ToUniversalTime() || utc > c.NotAfter.ToUniversalTime());
        return (AnchorNamed(elements[^1]), expired);
    }

    /// <summary>
    /// The chain from <paramref name="leaf"/> to one of these roots, leaf
    /// first, ignoring validity periods and revocation; <see langword="null"/>
    /// when there is none.
    /// </summary>
    private X509Certificate2[]? BuildChain(X509Certificate2 leaf, IEnumerable<X509Certificate2> extra)
    {
        if (Roots.Count == 0)
            return null;

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.IgnoreNotTimeValid;
        chain.ChainPolicy.CustomTrustStore.AddRange(Roots.Select(r => r.Certificate).ToArray());
        chain.ChainPolicy.ExtraStore.AddRange(extra.Concat(Intermediates).ToArray());
        try
        {
            return chain.Build(leaf) ? chain.ChainElements.Select(e => e.Certificate).ToArray() : null;
        }
        catch (CryptographicException)
        {
            return null;
        }
    }

    private string? AnchorNamed(X509Certificate2 root) =>
        Roots.FirstOrDefault(r => r.Certificate.RawData.AsSpan().SequenceEqual(root.RawData))?.Name;

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
