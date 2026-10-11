using System.Security.Cryptography.X509Certificates;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// The client's MCP identity for SECOM (issue #832): loaded from PKCS#12 or
/// PEM, presented by SECOM handlers to services that ask for it (mutual TLS),
/// and reported by probes.
/// </summary>
public sealed class SecomClientIdentityTests(SecomClientIdentityTests.Pkis pkis) : IClassFixture<SecomClientIdentityTests.Pkis>, IDisposable
{
    private const string Mrn = "urn:mrn:mcp:device:mcc:soundcharts:test-vessel";

    private readonly TestPki _pki = pkis.Trusted;
    private readonly TempDirectory _temp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    /// <summary>The CAs and a server certificate, made once per class (slow on macOS).</summary>
    public sealed class Pkis : IDisposable
    {
        public TestPki Trusted { get; } = TestPki.Create();

        public TestPki Other { get; } = TestPki.Create();

        public X509Certificate2 Server => _server ??= Trusted.Leaf(dnsNames: ["localhost"]);

        private X509Certificate2? _server;

        public void Dispose()
        {
            _server?.Dispose();
            Trusted.Dispose();
            Other.Dispose();
        }
    }

    [Fact]
    public void An_identity_loads_from_pkcs12_and_pem_and_reports_its_mrn_and_anchor()
    {
        using var issued = _pki.ClientLeaf(Mrn);

        var p12 = Path.Combine(_temp.Path, "identity.p12");
        File.WriteAllBytes(p12, issued.Export(X509ContentType.Pkcs12, "secret"));
        using var fromP12 = SecomClientIdentity.Load(p12, "secret", _pki.Anchors);
        Assert.Equal(Mrn, fromP12.Mrn);
        Assert.Equal("Test vessel", fromP12.Subject);
        Assert.Equal(Mrn, fromP12.DisplayName);
        Assert.Equal("Test MCP", fromP12.Anchor);
        Assert.True(fromP12.IsValidAt(DateTimeOffset.UtcNow));
        Assert.True(fromP12.Certificate.HasPrivateKey);

        // PEM: certificate and private key in one file, as the MCP portal offers them.
        var pem = Path.Combine(_temp.Path, "identity.pem");
        using (var key = issued.GetECDsaPrivateKey()!)
            File.WriteAllText(pem, issued.ExportCertificatePem() + "\n" + key.ExportPkcs8PrivateKeyPem());
        using var fromPem = SecomClientIdentity.Load(pem, anchors: _pki.Anchors);
        Assert.Equal(Mrn, fromPem.Mrn);

        // Not issued under the anchors: still usable, but no anchor.
        using var foreign = SecomClientIdentity.Load(p12, "secret", pkis.Other.Anchors);
        Assert.Null(foreign.Anchor);
    }

    [Fact]
    public void An_identity_needs_its_private_key_and_the_right_password()
    {
        using var issued = _pki.ClientLeaf(Mrn);

        var certOnly = Path.Combine(_temp.Path, "cert.pem");
        File.WriteAllText(certOnly, issued.ExportCertificatePem());
        Assert.Throws<InvalidDataException>(() => SecomClientIdentity.Load(certOnly));

        var p12 = Path.Combine(_temp.Path, "identity.p12");
        File.WriteAllBytes(p12, issued.Export(X509ContentType.Pkcs12, "secret"));
        Assert.Throws<InvalidDataException>(() => SecomClientIdentity.Load(p12, "wrong"));
        Assert.Throws<FileNotFoundException>(() => SecomClientIdentity.Load(Path.Combine(_temp.Path, "missing.p12")));
    }

