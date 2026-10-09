using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// What a host's identity store needs from the library (#845): vessel
/// attributes and issuer for the import check, a revoked identity in use
/// cleared at once, and trust anchors replaced when the user turns a root off
/// or adds one.
/// </summary>
public sealed class SecomIdentityLifecycleTests(SecomClientIdentityTests.Pkis pkis) : IClassFixture<SecomClientIdentityTests.Pkis>, IDisposable
{
    private const string Mrn = "urn:mrn:mcp:device:mcc:soundcharts:bridge-pc";

    private readonly TestPki _pki = pkis.Trusted;
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void An_identity_reports_its_issuer_and_the_vessel_attributes_it_carries()
    {
        using var issued = _pki.ClientLeaf(Mrn, "Bridge PC", attributes: new Dictionary<string, string>
        {
            ["2.25.291283622413876360871493815653100799259"] = "9876543",
            ["2.25.328433707816814908768060331477217690907"] = "440123456",
            ["2.25.323100633285601570573910217875371967771"] = "KR",
            ["2.25.208070283325144527098121348946972755227"] = "D7AB",
        });

        using var identity = SecomClientIdentity.FromCertificate(issued, _pki.Anchors);

        Assert.Equal("Test MCP Identity Registry", identity.Issuer);
        Assert.Equal("9876543", identity.ImoNumber);
        Assert.Equal("440123456", identity.Mmsi);
        Assert.Equal("KR", identity.FlagState);
        Assert.Equal("D7AB", identity.CallSign);
    }

    [Fact]
    public void An_identity_without_vessel_attributes_reports_none()
    {
        using var issued = _pki.ClientLeaf(Mrn);
        using var identity = SecomClientIdentity.FromCertificate(issued, _pki.Anchors);

        Assert.Null(identity.ImoNumber);
        Assert.Null(identity.Mmsi);
        Assert.Null(identity.FlagState);
    }

    [Fact]
    public void An_identity_loads_with_an_exportable_key_for_a_key_store()
    {
        using var issued = _pki.ClientLeaf(Mrn);
        var p12 = Path.Combine(_temp.Path, "identity.p12");
        File.WriteAllBytes(p12, issued.Export(X509ContentType.Pkcs12, "secret"));

        using var identity = SecomClientIdentity.Load(p12, "secret", _pki.Anchors, X509KeyStorageFlags.Exportable);

        Assert.NotEmpty(identity.Certificate.Export(X509ContentType.Pkcs12, "again"));
    }

    [Fact]
    public void A_revoked_identity_in_use_is_cleared_at_once()
    {
        using var issued = _pki.ClientLeaf(Mrn, withCrl: true);
        var crls = new SecomRevocationTests.CrlServer();
        crls.Crls[TestPki.IntermediateCrlUri.AbsoluteUri] = _pki.IntermediateCrl(null, issued);
        crls.Crls[TestPki.RootCrlUri.AbsoluteUri] = _pki.RootCrl();
        var trust = new SecomServerTrust(_pki.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));
        using var identity = SecomClientIdentity.FromCertificate(issued, _pki.Anchors);
        trust.SetIdentity(identity);
        SecomIdentityRevokedEventArgs? revoked = null;
        var changes = 0;
        trust.IdentityRevoked += (_, e) => revoked = e;
        trust.IdentityChanged += (_, _) => changes++;

        var result = trust.CheckIdentityRevocation();

        Assert.Equal(SecomRevocationStatus.Revoked, result.Status);
        Assert.NotNull(result.RevokedAt);
        Assert.Null(trust.Identity);
        Assert.Same(identity, revoked?.Identity);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void An_identity_not_revoked_stays_in_use()
    {
        using var issued = _pki.ClientLeaf(Mrn, withCrl: true);
        var crls = new SecomRevocationTests.CrlServer();
        crls.Crls[TestPki.IntermediateCrlUri.AbsoluteUri] = _pki.IntermediateCrl();
        crls.Crls[TestPki.RootCrlUri.AbsoluteUri] = _pki.RootCrl();
        var trust = new SecomServerTrust(_pki.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));
        using var identity = SecomClientIdentity.FromCertificate(issued, _pki.Anchors);
        trust.SetIdentity(identity);

        Assert.Equal(SecomRevocationStatus.NotRevoked, trust.CheckIdentityRevocation().Status);
        Assert.Same(identity, trust.Identity);
    }

    [Fact]
    public void Replacing_the_anchors_forgets_earlier_decisions_and_is_announced()
    {
        var trust = new SecomServerTrust(_pki.Anchors);
        using var server = _pki.Leaf(dnsNames: ["service.secom.test"]);
        using var chain = new X509Chain();
        trust.Validate("service.secom.test", server, chain, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors);
        Assert.NotNull(trust.ResultFor("service.secom.test"));
        var announced = 0;
        trust.AnchorsChanged += (_, _) => announced++;

        trust.Anchors = SecomTrustAnchors.None;

        Assert.Same(SecomTrustAnchors.None, trust.Anchors);
        Assert.Null(trust.ResultFor("service.secom.test"));
        Assert.Equal(1, announced);
        Assert.False(trust.Validate("service.secom.test", server, chain, System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors).Allowed);
    }
}
