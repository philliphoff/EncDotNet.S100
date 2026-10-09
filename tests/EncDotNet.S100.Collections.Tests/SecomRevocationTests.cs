using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json.Nodes;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Secom;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// Revocation checks for SECOM signers against the CRLs their CAs publish
/// (issue #833), with a generated root → intermediate → leaf PKI whose CRLs are
/// served by a fake handler. TLS servers are covered in <see cref="SecomServerTrustTests"/>.
/// </summary>
public sealed class SecomRevocationTests(SecomServerTrustTests.Pkis pkis) : IClassFixture<SecomServerTrustTests.Pkis>, IDisposable
{
    private readonly TestPki _pki = pkis.Trusted;
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_mcp_crl_is_read_as_pem_and_verified_against_the_built_in_intermediate()
    {
        // Captured from MCP MCC on 2026-10-09: PEM, not DER, as MCP serves it.
        var crl = SecomRevocation.Crl.Parse(File.ReadAllBytes(TestPaths.Fixture("mcp-idreg-new-crl.pem")));
        var intermediate = SecomTrustAnchors.BuiltIn.Intermediates.Single(c => c.Subject.Contains("MCP Identity Registry", StringComparison.Ordinal));

        Assert.True(crl.IsFrom(intermediate));
        Assert.False(crl.IsFrom(SecomTrustAnchors.BuiltIn.Roots[0].Certificate));
        Assert.Equal(133, crl.Revoked.Count);
        Assert.Equal(TimeSpan.FromDays(7), crl.NextUpdate - crl.ThisUpdate);
        Assert.Contains("7D6AE37AC8E7D5C69C7B5F5111A5989E109F08C3", crl.Revoked.Keys);
        Assert.DoesNotContain("685E92B6066818FCA5978377D60CC16145736FAB", crl.Revoked.Keys);  // CCG's signer

        // The CCG signer names this CRL.
        using var ccg = CcgSigner();
        Assert.Equal(
            "http://api.maritimeconnectivity.net/x509/api/certificates/crl/urn:mrn:mcp:ca:mcc:mcp-idreg-new",
            Assert.Single(SecomRevocation.DistributionPoints(ccg)).AbsoluteUri);
    }

