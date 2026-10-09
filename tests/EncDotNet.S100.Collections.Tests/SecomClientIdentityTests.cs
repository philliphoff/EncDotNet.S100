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
    public async Task The_identity_is_presented_to_a_service_that_asks_and_can_change_at_runtime()
    {
        await using var server = await TlsServer.StartAsync(
            pkis.Server, _pki.Intermediate, ClientCertificateMode.ForSummary, _pki.Issued);
        var trust = new SecomServerTrust(_pki.Anchors);
        using var secom = new HttpClient(trust.CreateHandler());
        var summary = server.Uri("/api/secom/v2/object/summary");

        // No identity: the service refuses the summary.
        var anonymous = await secom.GetAsync(summary, Ct);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, anonymous.StatusCode);

        // With one, it is presented and accepted.
        using var identity = Identity(_pki.ClientLeaf(Mrn));
        trust.SetIdentity(identity);
        Assert.True((await secom.GetAsync(summary, Ct)).IsSuccessStatusCode);
        Assert.Contains(server.ClientCertificates, c => c?.Thumbprint == identity.Certificate.Thumbprint);

        // Cleared: no longer presented.
        trust.SetIdentity(null);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await secom.GetAsync(summary, Ct)).StatusCode);

        // A handler that never presents it, and a non-SECOM client, never do.
        trust.SetIdentity(identity);
        using var anonymousOnly = new HttpClient(trust.CreateHandler(presentIdentity: false));
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await anonymousOnly.GetAsync(summary, Ct)).StatusCode);
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
        Assert.Equal(SecomReachability.NeedsCertificate, (await registry.ProbeAsync(service, Ct)).Reachability);

        // An identity the service accepts.
        using var accepted = Identity(_pki.ClientLeaf(Mrn));
        trust.SetIdentity(accepted);
        var readable = await registry.ProbeAsync(service, Ct);
        Assert.Equal(SecomReachability.OpenWithCertificate, readable.Reachability);
        Assert.Equal(Mrn, readable.Detail);

        // An identity from another registry: refused.
        using var foreign = Identity(pkis.Other.ClientLeaf("urn:mrn:mcp:device:other:vessel"));
        trust.SetIdentity(foreign);
        Assert.Equal(SecomReachability.CertificateRefused, (await registry.ProbeAsync(service, Ct)).Reachability);
    }

    [Fact]
    public async Task An_open_service_stays_open_with_an_identity_set()
    {
        await using var server = await TlsServer.StartAsync(pkis.Server, _pki.Intermediate);
        var trust = new SecomServerTrust(_pki.Anchors);
        using var identity = Identity(_pki.ClientLeaf(Mrn));
        trust.SetIdentity(identity);
        var registry = new SecomRegistry(new HttpClient(trust.CreateHandler()), serverTrust: trust);

        Assert.Equal(SecomReachability.Open, (await registry.ProbeAsync(server.Uri("/api/secom"), Ct)).Reachability);
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
