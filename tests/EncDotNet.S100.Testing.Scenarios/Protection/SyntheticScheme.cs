using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EncDotNet.S100.ExchangeSets;
using EncDotNet.S100.ExchangeSets.Protection;

namespace EncDotNet.S100.Testing.Scenarios.Protection;

/// <summary>
/// A TEST ONLY Part 15 scheme derived from a seed: a Scheme Administrator,
/// a data server it certifies, a second Scheme Administrator nobody trusts,
/// a test manufacturer, and the hardware ID of a test system.
/// </summary>
/// <remarks>
/// <para>
/// Every key is a <see cref="DeterministicEcdsa"/> key or a hash of the seed,
/// and every certificate has fixed dates and serial numbers, so the same
/// seed gives the same certificates byte for byte. The keys are computable
/// from public inputs: they protect nothing and must never be trusted outside
/// synthetic test data. Names say "TEST ONLY" and "not real".
/// </para>
/// <para>
/// Certificates are valid from 1 January 2026 to 31 December 2049, except
/// the expired data-server certificate, which ended on 30 June 2026.
/// </para>
/// </remarks>
public sealed class SyntheticScheme : IDisposable
{
    /// <summary>The Scheme Administrator identity written to catalogues and permit signatures.</summary>
    public const string SchemeAdministratorId = "ZZ00-TEST-SA";

    /// <summary>The identifier signatures use for the data-server certificate.</summary>
    public const string DataServerCertificateId = "ZZ00-TEST-DATA-SERVER";

    /// <summary>The test manufacturer ID (<c>M_ID</c>).</summary>
    public const string ManufacturerId = "ZZTEST";

    private static readonly DateTimeOffset ValidFrom = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ValidTo = new(2049, 12, 31, 23, 59, 59, TimeSpan.Zero);
    private static readonly DateTimeOffset ExpiredOn = new(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);

    private readonly DeterministicEcdsa _saKey;
    private readonly DeterministicEcdsa _otherSaKey;
    private readonly DeterministicEcdsa _serverKey;

    /// <summary>Derives the scheme for a seed.</summary>
    /// <param name="seed">The seed.</param>
    public SyntheticScheme(ulong seed)
    {
        Seed = seed;
        _saKey = DeterministicEcdsa.Derive(seed, "scheme-administrator");
        _otherSaKey = DeterministicEcdsa.Derive(seed, "other-scheme-administrator");
        _serverKey = DeterministicEcdsa.Derive(seed, "data-server");

        SchemeAdministrator = SelfSigned(_saKey, Name("ZZ00 TEST ONLY Scheme Administrator (not real)"), 1);
        OtherSchemeAdministrator = SelfSigned(_otherSaKey, Name("ZZ00 TEST ONLY Other Scheme Administrator (not real; untrusted)"), 2);
        DataServer = Issue(_serverKey, Name("ZZ00 TEST ONLY Data Server (not real)"), SchemeAdministrator, _saKey, 3, ValidTo);
        DataServerFromOtherScheme = Issue(_serverKey, Name("ZZ00 TEST ONLY Data Server (not real)"), OtherSchemeAdministrator, _otherSaKey, 4, ValidTo);
        ExpiredDataServer = Issue(_serverKey, Name("ZZ00 TEST ONLY Data Server (not real; expired)"), SchemeAdministrator, _saKey, 5, ExpiredOn);

        ManufacturerKey = Derive16("manufacturer-key");
        HardwareId = HardwareId.FromBytes(Derive16("hardware-id"));
        OtherHardwareId = HardwareId.FromBytes(Derive16("other-hardware-id"));
        UserPermit = UserPermit.Create(HardwareId, ManufacturerKey, ManufacturerId);
    }

    /// <summary>The seed.</summary>
    public ulong Seed { get; }

    /// <summary>The Scheme Administrator certificate to trust.</summary>
    public X509Certificate2 SchemeAdministrator { get; }

    /// <summary>A second Scheme Administrator, which tests don't trust.</summary>
    public X509Certificate2 OtherSchemeAdministrator { get; }

    /// <summary>The data server's certificate, issued by <see cref="SchemeAdministrator"/>.</summary>
    public X509Certificate2 DataServer { get; }

    /// <summary>The same data-server key, certified by <see cref="OtherSchemeAdministrator"/>.</summary>
    public X509Certificate2 DataServerFromOtherScheme { get; }

    /// <summary>The data-server key with a certificate that expired on 30 June 2026.</summary>
    public X509Certificate2 ExpiredDataServer { get; }

    /// <summary>The test manufacturer key (<c>M_KEY</c>).</summary>
    public byte[] ManufacturerKey { get; }

    /// <summary>The test system's hardware ID; permits are issued for it.</summary>
    public HardwareId HardwareId { get; }

    /// <summary>Another system's hardware ID, for keys wrapped for the wrong system.</summary>
    public HardwareId OtherHardwareId { get; }

    /// <summary>The test system's user permit (<see cref="HardwareId"/> under <see cref="ManufacturerKey"/>).</summary>
    public UserPermit UserPermit { get; }

    /// <summary>Trust anchors that trust <see cref="SchemeAdministrator"/> only.</summary>
    public TrustAnchorOptions TrustAnchors => new() { TrustedRoots = [SchemeAdministrator] };

    /// <summary>A signer with the data-server key and the given certificate.</summary>
    /// <param name="certificate">One of the data-server certificates.</param>
    /// <returns>The signer; dispose it after use.</returns>
    public Part15Signer CreateSigner(X509Certificate2 certificate) =>
        new(certificate, _serverKey, DataServerCertificateId, SchemeAdministratorId);

    /// <summary>A 16-byte value derived from the seed, such as a cell key.</summary>
    /// <param name="name">What the value is for.</param>
    /// <returns>The bytes.</returns>
    public byte[] Derive16(string name) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"ZZ00 TEST ONLY|{Seed}|{name}"))[..16];

    /// <inheritdoc />
    public void Dispose()
    {
        SchemeAdministrator.Dispose();
        OtherSchemeAdministrator.Dispose();
        DataServer.Dispose();
        DataServerFromOtherScheme.Dispose();
        ExpiredDataServer.Dispose();
        _saKey.Dispose();
        _otherSaKey.Dispose();
        _serverKey.Dispose();
    }

    private static X509Certificate2 SelfSigned(DeterministicEcdsa key, X500DistinguishedName name, byte serial)
    {
        var request = new CertificateRequest(name, key, HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign | X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        return request.Create(name, X509SignatureGenerator.CreateForECDsa(key), ValidFrom, ValidTo, Serial(serial));
    }

    private static X509Certificate2 Issue(DeterministicEcdsa key, X500DistinguishedName subject, X509Certificate2 issuer, DeterministicEcdsa issuerKey, byte serial, DateTimeOffset notAfter)
    {
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        request.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(issuer, includeKeyIdentifier: true, includeIssuerAndSerial: false));
        return request.Create(issuer.SubjectName, X509SignatureGenerator.CreateForECDsa(issuerKey), ValidFrom.AddMinutes(1), notAfter, Serial(serial));
    }

    private static X500DistinguishedName Name(string commonName)
    {
        var builder = new X500DistinguishedNameBuilder();
        builder.AddOrganizationName("Synthetic Hydrographic Office (fictitious)");
        builder.AddCommonName(commonName);
        return builder.Build();
    }

    // Serial numbers 0x5A5A0000…0n: positive, fixed, and obviously made up.
    private static byte[] Serial(byte n) => [0x5A, 0x5A, 0, 0, 0, 0, 0, n];
}
