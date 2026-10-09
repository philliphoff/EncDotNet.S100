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

    // MCP attributes an Identity Registry writes into the subject alternative
    // name as otherName entries (MCP PKI documentation, "Certificate attributes").
    private const string FlagStateOid = "2.25.323100633285601570573910217875371967771";
    private const string CallSignOid = "2.25.208070283325144527098121348946972755227";
    private const string ImoNumberOid = "2.25.291283622413876360871493815653100799259";
    private const string MmsiOid = "2.25.328433707816814908768060331477217690907";

    private SecomClientIdentity(X509Certificate2 certificate, string? anchor)
    {
        Certificate = certificate;
        Anchor = anchor;
        Subject = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: false);
        Mrn = SubjectAttribute(certificate, UidOid) is { } uid && uid.StartsWith("urn:mrn:", StringComparison.OrdinalIgnoreCase) ? uid : null;
        NotBefore = new DateTimeOffset(certificate.NotBefore.ToUniversalTime(), TimeSpan.Zero);
        NotAfter = new DateTimeOffset(certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        Issuer = certificate.GetNameInfo(X509NameType.SimpleName, forIssuer: true);
        var attributes = AlternativeNameAttributes(certificate);
        FlagState = attributes.GetValueOrDefault(FlagStateOid);
        CallSign = attributes.GetValueOrDefault(CallSignOid);
        ImoNumber = attributes.GetValueOrDefault(ImoNumberOid);
        Mmsi = attributes.GetValueOrDefault(MmsiOid);
    }

    /// <summary>The certificate, with its private key.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>The certificate's subject common name.</summary>
    public string Subject { get; }

    /// <summary>The MRN the certificate was issued to (its subject <c>UID</c>), when it has one.</summary>
    public string? Mrn { get; }

    /// <summary>The issuer's common name, e.g. "MCP Identity Registry".</summary>
    public string Issuer { get; }

    /// <summary>The vessel's flag state, when the certificate carries the MCP attribute.</summary>
    public string? FlagState { get; }

    /// <summary>The vessel's call sign, when the certificate carries the MCP attribute.</summary>
    public string? CallSign { get; }

    /// <summary>The vessel's IMO number, when the certificate carries the MCP attribute.</summary>
    public string? ImoNumber { get; }

    /// <summary>The vessel's MMSI, when the certificate carries the MCP attribute.</summary>
    public string? Mmsi { get; }

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
    /// <param name="keyStorageFlags">
    /// How the private key is held, e.g. <see cref="X509KeyStorageFlags.Exportable"/>
    /// or <see cref="X509KeyStorageFlags.PersistKeySet"/> for a host that adds the
    /// identity to a platform key store; by default the platform's default.
    /// </param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file holds no certificate with a private key, or the password is wrong.</exception>
    public static SecomClientIdentity Load(
        string path,
        string? password = null,
        SecomTrustAnchors? anchors = null,
        X509KeyStorageFlags keyStorageFlags = X509KeyStorageFlags.DefaultKeySet)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("The identity file does not exist.", path);

        X509Certificate2 certificate;
        try
        {
            var bytes = File.ReadAllBytes(path);
            certificate = IsPem(bytes)
                ? FromPem(File.ReadAllText(path), password, keyStorageFlags)
                : FromPkcs12(bytes, password, keyStorageFlags);
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
    private static X509Certificate2 FromPem(string pem, string? password, X509KeyStorageFlags keyStorageFlags)
    {
        using var attached = string.IsNullOrEmpty(password)
            ? X509Certificate2.CreateFromPem(pem, pem)
            : X509Certificate2.CreateFromEncryptedPem(pem, pem, password);
        var transfer = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        return FromPkcs12(attached.Export(X509ContentType.Pkcs12, transfer), transfer, keyStorageFlags);
    }

    private static X509Certificate2 FromPkcs12(byte[] bytes, string? password, X509KeyStorageFlags keyStorageFlags)
    {
#if NET10_0_OR_GREATER
        return X509CertificateLoader.LoadPkcs12(bytes, password, keyStorageFlags);
#else
        return new X509Certificate2(bytes, password, keyStorageFlags);
#endif
    }

    /// <summary>
    /// The subject alternative name's otherName entries that hold a string,
    /// by type OID: where an MCP Identity Registry puts vessel attributes.
    /// </summary>
    private static Dictionary<string, string> AlternativeNameAttributes(X509Certificate2 certificate)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (certificate.Extensions["2.5.29.17"] is not { } extension)
            return attributes;

        try
        {
            // GeneralNames ::= SEQUENCE OF GeneralName; otherName [0] { type-id OID, value [0] EXPLICIT ANY }.
            var names = new AsnReader(extension.RawData, AsnEncodingRules.DER).ReadSequence();
            while (names.HasData)
            {
                var tag = names.PeekTag();
                if (!tag.HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0)))
                {
                    names.ReadEncodedValue();
                    continue;
                }

                var otherName = names.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                var type = otherName.ReadObjectIdentifier();
                var value = otherName.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
                if (value.HasData && value.PeekTag() is { TagClass: TagClass.Universal } inner
                    && (UniversalTagNumber)inner.TagValue is UniversalTagNumber.UTF8String or UniversalTagNumber.PrintableString or UniversalTagNumber.IA5String)
                {
                    attributes.TryAdd(type, value.ReadCharacterString((UniversalTagNumber)inner.TagValue));
                }
            }
        }
        catch (AsnContentException)
        {
            // A malformed name: no attributes.
        }

        return attributes;
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
