using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// TLS trust for SECOM requests (issue #829): server certificates issued under
/// a SECOM trust anchor are accepted, against a real TLS handshake with a local
/// server presenting a generated root → intermediate → leaf chain.
/// </summary>
public sealed class SecomServerTrustTests(SecomServerTrustTests.Pkis pkis) : IClassFixture<SecomServerTrustTests.Pkis>
{
    private readonly TestPki _pki = pkis.Trusted;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// The CAs, made once per class: on macOS every signing and key operation
    /// goes through the keychain and takes a few hundred milliseconds.
    /// </summary>
    public sealed class Pkis : IDisposable
    {
        public TestPki Trusted { get; } = TestPki.Create();

        public TestPki Other { get; } = TestPki.Create();

        public void Dispose()
        {
            Trusted.Dispose();
            Other.Dispose();
        }
    }

    [Fact]
    public async Task A_server_certificate_under_an_anchor_is_trusted_for_secom_requests_only()
    {
        var pki = _pki;
        using var leaf = pki.Leaf(dnsNames: ["localhost"]);
        await using var server = await TlsServer.StartAsync(leaf, pki.Intermediate);
        var trust = new SecomServerTrust(pki.Anchors);

        using var secom = new HttpClient(trust.CreateHandler());
        Assert.Equal("ok", await secom.GetStringAsync(server.Uri("/"), Ct));
        Assert.Equal(new SecomServerTrustResult(SecomServerTrustOutcome.AnchorTrusted, "Test MCP"), trust.ResultFor("localhost"));

        // Any other client keeps the system's trust.
        using var plain = new HttpClient();
        await Assert.ThrowsAsync<HttpRequestException>(() => plain.GetStringAsync(server.Uri("/"), Ct));
    }

    [Fact]
    public async Task The_anchors_supply_an_intermediate_the_server_does_not_send()
    {
        var pki = _pki;
        using var leaf = pki.Leaf(dnsNames: ["localhost"]);
        await using var server = await TlsServer.StartAsync(leaf);
        var trust = new SecomServerTrust(pki.Anchors);

        using var secom = new HttpClient(trust.CreateHandler());
        Assert.Equal("ok", await secom.GetStringAsync(server.Uri("/"), Ct));
    }

    [Fact]
    public async Task A_certificate_naming_the_host_only_in_its_common_name_is_trusted()
    {
        // MCP device certificates (AMSA's) carry the MRN as the only alternative name.
        var pki = _pki;
        using var leaf = pki.Leaf(dnsNames: [], commonName: "localhost", mrn: "urn:mrn:mcp:device:mcc:test:secom");
        await using var server = await TlsServer.StartAsync(leaf, pki.Intermediate);
        var trust = new SecomServerTrust(pki.Anchors);

        using var secom = new HttpClient(trust.CreateHandler());
        Assert.Equal("ok", await secom.GetStringAsync(server.Uri("/"), Ct));
    }

    [Theory]
    [InlineData("wrong-host", SecomServerTrustOutcome.WrongHost)]
    [InlineData("cn-but-dns-elsewhere", SecomServerTrustOutcome.WrongHost)]
    [InlineData("expired", SecomServerTrustOutcome.Expired)]
    [InlineData("untrusted-root", SecomServerTrustOutcome.NotTrusted)]
    public async Task Refused_certificates_fail_the_request_and_say_why(string kind, SecomServerTrustOutcome expected)
    {
        var pki = _pki;
        var other = pkis.Other;
        using var leaf = kind switch
        {
            "wrong-host" => pki.Leaf(dnsNames: ["service.example.test"]),
            // A DNS name is listed, so the common name is not consulted.
            "cn-but-dns-elsewhere" => pki.Leaf(dnsNames: ["service.example.test"], commonName: "localhost"),
            "expired" => pki.Leaf(dnsNames: ["localhost"], notAfter: DateTimeOffset.UtcNow.AddDays(-1)),
            _ => other.Leaf(dnsNames: ["localhost"]),
        };
        await using var server = await TlsServer.StartAsync(leaf, kind == "untrusted-root" ? other.Intermediate : pki.Intermediate);
        var trust = new SecomServerTrust(pki.Anchors);

        using var secom = new HttpClient(trust.CreateHandler());
        await Assert.ThrowsAsync<HttpRequestException>(() => secom.GetStringAsync(server.Uri("/"), Ct));
        Assert.Equal(expected, trust.ResultFor("localhost")!.Outcome);
    }

