using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
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
}
