using System.Globalization;
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
/// SECOM 2.0's signed POST GetSummary and Get (issue #838): request
/// envelopes signed with the client's MCP identity, against a fake service
/// that has no GET summary and checks each signature as GLA's SECOMLib does.
/// </summary>
public sealed class SecomEnvelopeTests : IDisposable
{
    private static readonly Uri ServiceUri = new("https://secom2.test/api/secom/");

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void The_canonical_string_follows_secomlib()
    {
        var at = new DateTimeOffset(2026, 10, 9, 5, 0, 0, TimeSpan.Zero);
        Assert.Equal(
            "0.S-124...." + at.ToUnixTimeSeconds() + "..1.100.a,b.ff.true",
            SecomEnvelopeSigner.CanonicalString([SecomContainerType.DataSet, "S-124", null, null, null, at, null, 1, 100, new[] { "a", "b" }, "ff", true]));
    }

    [Fact]
    public void A_summary_request_is_signed_over_its_envelope_with_the_identity()
    {
        using var identity = Identity(ECCurve.NamedCurves.nistP384);
        var signer = new SecomEnvelopeSigner(identity, SecomTrustAnchors.None);
        Assert.Equal("ecdsa-384-sha3", signer.SignatureReference);

        var now = new DateTimeOffset(2026, 10, 9, 5, 0, 0, TimeSpan.Zero);
        var body = signer.SummaryRequest(new SecomQuery(GeometryWkt: "POLYGON((0 0,1 0,1 1,0 0))", PageSize: 50), 2, now);
        var envelope = body["envelope"]!.AsObject();
        Assert.Equal(2, (int)envelope["page"]!);
        Assert.Equal("2026-10-09T05:00:00Z", (string)envelope["envelopeSignatureTime"]!);
        Assert.False(envelope.ContainsKey("dataProductType"));  // absent, not null
        // The root of a certificate under no anchor is itself: a SHA-384 thumbprint.
        Assert.Equal(Convert.ToHexString(SHA384.HashData(identity.Certificate.RawData)).ToLowerInvariant(),
            (string)envelope["envelopeRootCertificateThumbprint"]!);

        Assert.True(Service.Verifies(body, SummaryFields));
        envelope["page"] = 3;  // tampered
        Assert.False(Service.Verifies(body, SummaryFields));

        // A P-256 identity signs with SHA-256 unless told otherwise.
        using var p256 = Identity(ECCurve.NamedCurves.nistP256);
        Assert.Equal("ecdsa-256-sha2-256", new SecomEnvelopeSigner(p256).SignatureReference);
        Assert.Equal("ecdsa-384-sha2", new SecomEnvelopeSigner(identity, signatureReference: "ECDSA-384-SHA2").SignatureReference);
        Assert.Throws<InvalidDataException>(() => new SecomEnvelopeSigner(identity, signatureReference: "dsa"));
    }

    [Fact]
    public async Task A_post_only_service_is_listed_and_read_through_signed_requests()
    {
        using var identity = Identity(ECCurve.NamedCurves.nistP384);
        var service = new Service(objects: 3);
        var client = new SecomClient(new HttpClient(service), ServiceUri) { Signer = () => new SecomEnvelopeSigner(identity) };

        var list = await client.GetSummariesAsync(new SecomQuery(PageSize: 2), cancellationToken: Ct);
        Assert.Equal(3, list.Items.Count);
        Assert.True(client.UsesPostInterfaces);
        Assert.Equal(SecomApiVersion.V2, client.ApiVersion);
        Assert.Contains(service.Requests, r => r == "GET /api/secom/v2/object/summary");  // tried first
        // Only the empty request that detects the POST form fails verification; every signed one passes.
        Assert.Equal("POST /api/secom/v2/object/search/summary bad", Assert.Single(service.Requests, r => r.EndsWith(" bad", StringComparison.Ordinal)));
        Assert.Equal(2, service.Requests.Count(r => r == "POST /api/secom/v2/object/search/summary"));

        var data = await client.GetAsync("ref-0002", Ct);
        Assert.Equal("<S124:Dataset>ref-0002</S124:Dataset>", Encoding.UTF8.GetString(data!.Data));
    }

    [Fact]
    public async Task Without_an_identity_a_post_only_service_says_it_needs_one()
    {
        var client = new SecomClient(new HttpClient(new Service(objects: 1)), ServiceUri) { Signer = () => null };

        var ex = await Assert.ThrowsAsync<SecomIdentityRequiredException>(() => client.GetSummaryPageAsync(SecomQuery.All, 1, Ct));
        Assert.Contains("MCP identity", ex.Message, StringComparison.Ordinal);
        Assert.True(client.UsesPostInterfaces);
    }

    [Fact]
    public async Task A_post_only_source_indexes_and_downloads_with_signed_requests()
    {
        using var identity = Identity(ECCurve.NamedCurves.nistP384);
        Func<SecomEnvelopeSigner?> signer = () => new SecomEnvelopeSigner(identity);
        var service = new Service(objects: 2);
        var downloads = Path.Combine(_temp.Path, "downloads");
        var indexer = new SecomSourceIndexer(new HttpClient(service), downloadsRoot: downloads) { Signer = signer };
        var source = new SecomSource(Guid.NewGuid(), null, ServiceUri, SecomFilter.All);

        var index = await indexer.IndexAsync(source, null, Ct);
        Assert.Equal(2, index.Items.Count);
        var item = index.Items.Single(i => i.Key == "ref-0001");
        var remote = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.Equal(RemoteEnvelope.SecomPost, remote.Envelope);

        var downloader = new EncCellDownloader(new HttpClient(service), Path.Combine(downloads, remote.DownloadFolder!)) { SecomSigner = signer };
        var downloaded = await downloader.DownloadAsync(item, cancellationToken: Ct);
        Assert.Equal("<S124:Dataset>ref-0001</S124:Dataset>", File.ReadAllText(Path.Combine(downloaded.Location.RootPath, downloaded.Location.RelativePath)));

        // Without an identity the download says what it needs.
        var unsigned = new EncCellDownloader(new HttpClient(service), Path.Combine(_temp.Path, "other")) { SecomSigner = () => null };
        await Assert.ThrowsAsync<SecomIdentityRequiredException>(() => unsigned.DownloadAsync(item, cancellationToken: Ct));
    }