    [Fact]
    public async Task Each_request_presents_its_own_identity_and_only_that_one()
    {
        await using var server = await TlsServer.StartAsync(
            pkis.Server, _pki.Intermediate, ClientCertificateMode.ForSummary, _pki.Issued);
        var trust = new SecomServerTrust(_pki.Anchors);
        using var secom = new HttpClient(trust.CreateHandler());
        var summary = server.Uri("/api/secom/v2/object/summary");
        using var identity = Identity(_pki.ClientLeaf(Mrn));
        using var other = Identity(_pki.ClientLeaf("urn:mrn:mcp:device:mcc:soundcharts:bridge-pc", "Bridge PC"));

        // A default is set, but an untagged request presents nothing: the service refuses the summary.
        trust.SetIdentity(identity);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await SendAsync(null)).StatusCode);
        // Windows (Schannel) may not call back when it has no certificate to offer a
        // server that also accepts none, so there the request can go unseen.
        if (!OperatingSystem.IsWindows())
            Assert.True(trust.HandshakeFor("localhost", null)!.CertificateRequested);

        // Tagged with an identity, that one is presented and accepted.
        Assert.True((await SendAsync(identity)).IsSuccessStatusCode);
        Assert.Equal(identity.Certificate.Thumbprint, server.ClientCertificates[^1]?.Thumbprint);
        Assert.True(trust.HandshakeFor("localhost", identity)!.CertificateRequested);

        // Requests with different identities never share a connection.
        Assert.True((await SendAsync(other)).IsSuccessStatusCode);
        Assert.Equal(other.Certificate.Thumbprint, server.ClientCertificates[^1]?.Thumbprint);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await SendAsync(null)).StatusCode);
        Assert.Null(server.ClientCertificates[^1]);

        async Task<HttpResponseMessage> SendAsync(SecomClientIdentity? presented)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, summary);
            SecomServerTrust.PresentIdentity(request, presented);
            return await secom.SendAsync(request, Ct);
        }
    }

    [Fact]
    public async Task A_service_that_does_not_ask_sees_no_certificate_and_no_request_is_recorded()
    {
        await using var server = await TlsServer.StartAsync(pkis.Server, _pki.Intermediate);
        var trust = new SecomServerTrust(_pki.Anchors);
        using var secom = new HttpClient(trust.CreateHandler());
        using var identity = Identity(_pki.ClientLeaf(Mrn));

        using var request = new HttpRequestMessage(HttpMethod.Get, server.Uri("/api/secom/v2/object/summary"));
        SecomServerTrust.PresentIdentity(request, identity);
        Assert.True((await secom.SendAsync(request, Ct)).IsSuccessStatusCode);

        Assert.False(trust.HandshakeFor("localhost", identity)!.CertificateRequested);
        Assert.All(server.ClientCertificates, Assert.Null);
    }

    [Theory]
    [InlineData(ClientCertificateMode.ForSummary)]
    [InlineData(ClientCertificateMode.AtHandshake)]
    public async Task Probes_tell_a_service_readable_with_the_identity_from_one_that_refuses_it(ClientCertificateMode mode)
    {
        await using var server = await TlsServer.StartAsync(pkis.Server, _pki.Intermediate, mode, _pki.Issued);
        var trust = new SecomServerTrust(_pki.Anchors);
        var registry = new SecomRegistry(new HttpClient(trust.CreateHandler()), serverTrust: trust);
        var service = server.Uri("/api/secom");

        // No identity set.
        var anonymous = await registry.ProbeAsync(service, cancellationToken: Ct);
        Assert.Equal(SecomReachability.NeedsCertificate, anonymous.Reachability);
        Assert.True(anonymous.Handshake?.CertificateRequested);

        // An identity the service accepts, as the default.
        using var accepted = Identity(_pki.ClientLeaf(Mrn));
        trust.SetIdentity(accepted);
        var readable = await registry.ProbeAsync(service, cancellationToken: Ct);
        Assert.Equal(SecomReachability.OpenWithCertificate, readable.Reachability);
        Assert.Equal(Mrn, readable.Detail);
        Assert.Equal(SecomIdentitySetting.ReferenceIdOf(accepted.Certificate.Thumbprint), readable.IdentityId);

        // An identity from another registry, chosen by reference: refused, and no other identity is tried.
        using var foreign = Identity(pkis.Other.ClientLeaf("urn:mrn:mcp:device:other:vessel"));
        var foreignId = SecomIdentitySetting.ReferenceIdOf(foreign.Certificate.Thumbprint);
        trust.SetIdentity(foreignId, foreign);
        var before = server.ClientCertificates.Count;
        Assert.Equal(SecomReachability.CertificateRefused, (await registry.ProbeAsync(service, foreignId, Ct)).Reachability);
        Assert.Contains(server.ClientCertificates.Skip(before), c => c?.Thumbprint == foreign.Certificate.Thumbprint);
        Assert.All(server.ClientCertificates.Skip(before), c => Assert.True(c is null || c.Thumbprint == foreign.Certificate.Thumbprint));

        // None: never presented, even with a default set.
        Assert.Equal(SecomReachability.NeedsCertificate, (await registry.ProbeAsync(service, SecomIdentitySetting.None, Ct)).Reachability);
    }

    [Fact]
    public async Task An_open_service_stays_open_with_an_identity_set()
    {
        await using var server = await TlsServer.StartAsync(pkis.Server, _pki.Intermediate);
        var trust = new SecomServerTrust(_pki.Anchors);
        using var identity = Identity(_pki.ClientLeaf(Mrn));
        trust.SetIdentity(identity);
        var registry = new SecomRegistry(new HttpClient(trust.CreateHandler()), serverTrust: trust);

        Assert.Equal(SecomReachability.Open, (await registry.ProbeAsync(server.Uri("/api/secom"), cancellationToken: Ct)).Reachability);
    }

    /// <summary>An identity from a freshly issued certificate, through a PKCS#12 file as a host would load it.</summary>
    private SecomClientIdentity Identity(X509Certificate2 issued)
    {
        using (issued)
        {
            var path = Path.Combine(_temp.Path, $"{Guid.NewGuid():N}.p12");
            File.WriteAllBytes(path, issued.Export(X509ContentType.Pkcs12, "t"));
            return SecomClientIdentity.Load(path, "t", _pki.Anchors);
        }
    }
}
