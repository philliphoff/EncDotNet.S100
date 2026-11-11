using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Viewer.Services.Secom;
using EncDotNet.S100.Viewer.ViewModels.Keys;

namespace EncDotNet.S100.Viewer.Tests.Keys;

/// <summary>
/// A throwaway CA that issues MCP-style client identities (P-384, MRN as the
/// subject UID), with the stores and page wired over an in-memory key store
/// and a temporary settings file (#845).
/// </summary>
internal sealed class KeysTestSupport : IDisposable
{
    public const string Mrn = "urn:mrn:mcp:device:mcc:soundcharts:bridge-pc";

    private readonly ECDsa _rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP384);

    public KeysTestSupport(DateTimeOffset? now = null)
    {
        Time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(now ?? new DateTimeOffset(2026, 10, 7, 12, 0, 0, TimeSpan.Zero));
        Folder = Directory.CreateTempSubdirectory("keys-tests-").FullName;
        var request = new CertificateRequest("CN=Example MCP Root Certificate, O=Example MCP", _rootKey, HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        Root = request.CreateSelfSigned(Time.GetUtcNow().AddYears(-1), Time.GetUtcNow().AddYears(5));
        Anchors = new SecomTrustAnchors([new SecomTrustAnchor("Example MCP", Root)]);
        Settings = new ViewerSettings { SettingsFilePath = Path.Combine(Folder, "settings.json") };
        Trust = new SecomServerTrust(Anchors, Time);
        Identities = new SecomIdentityStore(Settings, Trust, Keys);
        Authorities = new TrustedAuthorityStore(Settings, Trust, Anchors);
    }

    public Microsoft.Extensions.Time.Testing.FakeTimeProvider Time { get; }
    public string Folder { get; }
    public X509Certificate2 Root { get; }
    public SecomTrustAnchors Anchors { get; }
    public ViewerSettings Settings { get; }
    public SecomServerTrust Trust { get; }
    public InMemorySecomKeyStore Keys { get; } = new();
    public SecomIdentityStore Identities { get; }
    public TrustedAuthorityStore Authorities { get; }
    public FakeKeysDialogs Dialogs { get; } = new();

    /// <summary>The page over these stores, posting synchronously.</summary>
    public KeysAndCertificatesViewModel Page() => new(Identities, Authorities, Dialogs, Time, post: a => a());

    /// <summary>A PKCS#12 identity file signed by the root; returns its path.</summary>
    public string IdentityFile(string name = "bridge-pc", string mrn = Mrn, int daysLeft = 300, string password = "secret",
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        using var leaf = Leaf(mrn, name, daysLeft, attributes);
        var path = Path.Combine(Folder, name + ".p12");
        File.WriteAllBytes(path, leaf.Export(X509ContentType.Pkcs12, password));
        return path;
    }

    /// <summary>The root as a PEM file; returns its path.</summary>
    public string RootPemFile(X509Certificate2? root = null)
    {
        var path = Path.Combine(Folder, $"root-{Guid.NewGuid():N}.pem");
        File.WriteAllText(path, (root ?? Root).ExportCertificatePem());
        return path;
    }

    /// <summary>A self-signed root with its own name and validity, for authorities added from file.</summary>
    public static X509Certificate2 OtherRoot(string commonName, DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    /// <summary>Where the leaves' CRL is published, when they name one.</summary>
    public const string CrlUri = "http://crl.keys.test/root.crl";

    /// <summary>The root's CRL listing <paramref name="revoked"/>.</summary>
    public byte[] Crl(params X509Certificate2[] revoked)
    {
        var builder = new CertificateRevocationListBuilder();
        foreach (var certificate in revoked)
            builder.AddEntry(certificate, Time.GetUtcNow().AddDays(-2));
        return builder.Build(Root, 1, Time.GetUtcNow().AddDays(7), HashAlgorithmName.SHA384, thisUpdate: Time.GetUtcNow().AddHours(-1));
    }

    public X509Certificate2 Leaf(string mrn, string commonName, int daysLeft, IReadOnlyDictionary<string, string>? attributes = null, bool withCrl = false)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var subject = new X500DistinguishedNameBuilder();
        subject.Add("0.9.2342.19200300.100.1.1", mrn, UniversalTagNumber.UTF8String);
        subject.AddCommonName(commonName);
        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA384);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        if (withCrl)
            request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([CrlUri]));
        if (attributes is { Count: > 0 })
        {
            var writer = new AsnWriter(AsnEncodingRules.DER);
            using (writer.PushSequence())
            {
                foreach (var (oid, value) in attributes)
                {
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                    {
                        writer.WriteObjectIdentifier(oid);
                        using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
                            writer.WriteCharacterString(UniversalTagNumber.UTF8String, value);
                    }
                }
            }

            request.CertificateExtensions.Add(new X509Extension("2.5.29.17", writer.Encode(), false));
        }

        var now = Time.GetUtcNow();
        using var issued = request.Create(Root.SubjectName, X509SignatureGenerator.CreateForECDsa(_rootKey),
            now.AddDays(-30), now.AddDays(daysLeft), RandomNumberGenerator.GetBytes(8));
        return issued.CopyWithPrivateKey(key);
    }

    public void Dispose()
    {
        _rootKey.Dispose();
        Root.Dispose();
        try { Directory.Delete(Folder, recursive: true); } catch (IOException) { }
    }
}

/// <summary>Records what the page asked to show, and answers file pickers.</summary>
internal sealed class FakeKeysDialogs : IKeysDialogs
{
    public Queue<string?> IdentityFiles { get; } = new();
    public Queue<string?> AuthorityFiles { get; } = new();
    public List<object> Shown { get; } = [];
    public List<(string Title, string Message, Action Confirmed)> Confirms { get; } = [];

    public Task<string?> PickIdentityFileAsync() => Task.FromResult(IdentityFiles.Count > 0 ? IdentityFiles.Dequeue() : null);
    public Task<string?> PickAuthorityFileAsync() => Task.FromResult(AuthorityFiles.Count > 0 ? AuthorityFiles.Dequeue() : null);
    public void Show<T>(T dialog, double maxWidth)
        where T : class => Shown.Add(dialog);
    public void Close(object dialog) => Shown.Remove(dialog);
    public void Confirm(string title, string message, string confirmLabel, Action confirmed) => Confirms.Add((title, message, confirmed));
}