    [Fact]
    public void Crls_are_read_as_der_or_pem()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withKey: false, withCrl: true);
        var der = _pki.IntermediateCrl(null, leaf);
        var pem = Encoding.ASCII.GetBytes(PemEncoding.WriteString("X509 CRL", der));

        foreach (var bytes in new[] { der, pem })
        {
            var crl = SecomRevocation.Crl.Parse(bytes);
            Assert.True(crl.IsFrom(_pki.Intermediate));
            Assert.Single(crl.Revoked);
        }

        Assert.Throws<InvalidDataException>(() => SecomRevocation.Crl.Parse("not a crl"u8.ToArray()));
    }

    [Fact]
    public void A_signer_on_no_crl_is_not_revoked_and_reads_as_fully_checked()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withCrl: true);
        var crls = Crls();
        var (data, metadata) = Signed(leaf);

        var check = SecomSignatureVerifier.Verify(data, metadata, _pki.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));

        Assert.Equal(SecomRevocationStatus.NotRevoked, check.SignerRevocation);
        Assert.Equal("valid · trusted (Test MCP)", SecomSourceIndexer.Describe(check));
        Assert.Equal(2, crls.Requests);  // the intermediate's CRL and the root's
    }

    [Fact]
    public void A_revoked_signer_is_shown_as_revoked_apart_from_trust_and_expiry()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withCrl: true);
        var crls = Crls(revoked: leaf);
        var (data, metadata) = Signed(leaf);

        var check = SecomSignatureVerifier.Verify(data, metadata, _pki.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));

        Assert.Equal(SecomSignatureStatus.Valid, check.Status);
        Assert.True(check.SignerTrusted);
        Assert.Equal(SecomRevocationStatus.Revoked, check.SignerRevocation);
        Assert.Equal("valid · trusted (Test MCP) · signer certificate revoked", SecomSourceIndexer.Describe(check));
        Assert.Equal(
            "valid · trusted (Test MCP) · signer certificate revoked · signer certificate expired",
            SecomSourceIndexer.Describe(check with { SignerExpired = true }));
    }

    [Fact]
    public void A_revoked_intermediate_revokes_the_signers_under_it()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withCrl: true);
        var crls = Crls();
        crls.Crls[TestPki.RootCrlUri.AbsoluteUri] = _pki.RootCrl(null, _pki.Intermediate);
        var (data, metadata) = Signed(leaf);

        var check = SecomSignatureVerifier.Verify(data, metadata, _pki.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));

        Assert.Equal(SecomRevocationStatus.Revoked, check.SignerRevocation);
    }

    [Fact]
    public void Revocation_is_checked_only_for_trusted_signers()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withCrl: true);
        var crls = Crls(revoked: leaf);
        var (data, metadata) = Signed(leaf);

        var untrusted = SecomSignatureVerifier.Verify(data, metadata, pkis.Other.Anchors, revocation: new SecomRevocation(new HttpClient(crls)));

        Assert.False(untrusted.SignerTrusted);
        Assert.Equal(SecomRevocationStatus.NotChecked, untrusted.SignerRevocation);
        Assert.Equal("valid · signer not trusted", SecomSourceIndexer.Describe(untrusted));
        Assert.Equal(0, crls.Requests);
    }

    [Fact]
    public void An_unreachable_crl_soft_fails_and_is_not_asked_again_at_once()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withCrl: true);
        var crls = Crls();
        crls.Down = true;
        var revocation = new SecomRevocation(new HttpClient(crls));
        var (data, metadata) = Signed(leaf);

        var check = SecomSignatureVerifier.Verify(data, metadata, _pki.Anchors, revocation: revocation);
        Assert.Equal(SecomRevocationStatus.NotChecked, check.SignerRevocation);
        Assert.Equal("valid · trusted (Test MCP) · revocation not checked", SecomSourceIndexer.Describe(check));
        var asked = crls.Requests;

        // Within the back-off, the responder is not asked again, even once it is back.
        crls.Down = false;
        Assert.Equal(SecomRevocationStatus.NotChecked, SecomSignatureVerifier.Verify(data, metadata, _pki.Anchors, revocation: revocation).SignerRevocation);
        Assert.Equal(asked, crls.Requests);

        var detail = revocation.Check([leaf, _pki.Intermediate, _pki.Root]).Detail;
        Assert.Contains("Connection refused", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void A_slow_crl_responder_is_abandoned_after_the_timeout()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withKey: false, withCrl: true);
        var crls = Crls();
        crls.Delay = TimeSpan.FromSeconds(30);
        var revocation = new SecomRevocation(new HttpClient(crls)) { FetchTimeout = TimeSpan.FromMilliseconds(200) };

        var watch = Stopwatch.StartNew();
        var result = revocation.Check([leaf, _pki.Intermediate, _pki.Root]);

        Assert.Equal(SecomRevocationStatus.NotChecked, result.Status);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed}");
    }

    [Theory]
    [InlineData("stale")]
    [InlineData("wrong-issuer")]
    [InlineData("not-found")]
    public void A_crl_that_cannot_be_relied_on_is_not_used(string kind)
    {
        using var leaf = _pki.Leaf(dnsNames: [], withKey: false, withCrl: true);
        var crls = Crls();
        var key = TestPki.IntermediateCrlUri.AbsoluteUri;
        switch (kind)
        {
            case "stale":
                crls.Crls[key] = _pki.IntermediateCrl(DateTimeOffset.UtcNow.AddDays(-1));
                break;
            case "wrong-issuer":
                crls.Crls[key] = pkis.Other.IntermediateCrl();
                break;
            default:
                crls.Crls.Remove(key);
                break;
        }

        var result = new SecomRevocation(new HttpClient(crls)).Check([leaf, _pki.Intermediate, _pki.Root]);

        Assert.Equal(SecomRevocationStatus.NotChecked, result.Status);
    }

    [Fact]
    public void Crls_are_kept_on_disk_and_cache_only_checks_never_fetch()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withKey: false, withCrl: true);
        var chain = new[] { leaf, _pki.Intermediate, _pki.Root };
        var cache = Path.Combine(_temp.Path, "cache");

        var cold = Crls();
        Assert.Equal(SecomRevocationStatus.NotChecked, new SecomRevocation(new HttpClient(cold), cache).CacheOnly.Check(chain).Status);
        Assert.Equal(0, cold.Requests);

        Assert.Equal(SecomRevocationStatus.NotRevoked, new SecomRevocation(new HttpClient(cold), cache).Check(chain).Status);
        Assert.Equal(2, Directory.GetFiles(cache, "crl-*.crl").Length);

        // A later run reads the disk copies, offline.
        var down = Crls();
        down.Down = true;
        var later = new SecomRevocation(new HttpClient(down), cache);
        Assert.Equal(SecomRevocationStatus.NotRevoked, later.CacheOnly.Check(chain).Status);
        Assert.Equal(SecomRevocationStatus.NotRevoked, later.Check(chain).Status);
        Assert.Equal(0, down.Requests);
    }

    [Fact]
    public async Task Recorded_downloads_are_checked_again_on_read_and_stay_revoked()
    {
        using var leaf = _pki.Leaf(dnsNames: [], withCrl: true);
        var data = Encoding.UTF8.GetBytes("<S124:Dataset/>");
        var (_, metadata) = Signed(leaf, data);
        var server = new SecomTests.FakeSecomServer([SecomTests.Summary(1, "S-124")]);
        server.Objects["ref-0001"] = (data, metadata);
        var item = (await CollectionIndexer.CreateDefault(feeds: [new SecomSourceIndexer(new HttpClient(server))])
            .IndexAsync(new SecomSource(Guid.NewGuid(), null, new Uri("https://secom.test/api/secom"), SecomFilter.All), cancellationToken: Ct)).Items.Single();
        var root = Path.Combine(_temp.Path, "downloads");

        // Downloaded while the CRL was unreachable: the signature is accepted, revocation not checked.
        var down = Crls();
        down.Down = true;
        var downloader = new EncCellDownloader(new HttpClient(server), root) { TrustAnchors = _pki.Anchors, Revocation = new SecomRevocation(new HttpClient(down)) };
        var downloaded = await downloader.DownloadAsync(item, cancellationToken: Ct);
        Assert.Equal(SecomRevocationStatus.NotChecked, downloaded.Signature!.SignerRevocation);

        // Reading never fetches; an index run may, and then finds the signer revoked.
        var revoked = Crls(revoked: leaf);
        var reader = new EncCellDownloader(new HttpClient(server), root) { TrustAnchors = _pki.Anchors, Revocation = new SecomRevocation(new HttpClient(revoked)) };
        Assert.Equal(SecomRevocationStatus.NotChecked, reader.TryGetDownloaded(item.Name)!.Signature!.SignerRevocation);
        Assert.Equal(0, revoked.Requests);
        Assert.Equal(SecomRevocationStatus.Revoked, reader.TryGetDownloaded(item.Name, fetchRevocation: true)!.Signature!.SignerRevocation);
        Assert.Equal(SecomRevocationStatus.Revoked, reader.TryGetDownloaded(item.Name)!.Signature!.SignerRevocation);

        // A download made when the CRL listed the signer is recorded as revoked, and stays so without a CRL.
        var again = new EncCellDownloader(new HttpClient(server), root) { TrustAnchors = _pki.Anchors, Revocation = new SecomRevocation(new HttpClient(Crls(revoked: leaf))) };
        await again.DownloadAsync(item, cancellationToken: Ct);
        var record = JsonNode.Parse(File.ReadAllText(Path.Combine(root, item.Name, EncCellDownloader.RecordFileName)))!;
        Assert.Equal("Revoked", record["signature"]!["revocation"]!.GetValue<string>());
        var offline = new EncCellDownloader(new HttpClient(server), root) { TrustAnchors = _pki.Anchors, Revocation = new SecomRevocation(new HttpClient(down)) };
        Assert.Equal(SecomRevocationStatus.Revoked, offline.TryGetDownloaded(item.Name, fetchRevocation: true)!.Signature!.SignerRevocation);
    }

    /// <summary>A CRL server holding a current CRL from each CA, the intermediate's listing <paramref name="revoked"/>.</summary>
    private CrlServer Crls(params X509Certificate2[] revoked)
    {
        var server = new CrlServer();
        server.Crls[TestPki.IntermediateCrlUri.AbsoluteUri] = _pki.IntermediateCrl(null, revoked);
        server.Crls[TestPki.RootCrlUri.AbsoluteUri] = _pki.RootCrl();
        return server;
    }

    private static (byte[] Data, SecomExchangeMetadata Metadata) Signed(X509Certificate2 signer, byte[]? data = null)
    {
        data ??= "<S124:Dataset/>"u8.ToArray();
        using var key = signer.GetECDsaPrivateKey()!;
        return (data, new SecomExchangeMetadata(
            false,
            "SECOM",
            "ecdsa-256-sha2-256",
            false,
            [Convert.ToBase64String(signer.RawData)],
            null,
            Convert.ToHexString(key.SignHash(SHA256.HashData(data), DSASignatureFormat.Rfc3279DerSequence))));
    }

    private static X509Certificate2 CcgSigner()
    {
        var fixture = JsonNode.Parse(File.ReadAllText(TestPaths.Fixture("secom-ccg-get.json")))!;
        var certificate = fixture["dataResponseObject"]![0]!["exchangeMetadata"]!["digitalSignatureValue"]!["publicCertificate"]!;
        return SecomSignatureVerifier.LoadCertificate(certificate is JsonArray list ? list[0]!.GetValue<string>() : certificate.GetValue<string>());
    }

    /// <summary>Serves CRLs by URL, synchronously as well, as <see cref="SecomRevocation"/> fetches them.</summary>
    internal sealed class CrlServer : HttpMessageHandler
    {
        private int _requests;

        public Dictionary<string, byte[]> Crls { get; } = new(StringComparer.Ordinal);

        public bool Down { get; set; }

        public TimeSpan Delay { get; set; }

        public int Requests => Volatile.Read(ref _requests);

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            if (Delay > TimeSpan.Zero)
                Task.Delay(Delay, cancellationToken).GetAwaiter().GetResult();
            if (Down)
                throw new HttpRequestException("Connection refused.");
            return Crls.TryGetValue(request.RequestUri!.AbsoluteUri, out var crl)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(crl) }
                : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(Send(request, cancellationToken));
    }
}
