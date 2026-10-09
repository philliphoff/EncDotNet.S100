using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace EncDotNet.S100.Collections.Secom;

/// <summary>
/// An MCP identity for SECOM requests (issue #832): a client certificate with
/// its private key, presented by <see cref="SecomServerTrust"/>'s handlers when
/// a SECOM service asks for one (mutual TLS).
/// </summary>
/// <remarks>
/// Identities are issued by an MCP Identity Registry to an organisation's
/// devices and services; their subject carries the MRN (as <c>UID</c>). This
/// library only holds an identity in memory: loading it from a key store and
/// persisting it is the host's concern.
/// </remarks>
public sealed class SecomClientIdentity : IDisposable
{
    private const string UidOid = "0.9.2342.19200300.100.1.1";

    private SecomClientIdentity(X509Certificate2 certificate, string? anchor)
    {
        Certificate = certificate;
        Anchor = anchor;
        Subject = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        Mrn = SubjectAttribute(certificate, UidOid) is { } uid && uid.StartsWith("urn:mrn:", StringComparison.OrdinalIgnoreCase) ? uid : null;
        NotBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
        NotAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
    }

    /// <summary>The certificate, with its private key.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>The certificate's subject common name.</summary>
    public string Subject { get; }

    /// <summary>The MRN the certificate was issued to (its subject <c>UID</c>), when it has one.</summary>
    public string? Mrn { get; }

    /// <summary>The trust anchor the certificate chains to (e.g. "MCP MCC"), or <see langword="null"/> for none.</summary>
    public string? Anchor { get; }

    /// <summary>When the certificate becomes valid.</summary>
    public DateTimeOffset NotBefore { get; }

    /// <summary>When the certificate expires.</summary>
    public DateTimeOffset NotAfter { get; }

    /// <summary>The MRN when there is one, otherwise the subject: how the identity is named in messages.</summary>
    public string DisplayName => Mrn ?? Subject;

    /// <summary>True when <paramref name="at"/> lies within the certificate's validity period.</summary>
    public bool IsValidAt(DateTimeOffset at) => at >= NotBefore && at <= NotAfter;

    /// <summary>
    /// Wraps a certificate that already holds its private key.
    /// </summary>
    /// <param name="certificate">The certificate; the identity takes ownership of it.</param>
    /// <param name="anchors">The anchors to report <see cref="Anchor"/> against; by default <see cref="SecomTrustAnchors.BuiltIn"/>.</param>
    /// <exception cref="InvalidDataException">The certificate has no private key.</exception>
    public static SecomClientIdentity FromCertificate(X509Certificate2 certificate, SecomTrustAnchors? anchors = null)
    {
        ArgumentNullException.ThrowIfNull(certificate);
        if (!certificate.HasPrivateKey)
            throw new InvalidDataException("The certificate has no private key, so it cannot identify this client.");

        return new SecomClientIdentity(certificate, (anchors ?? SecomTrustAnchors.BuiltIn).FindAnchor(certificate, [], statedThumbprint: null));
    }

    /// <summary>
    /// Loads an identity from a PKCS#12 file (<c>.p12</c>, <c>.pfx</c>) or a
    /// PEM file holding the certificate and its private key (optionally
    /// encrypted), as the MCP Management Portal issues them.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <param name="password">The PKCS#12 or encrypted-key password, if any.</param>
    /// <param name="anchors">The anchors to report <see cref="Anchor"/> against; by default <see cref="SecomTrustAnchors.BuiltIn"/>.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file holds no certificate with a private key, or the password is wrong.</exception>
    public static SecomClientIdentity Load(string path, string? password = null, SecomTrustAnchors? anchors = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("The identity file does not exist.", path);

        X509Certificate2 certificate;
        try
        {
            var bytes = File.ReadAllBytes(path);
            certificate = IsPem(bytes) ? FromPem(File.ReadAllText(path), password) : FromPkcs12(bytes, password);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidDataException($"The identity could not be read: {ex.Message}", ex);
        }

        try
        {
            return FromCertificate(certificate, anchors);
        }
        catch
        {
            certificate.Dispose();
            throw;
        }
    }

    /// <inheritdoc />
    public void Dispose() => Certificate.Dispose();

    private static bool IsPem(byte[] bytes) =>
        bytes.AsSpan().IndexOf("-----BEGIN"u8) >= 0;

    /// <summary>
    /// The certificate and key from PEM text, re-loaded through PKCS#12: a key
    /// attached in memory is not usable for client authentication on macOS
    /// and Windows, whose TLS stacks need a key they can find again.
    /// </summary>
    private static X509Certificate2 FromPem(string pem, string? password)
    {
        using var attached = string.IsNullOrEmpty(password)
            ? X509Certificate2.CreateFromPem(pem, pem)
            : X509Certificate2.CreateFromEncryptedPem(pem, pem, password);
        var transfer = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return FromPkcs12(attached.Export(X509ContentType.Pkcs12, transfer), transfer);
    }

    private static X509Certificate2 FromPkcs12(byte[] bytes, string? password)
    {
#if NET10_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(bytes, password);
#else
        return new X509Certificate2(bytes, password);
#endif
    }

    private static string? SubjectAttribute(X509Certificate2 certificate, string oid)
    {
        foreach (var rdn in certificate.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            try
            {
                if (!rdn.HasMultipleElements && rdn.GetSingleElementType().Value == oid)
                    return rdn.GetSingleElementValue();
            }
            catch (Exception ex) when (ex is InvalidOperationException or CryptographicException or AsnContentException)
            {
                // An attribute that is not a string: not the one wanted.
            }
        }

        return null;
    }
}