    [Fact]
    public void A_system_trusted_certificate_for_another_host_is_never_widened()
    {
        var pki = _pki;
        using var leaf = pki.Leaf(dnsNames: ["localhost"], withKey: false);
        var trust = new SecomServerTrust(pki.Anchors);

        Assert.True(trust.Validate("localhost", leaf, null, SslPolicyErrors.None).Allowed);
        Assert.Equal(SecomServerTrustOutcome.WrongHost, trust.Validate("localhost", leaf, null, SslPolicyErrors.RemoteCertificateNameMismatch).Outcome);
        Assert.Equal(SecomServerTrustOutcome.NotTrusted, trust.Validate("localhost", null, null, SslPolicyErrors.RemoteCertificateNotAvailable).Outcome);
    }

    [Theory]
    [InlineData("localhost", true)]
    [InlineData("LOCALHOST.", true)]
    [InlineData("a.secom.test", true)]       // *.secom.test
    [InlineData("a.b.secom.test", false)]    // a wildcard covers one label
    [InlineData("secom.test", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.1", false)]
    public void Host_names_match_dns_names_wildcards_and_addresses(string host, bool expected)
    {
        var pki = _pki;
        using var leaf = pki.Leaf(dnsNames: ["localhost", "*.secom.test"], ipAddresses: [IPAddress.Loopback], withKey: false);

        Assert.Equal(expected, SecomServerTrust.NamesHost(leaf, host));
    }

    [Fact]
    public async Task Probes_report_the_server_certificate_decision()
    {
        var pki = _pki;
        using var good = pki.Leaf(dnsNames: ["localhost"]);
        using var wrong = pki.Leaf(dnsNames: ["service.example.test"]);
        await using var open = await TlsServer.StartAsync(good, pki.Intermediate);
        await using var misnamed = await TlsServer.StartAsync(wrong, pki.Intermediate);

        var trust = new SecomServerTrust(pki.Anchors);
        var registry = new SecomRegistry(new HttpClient(trust.CreateHandler()), serverTrust: trust);

        var readable = await registry.ProbeAsync(open.Uri("/api/secom"), Ct);
        Assert.Equal(SecomReachability.Open, readable.Reachability);
        Assert.Equal("Test MCP", readable.ServerTrust?.Anchor);

        // The trust decision is per host, so probe the misnamed server under another name for localhost.
        var refused = await registry.ProbeAsync(misnamed.Uri("/api/secom", host: "127.0.0.1"), Ct);
        Assert.Equal(SecomReachability.UntrustedServer, refused.Reachability);
        Assert.Equal(SecomServerTrustOutcome.WrongHost, refused.ServerTrust?.Outcome);
        Assert.Equal("Its certificate does not name this host.", refused.Detail);
    }

    [Theory]
    [InlineData("not-revoked", SecomRevocationStatus.NotRevoked)]
    [InlineData("crl-unreachable", SecomRevocationStatus.NotChecked)]
    public async Task An_anchor_trusted_server_reports_whether_revocation_was_checked(string kind, SecomRevocationStatus expected)
    {
        // #833: an unreachable CRL soft-fails; the connection is allowed and says so.
        var pki = _pki;
        using var leaf = pki.Leaf(dnsNames: ["localhost"], withCrl: true);
        await using var server = await TlsServer.StartAsync(leaf, pki.Intermediate);
        var crls = Crls(pki);
        crls.Down = kind == "crl-unreachable";
        var trust = new SecomServerTrust(pki.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));

        using var secom = new HttpClient(trust.CreateHandler());
        Assert.Equal("ok", await secom.GetStringAsync(server.Uri("/"), Ct));
        var result = trust.ResultFor("localhost")!;
        Assert.Equal(SecomServerTrustOutcome.AnchorTrusted, result.Outcome);
        Assert.Equal(expected, result.Revocation);
    }

    [Fact]
    public async Task A_revoked_server_certificate_is_refused_and_probes_say_so()
    {
        var pki = _pki;
        using var leaf = pki.Leaf(dnsNames: ["localhost"], withCrl: true);
        await using var server = await TlsServer.StartAsync(leaf, pki.Intermediate);
        var crls = Crls(pki, leaf);
        var trust = new SecomServerTrust(pki.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));

        using var secom = new HttpClient(trust.CreateHandler());
        await Assert.ThrowsAsync<HttpRequestException>(() => secom.GetStringAsync(server.Uri("/"), Ct));
        Assert.Equal(new SecomServerTrustResult(SecomServerTrustOutcome.Revoked, "Test MCP") { Revocation = SecomRevocationStatus.Revoked }, trust.ResultFor("localhost"));

        var registry = new SecomRegistry(new HttpClient(trust.CreateHandler()), serverTrust: trust);
        var probe = await registry.ProbeAsync(server.Uri("/api/secom"), Ct);
        Assert.Equal(SecomReachability.UntrustedServer, probe.Reachability);
        Assert.Equal("Its certificate from Test MCP has been revoked.", probe.Detail);
    }