    private static readonly string[] SummaryFields =
        ["containerType", "dataProductType", "productVersion", "geometry", "unlocode", "validFrom", "validTo", "page", "pageSize"];

    private static readonly string[] GetFields =
        ["dataReference", "containerType", "dataProductType", "productVersion", "geometry", "unlocode", "validFrom", "validTo", "page", "pageSize"];

    private static SecomClientIdentity Identity(ECCurve curve)
    {
        using var key = ECDsa.Create(curve);
        var request = new CertificateRequest("CN=Test vessel", key, HashAlgorithmName.SHA256);
        return SecomClientIdentity.FromCertificate(
            request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)), SecomTrustAnchors.None);
    }

    /// <summary>
    /// A SECOM 2.0 service with only the POST forms: no GET summary or Get
    /// (404). It rebuilds each envelope's canonical string from the posted
    /// JSON and checks the signature with the presented certificate, as
    /// SECOMLib does, answering 400 when it does not verify.
    /// </summary>
    private sealed class Service(int objects) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            lock (Requests)
                Requests.Add($"{request.Method} {path}");

            if (request.Method == HttpMethod.Get && path.EndsWith("/v2/capability", StringComparison.Ordinal))
                return Json("""{"capability":[{"containerType":0,"dataProductType":"S-124","implementedInterfaces":{"get":true,"getSummary":true},"serviceVersion":"0.1.0"}]}""");
            if (request.Method != HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.NotFound);

            var text = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            var body = JsonNode.Parse(text) as JsonObject;
            var summary = path.EndsWith("/v2/object/search/summary", StringComparison.Ordinal);
            var get = path.EndsWith("/v2/object/search", StringComparison.Ordinal);
            if (!summary && !get)
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            if (body?["envelope"] is not JsonObject envelope || !Verifies(body, summary ? SummaryFields : GetFields))
            {
                lock (Requests)
                    Requests[^1] += " bad";
                return new HttpResponseMessage(HttpStatusCode.BadRequest);
            }

            if (summary)
            {
                var page = (int?)envelope["page"] ?? 1;
                var size = (int?)envelope["pageSize"] ?? 100;
                var all = SecomTests.Summaries(objects).ToArray();
                var list = new JsonArray(all.Skip((page - 1) * size).Take(size).Select(s => (JsonNode)s.DeepClone()).ToArray());
                return Json(new JsonObject
                {
                    ["summaryObject"] = list,
                    ["pagination"] = new JsonObject { ["totalItems"] = objects, ["maxItemsPerPage"] = size },
                }.ToJsonString());
            }

            var reference = (string?)envelope["dataReference"] ?? string.Empty;
            return Json(new JsonObject
            {
                ["dataResponseObject"] = new JsonArray(new JsonObject
                {
                    ["data"] = Convert.ToBase64String(Encoding.UTF8.GetBytes($"<S124:Dataset>{reference}</S124:Dataset>")),
                    ["exchangeMetadata"] = new JsonObject { ["dataProtection"] = false, ["compressionFlag"] = false },
                }),
            }.ToJsonString());
        }

        /// <summary>
        /// Checks an envelope's signature as SECOMLib does: the canonical string
        /// of its fields in order (absent is empty, times as epoch seconds,
        /// lists joined with ','), signed with SHA3-384 or SHA-256 ECDSA (DER)
        /// by the first presented certificate, and the thumbprint as SHA-384 of the root.
        /// </summary>
        public static bool Verifies(JsonObject body, string[] filterFields)
        {
            var envelope = body["envelope"]!.AsObject();
            string Text(string field) => envelope[field] switch
            {
                null => string.Empty,
                JsonArray list => string.Join(',', list.Select(n => (string)n!)),
                JsonValue v when field is "validFrom" or "validTo" or "envelopeSignatureTime" =>
                    DateTimeOffset.Parse((string)v!, CultureInfo.InvariantCulture).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                var other => other.ToString(),
            };

            var canonical = string.Join('.', filterFields
                .Concat(["envelopeSignatureCertificate", "envelopeRootCertificateThumbprint", "envelopeSignatureTime"])
                .Select(Text));
            var certificates = envelope["envelopeSignatureCertificate"]!.AsArray();
            using var certificate = X509CertificateLoader.LoadCertificate(Convert.FromBase64String((string)certificates[0]!));
            if (!string.Equals((string?)envelope["envelopeRootCertificateThumbprint"],
                    Convert.ToHexString(SHA384.HashData(certificate.RawData)), StringComparison.OrdinalIgnoreCase))
                return false;

            using var key = certificate.GetECDsaPublicKey()!;
            var data = Encoding.UTF8.GetBytes(canonical);
            var digest = key.KeySize >= 384
                ? (SHA3_384.IsSupported ? SHA3_384.HashData(data) : Sha3.HashData384(data))
                : SHA256.HashData(data);
            return key.VerifyHash(digest, Convert.FromHexString((string)body["envelopeSignature"]!), DSASignatureFormat.Rfc3279DerSequence);
        }

        private static HttpResponseMessage Json(string body) =>
            new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
