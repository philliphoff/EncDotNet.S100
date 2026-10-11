using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Collections.Persistence;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>A SECOM source's identity setting, its resolution, and the picker's hints (#883).</summary>
public sealed class SecomIdentitySettingTests
{
    private static readonly DateTimeOffset From = DateTimeOffset.UtcNow.AddDays(-2);
    private static readonly DateTimeOffset To = DateTimeOffset.UtcNow.AddDays(30);

    [Theory]
    [InlineData("default", "default")]
    [InlineData(" None ", "none")]
    [InlineData("sc-ident:91D0B3C5", "sc-ident:91d0b3c5")]
    [InlineData("sc-ident:", null)]
    [InlineData("sc-ident:not-hex", null)]
    [InlineData("/tmp/identity.p12", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Settings_are_read_in_canonical_form(string? value, string? expected)
    {
        Assert.Equal(expected is not null, SecomIdentitySetting.TryParse(value, out var setting));
        Assert.Equal(expected, setting);
    }

    [Fact]
    public void A_source_saved_before_the_setting_existed_follows_the_default()
    {
        var saved = new SecomSource(Guid.NewGuid(), null, new Uri("https://secom.test/api/secom/"), SecomFilter.All);
        var current = CollectionJson.SerializeStore(new CollectionStoreDocument(1, [new DatasetCollection(Guid.NewGuid(), "SECOM", [saved], DateTimeOffset.UtcNow)]));
        Assert.Contains("\"identity\": \"default\"", current, StringComparison.Ordinal);
        var json = System.Text.RegularExpressions.Regex.Replace(current, "\\s*\"identity\": \"default\",?", string.Empty);
        Assert.DoesNotContain("identity", json, StringComparison.Ordinal);

        var source = Assert.IsType<SecomSource>(CollectionJson.DeserializeStore(json).Collections[0].Sources[0]);
        Assert.Equal(SecomIdentitySetting.Default, source.Identity);

        // It is saved, and reads back unchanged.
        var chosen = source with { Identity = "sc-ident:91d0b3c5" };
        var written = CollectionJson.SerializeStore(new CollectionStoreDocument(1, [new DatasetCollection(Guid.NewGuid(), "SECOM", [chosen], DateTimeOffset.UtcNow)]));
        Assert.Contains("\"identity\": \"sc-ident:91d0b3c5\"", written, StringComparison.Ordinal);
        Assert.Equal("sc-ident:91d0b3c5", ((SecomSource)CollectionJson.DeserializeStore(written).Collections[0].Sources[0]).Identity);
    }

    [Fact]
    public void Settings_resolve_to_the_default_none_or_the_named_identity()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var identity = Identity("CN=Bridge PC");
        var id = SecomIdentitySetting.ReferenceIdOf(identity.Certificate.Thumbprint);

        Assert.Equal(new SecomResolvedIdentity("default", null, null), trust.Resolve(SecomIdentitySetting.Default));
        trust.SetIdentity(id, identity);
        Assert.Null(trust.Resolve(null).Identity);

        trust.DefaultIdentityId = id;
        Assert.Same(identity, trust.Resolve(null).Identity);
        Assert.Same(identity, trust.Resolve("default").Identity);
        Assert.Same(identity, trust.Resolve(id.ToUpperInvariant()).Identity);
        Assert.Same(SecomResolvedIdentity.None, trust.Resolve("none"));
        Assert.Null(trust.Resolve("garbage").Identity);
        Assert.True(trust.Resolve("sc-ident:00000000").IsMissing);
    }

    [Fact]
    public void Identity_changes_say_what_changed()
    {
        var trust = new SecomServerTrust(SecomTrustAnchors.None);
        using var first = Identity("CN=First");
        using var second = Identity("CN=Second");
        var changes = new List<(SecomIdentityChange, string?, string?)>();
        trust.IdentitiesChanged += (_, e) => changes.Add((e.Change, e.Id, e.DefaultId));

        trust.SetIdentity("sc-ident:aaaaaaaa", first);
        trust.DefaultIdentityId = "sc-ident:aaaaaaaa";
        trust.DefaultIdentityId = "sc-ident:aaaaaaaa";  // unchanged: nothing raised
        trust.SetIdentity("sc-ident:aaaaaaaa", second);
        trust.SetIdentity("sc-ident:aaaaaaaa", null);
        trust.SetIdentity("sc-ident:aaaaaaaa", null);   // already gone

        Assert.Equal(
            [
                (SecomIdentityChange.Added, "sc-ident:aaaaaaaa", null),
                (SecomIdentityChange.DefaultChanged, "sc-ident:aaaaaaaa", null),
                (SecomIdentityChange.Replaced, "sc-ident:aaaaaaaa", "sc-ident:aaaaaaaa"),
                (SecomIdentityChange.Removed, "sc-ident:aaaaaaaa", "sc-ident:aaaaaaaa"),
            ],
            changes);
    }

    [Fact]
    public void An_identity_is_accepted_when_its_chain_holds_an_issuer_the_server_named()
    {
        using var root = Authority("CN=Test MCP Root");
        using var intermediate = Authority("CN=Test MCP Identity Registry", root);
        using var leaf = Leaf("urn:mrn:mcp:vessel:mcc:soundcharts:bridge", intermediate);
        var anchors = new SecomTrustAnchors([new SecomTrustAnchor("Test MCP", root)], [intermediate]);
        using var identity = SecomClientIdentity.FromCertificate(leaf, anchors);

        Assert.Equal(SecomIdentityHint.IssuerAccepted, Judge(identity, anchors, ["CN=Test MCP Identity Registry"]));
        Assert.Equal(SecomIdentityHint.IssuerAccepted, Judge(identity, anchors, ["CN=Test MCP Root"]));
        Assert.Equal(SecomIdentityHint.IssuerNotAccepted, Judge(identity, anchors, ["CN=Another Registry"]));
        Assert.Equal(SecomIdentityHint.RefusedHere, Judge(identity, anchors, ["CN=Test MCP Root"], refused: ["sc-ident:12345678"]));
    }

    [Fact]
    public void Without_a_list_the_mcp_instance_decides()
    {
        using var leaf = Leaf("urn:mrn:mcp:vessel:mcc:soundcharts:bridge");
        using var identity = SecomClientIdentity.FromCertificate(leaf, SecomTrustAnchors.None);

        // The service's MRN, when known.
        Assert.Equal(SecomIdentityHint.IssuerAccepted, Judge(identity, SecomTrustAnchors.None, [], serviceMrn: "urn:mrn:mcp:service:mcc:khra:s-124"));
        Assert.Equal(SecomIdentityHint.IssuerNotAccepted, Judge(identity, SecomTrustAnchors.None, [], serviceMrn: "urn:mrn:mcp:service:mcc-test:khra:s-124"));
        // Else the server certificate's anchor: the identity has none here, so it is another instance.
        Assert.Equal(SecomIdentityHint.IssuerNotAccepted, Judge(identity, SecomTrustAnchors.None, [], serverAnchor: "MCP MCC"));
        // A server under a public CA says nothing about MCP instances.
        Assert.Equal(SecomIdentityHint.None, Judge(identity, SecomTrustAnchors.None, []));
        Assert.Equal("mcc-test", SecomIdentityHints.Instance("urn:mrn:mcp:device:mcc-test:soundcharts:dev"));
        Assert.Null(SecomIdentityHints.Instance("urn:mrn:iho:…"));
    }

    private static SecomIdentityHint Judge(
        SecomClientIdentity identity, SecomTrustAnchors anchors, string[] issuers,
        string? serverAnchor = null, string[]? refused = null, string? serviceMrn = null) =>
        SecomIdentityHints.Judge("sc-ident:12345678", identity, anchors, issuers, serverAnchor, refused ?? [], serviceMrn);

    private static SecomClientIdentity Identity(string subject)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return SecomClientIdentity.FromCertificate(
            new CertificateRequest(subject, key, HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)),
            SecomTrustAnchors.None);
    }

    private static X509Certificate2 Authority(string subject, X509Certificate2? issuer = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        var (from, to) = (From, To);
        if (issuer is null)
            return request.CreateSelfSigned(from, to);
        using var issued = request.Create(issuer, from, to, RandomNumberGenerator.GetBytes(8));
        return issued.CopyWithPrivateKey(key);
    }

    private static X509Certificate2 Leaf(string mrn, X509Certificate2? issuer = null)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var subject = new X500DistinguishedNameBuilder();
        subject.Add("0.9.2342.19200300.100.1.1", mrn, System.Formats.Asn1.UniversalTagNumber.UTF8String);
        subject.AddCommonName("Bridge PC");
        var request = new CertificateRequest(subject.Build(), key, HashAlgorithmName.SHA256);
        var (from, to) = (From.AddDays(1), To.AddDays(-1));
        if (issuer is null)
            return request.CreateSelfSigned(from, to);
        using var issued = request.Create(issuer, from, to, RandomNumberGenerator.GetBytes(8));
        return issued.CopyWithPrivateKey(key);
    }
}