    [Fact]
    public async Task A_refused_certificate_costs_no_crl_fetch()
    {
        var pki = _pki;
        using var leaf = pki.Leaf(dnsNames: ["service.example.test"], withCrl: true);
        await using var server = await TlsServer.StartAsync(leaf, pki.Intermediate);
        var crls = Crls(pki);
        var trust = new SecomServerTrust(pki.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));

        using var secom = new HttpClient(trust.CreateHandler());
        await Assert.ThrowsAsync<HttpRequestException>(() => secom.GetStringAsync(server.Uri("/"), Ct));
        Assert.Equal(SecomServerTrustOutcome.WrongHost, trust.ResultFor("localhost")!.Outcome);
        Assert.Equal(0, crls.Requests);
    }

    private static SecomRevocationTests.CrlServer Crls(TestPki pki, params X509Certificate2[] revoked)
    {
        var crls = new SecomRevocationTests.CrlServer();
        crls.Crls[TestPki.IntermediateCrlUri.AbsoluteUri] = pki.IntermediateCrl(null, revoked);
        crls.Crls[TestPki.RootCrlUri.AbsoluteUri] = pki.RootCrl();
        return crls;
    }

    /// <summary>A root and intermediate CA, and leaves the intermediate issues.</summary>
    public sealed class TestPki : IDisposable
    {
        private readonly ECDsa _intermediateKey;

        private TestPki(X509Certificate2 root, X509Certificate2 intermediate, ECDsa intermediateKey)
        {
            Root = root;
            Intermediate = intermediate;
            _intermediateKey = intermediateKey;
        }

        public X509Certificate2 Root { get; }

        public X509Certificate2 Intermediate { get; }

        public SecomTrustAnchors Anchors => new([new SecomTrustAnchor("Test MCP", Root)], [Intermediate]);

        /// <summary>Where the intermediate's CRL is published; leaves made with <c>withCrl</c> name it.</summary>
        public static Uri IntermediateCrlUri { get; } = new("http://crl.secom.test/crl/intermediate");

        /// <summary>Where the root's CRL is published; the intermediate names it.</summary>
        public static Uri RootCrlUri { get; } = new("http://crl.secom.test/crl/root");

        /// <summary>A CRL from the intermediate listing <paramref name="revoked"/>.</summary>
        public byte[] IntermediateCrl(DateTimeOffset? nextUpdate = null, params X509Certificate2[] revoked) => Crl(Intermediate, nextUpdate, revoked);

        /// <summary>A CRL from the root listing <paramref name="revoked"/>.</summary>
        public byte[] RootCrl(DateTimeOffset? nextUpdate = null, params X509Certificate2[] revoked) => Crl(Root, nextUpdate, revoked);

        private static byte[] Crl(X509Certificate2 issuer, DateTimeOffset? nextUpdate, X509Certificate2[] revoked)
        {
            var builder = new CertificateRevocationListBuilder();
            foreach (var certificate in revoked)
                builder.AddEntry(certificate, DateTimeOffset.UtcNow.AddDays(-1), X509RevocationReason.KeyCompromise);
            var next = nextUpdate ?? DateTimeOffset.UtcNow.AddDays(7);
            return builder.Build(issuer, 1, next, HashAlgorithmName.SHA256, thisUpdate: next.AddDays(-8));
        }

        public static TestPki Create()
        {
            // Wide enough to hold every leaf, including an expired one.
            var from = DateTimeOffset.UtcNow.AddDays(-60);
            var to = DateTimeOffset.UtcNow.AddDays(60);
            using var rootKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var root = Authority("CN=Test MCP Root Certificate", rootKey).CreateSelfSigned(from, to);

            var intermediateKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var intermediateRequest = Authority("CN=Test MCP Identity Registry", intermediateKey);
            intermediateRequest.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([RootCrlUri.AbsoluteUri]));
            using var intermediate = intermediateRequest.Create(root, from, to, RandomNumberGenerator.GetBytes(8));
            return new TestPki(root, intermediate.CopyWithPrivateKey(intermediateKey), intermediateKey);
        }

        public X509Certificate2 Leaf(
            string[] dnsNames,
            string commonName = "SECOM service",
            string? mrn = null,
            IPAddress[]? ipAddresses = null,
            DateTimeOffset? notAfter = null,
            bool withKey = true,
            bool withCrl = false)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest($"CN={commonName}", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
            request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], false));
            var san = new SubjectAlternativeNameBuilder();
            foreach (var name in dnsNames)
                san.AddDnsName(name);
            foreach (var address in ipAddresses ?? [])
                san.AddIpAddress(address);
            if (mrn is not null)
                san.AddUserPrincipalName(mrn);  // stands in for the MCP MRN othername
            if (dnsNames.Length > 0 || mrn is not null || ipAddresses is { Length: > 0 })
                request.CertificateExtensions.Add(san.Build());
            if (withCrl)
                request.CertificateExtensions.Add(CertificateRevocationListBuilder.BuildCrlDistributionPointExtension([IntermediateCrlUri.AbsoluteUri]));

            var to = notAfter ?? DateTimeOffset.UtcNow.AddDays(10);
            var issued = request.Create(Intermediate, to.AddDays(-20), to, RandomNumberGenerator.GetBytes(8));
            if (!withKey)
                return issued;
            using (issued)
                return issued.CopyWithPrivateKey(key);
        }

        private static CertificateRequest Authority(string name, ECDsa key)
        {
            var request = new CertificateRequest(name, key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
            return request;
        }

        public void Dispose()
        {
            Root.Dispose();
            Intermediate.Dispose();
            _intermediateKey.Dispose();
        }
    }

    /// <summary>
    /// A local HTTPS server on a loopback port: "ok" for <c>/</c>, and minimal
    /// SECOM Capability and GetSummary answers under <c>/api/secom</c>.
    /// </summary>
    private sealed class TlsServer : IAsyncDisposable
    {
        private const string Capability = """{"capability":[{"containerType":0,"dataProductType":"S124","implementedInterfaces":{"get":true,"getSummary":true}}]}""";
        private const string Summary = """{"summaryObject":[],"pagination":{"totalItems":0,"maxItemsPerPage":1}}""";

        private readonly TcpListener _listener;
        private readonly SslStreamCertificateContext _context;
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _loop;

        private readonly X509Certificate2 _certificate;

        private TlsServer(X509Certificate2 certificate, X509Certificate2? intermediate)
        {
            // Round-trip through PKCS#12: macOS's TLS stack needs a persisted key.
            _certificate = X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12, "t"), "t");
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _context = SslStreamCertificateContext.Create(
                _certificate, intermediate is null ? null : new X509Certificate2Collection(intermediate), offline: true);
            _loop = Task.Run(AcceptAsync);
        }

        public static Task<TlsServer> StartAsync(X509Certificate2 certificate, X509Certificate2? intermediate = null) =>
            Task.FromResult(new TlsServer(certificate, intermediate));

        public Uri Uri(string path, string host = "localhost") =>
            new($"https://{host}:{((IPEndPoint)_listener.LocalEndpoint).Port}{path}");

        private async Task AcceptAsync()
        {
            while (!_stop.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
                {
                    return;
                }

                _ = Task.Run(() => ServeAsync(client));
            }
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    await using var ssl = new SslStream(client.GetStream());
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificateContext = _context }, _stop.Token);
                    using var reader = new StreamReader(ssl, Encoding.ASCII, leaveOpen: true);
                    var requestLine = await reader.ReadLineAsync(_stop.Token) ?? string.Empty;
                    while (!string.IsNullOrEmpty(await reader.ReadLineAsync(_stop.Token)))
                    {
                    }

                    var path = requestLine.Split(' ') is [_, var target, ..] ? target.Split('?')[0] : "/";
                    var (type, body) = path == "/" ? ("text/plain", "ok")
                        : path.EndsWith("/capability", StringComparison.Ordinal) ? ("application/json", Capability)
                        : ("application/json", Summary);
                    var bytes = Encoding.UTF8.GetBytes(body);
                    var head = $"HTTP/1.1 200 OK\r\nContent-Type: {type}\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
                    await ssl.WriteAsync(Encoding.ASCII.GetBytes(head), _stop.Token);
                    await ssl.WriteAsync(bytes, _stop.Token);
                    await ssl.FlushAsync(_stop.Token);
                }
                catch (Exception ex) when (ex is IOException or System.Security.Authentication.AuthenticationException or OperationCanceledException or ObjectDisposedException)
                {
                    // The client refused the certificate, or the server is stopping.
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            _listener.Stop();
            await _loop;
            _stop.Dispose();
            _certificate.Dispose();
        }
    }
}
