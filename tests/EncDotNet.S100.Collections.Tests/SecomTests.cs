using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Persistence;
using EncDotNet.S100.Collections.Secom;
using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Collections.Tests;

/// <summary>
/// The SECOM client, signature check, source indexer and download path
/// (issue #804). The <c>secom-ccg-*</c> fixtures were captured from the Canadian
/// Coast Guard's public S-124 service on 2026-10-06.
/// </summary>
public sealed class SecomTests : IDisposable
{
    private static readonly Uri ServiceUri = new("https://secom.test/api/secom");

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Summary_parsing_tolerates_edition_and_implementation_differences()
    {
        using var ccg = JsonDocument.Parse(File.ReadAllText(TestPaths.Fixture("secom-ccg-summary.json")));
        var page = SecomJson.ReadSummaryPage(ccg.RootElement);

        Assert.Equal(3, page.Items.Count);
        var first = page.Items[0];
        Assert.Equal("S-124", first.ProductSpec);  // "S124" on the wire
        Assert.Equal(SecomContainerType.DataSet, first.ContainerType);
        Assert.False(string.IsNullOrEmpty(first.Identifier));
        Assert.NotNull(first.LastModified);
        Assert.Equal(1735, page.TotalItems);

        // Edition 1 style: compact dates, "summaryObject", a null container type, no pagination.
        using var v1 = JsonDocument.Parse("""
            {"summaryObject":[{"dataReference":"a","containerType":null,"dataProductType":"S122",
              "info_lastModifiedDate":"20260506T000000Z","info_size":"7"}]}
            """);
        var item = Assert.Single(SecomJson.ReadSummaryPage(v1.RootElement).Items);
        Assert.Equal("S-122", item.ProductSpec);
        Assert.Equal(SecomContainerType.DataSet, item.ContainerType);
        Assert.Equal(new DateTimeOffset(2026, 5, 6, 0, 0, 0, TimeSpan.Zero), item.LastModified);
        Assert.Equal(7, item.Size);
    }

    [Theory]
    [InlineData("https://h.test/api/secom", "https://h.test/api/secom/")]
    [InlineData("https://h.test/api/secom/", "https://h.test/api/secom/")]
    [InlineData("https://h.test/api/secom/v2/object/summary?page=1", "https://h.test/api/secom/")]
    [InlineData("https://h.test/api/secom/v1/capability", "https://h.test/api/secom/")]
    public void Service_uris_are_normalised(string input, string expected) =>
        Assert.Equal(new Uri(expected), SecomClient.NormalizeServiceUri(new Uri(input)));

    [Fact]
    public async Task Client_falls_back_to_edition_1_and_keeps_using_it()
    {
        var server = new FakeSecomServer(Summaries(3)) { V2 = false };
        var client = new SecomClient(new HttpClient(server), ServiceUri);

        var list = await client.GetSummariesAsync(SecomQuery.All, cancellationToken: Ct);

        Assert.Equal(3, list.Items.Count);
        Assert.Equal(SecomApiVersion.V1, client.ApiVersion);
        Assert.All(server.Requests.Skip(1), r => Assert.Contains("/v1/", r.AbsolutePath, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Paging_reads_every_page_and_reports_truncation()
    {
        var server = new FakeSecomServer(Summaries(25));
        var client = new SecomClient(new HttpClient(server), ServiceUri);

        var all = await client.GetSummariesAsync(new SecomQuery(PageSize: 10), cancellationToken: Ct);
        Assert.Equal(25, all.Items.Count);
        Assert.False(all.Truncated);
        Assert.Equal(3, server.Requests.Count(r => r.AbsolutePath.EndsWith("/object/summary", StringComparison.Ordinal)));

        var capped = await client.GetSummariesAsync(new SecomQuery(PageSize: 10), maxItems: 12, cancellationToken: Ct);
        Assert.Equal(12, capped.Items.Count);
        Assert.True(capped.Truncated);
        Assert.Equal(25, capped.TotalItems);
    }

    [Fact]
    public async Task Paging_stops_when_a_service_ignores_it()
    {
        var server = new FakeSecomServer(Summaries(5)) { IgnorePaging = true, Paginate = false };
        var client = new SecomClient(new HttpClient(server), ServiceUri);

        var list = await client.GetSummariesAsync(new SecomQuery(PageSize: 2), cancellationToken: Ct);

        Assert.Equal(5, list.Items.Count);
        Assert.Equal(2, server.Requests.Count);  // the second page added nothing new
    }

    [Fact]
    public async Task A_service_reporting_more_than_it_returns_is_not_truncated()
    {
        var server = new FakeSecomServer(Summaries(3)) { ReportedTotal = 5 };
        var client = new SecomClient(new HttpClient(server), ServiceUri);

        var list = await client.GetSummariesAsync(SecomQuery.All, cancellationToken: Ct);

        Assert.Equal(3, list.Items.Count);
        Assert.Equal(5, list.TotalItems);
        Assert.False(list.Truncated);
    }

    [Fact]
    public void A_real_ccg_signature_verifies_and_tampering_is_detected()
    {
        using var stream = File.OpenRead(TestPaths.Fixture("secom-ccg-get.json"));
        var secom = SecomClient.ReadDataObject(stream);
        Assert.Equal("ecdsa-384-sha2", secom.Metadata!.SignatureReference);

        var check = SecomSignatureVerifier.Verify(secom.Data, secom.Metadata);
        Assert.Equal(SecomSignatureStatus.Valid, check.Status);
        Assert.Equal("Canadian Coast Guard", check.Signer);
        Assert.Null(check.SignerTrusted);

        var tampered = (byte[])secom.Data.Clone();
        tampered[^2] ^= 0x20;
        Assert.Equal(SecomSignatureStatus.Invalid, SecomSignatureVerifier.Verify(tampered, secom.Metadata).Status);
    }

    [Fact]
    public void Signatures_check_trust_when_roots_are_given()
    {
        using var signer = Signer.Create();
        var data = "<S124:Dataset/>"u8.ToArray();
        var metadata = signer.Sign(data);

        Assert.Equal(SecomSignatureStatus.Unsigned, SecomSignatureVerifier.Verify(data, null).Status);
        Assert.Equal(SecomSignatureStatus.Unsigned, SecomSignatureVerifier.Verify(data, metadata with { Signature = null }).Status);

        var trusted = SecomSignatureVerifier.Verify(data, metadata, [signer.Certificate]);
        Assert.Equal(SecomSignatureStatus.Valid, trusted.Status);
        Assert.True(trusted.SignerTrusted);
        Assert.False(trusted.SignerExpired);

        using var other = Signer.Create();
        Assert.False(SecomSignatureVerifier.Verify(data, metadata, [other.Certificate]).SignerTrusted);

        // Single-line ("minified") PEM is accepted as well as base64 DER.
        var pem = "-----BEGIN CERTIFICATE-----" + Convert.ToBase64String(signer.Certificate.RawData) + "-----END CERTIFICATE-----";
        Assert.Equal(SecomSignatureStatus.Valid, SecomSignatureVerifier.Verify(data, metadata with { PublicCertificates = [pem] }).Status);
    }

    [Fact]
    public async Task Objects_index_as_online_secom_downloads()
    {
        var server = new FakeSecomServer(Summaries(4));
        var indexer = CollectionIndexer.CreateDefault(feeds: [new SecomSourceIndexer(new HttpClient(server), _temp.Path)]);
        var source = Source();

        var index = await indexer.IndexAsync(source, cancellationToken: Ct);

        Assert.Empty(index.Diagnostics);
        Assert.Equal(4, index.Items.Count);
        var item = index.Items.Single(i => i.Name == "NW-0001-26");
        Assert.Equal("S-124", item.ProductSpec);
        Assert.Equal(CollectionItemStatus.Active, item.Status);
        Assert.Null(item.Bounds);
        var remote = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.Equal(RemoteEnvelope.Secom, remote.Envelope);
        Assert.Equal(new Uri("https://secom.test/api/secom/v2/object?dataReference=ref-0001"), remote.Uri);
        Assert.Equal("NW-0001-26.gml", remote.Layout!.RelativePath);
        Assert.StartsWith("secom/secom.test-", remote.DownloadFolder, StringComparison.Ordinal);

        // Unchanged: the previous index is reused without re-reading the service.
        var requests = server.Requests.Count;
        var again = await indexer.IndexAsync(source, index, cancellationToken: Ct);
        Assert.Same(index, again);
        Assert.Equal(requests, server.Requests.Count);
    }

    [Fact]
    public async Task Products_filter_on_this_side_and_exchange_sets_are_listed_only()
    {
        var summaries = Summaries(3).Append(Summary(9, "S-122")).Append(Summary(10, "S-124", containerType: 1)).ToArray();
        var indexer = new SecomSourceIndexer(new HttpClient(new FakeSecomServer(summaries)));

        var index = await indexer.IndexAsync(Source(new SecomFilter { ProductSpecs = ["s-124"] }), null, Ct);

        Assert.Equal(4, index.Items.Count);
        Assert.All(index.Items, i => Assert.Equal("S-124", i.ProductSpec));
        Assert.IsType<NoItemLocation>(index.Items.Single(i => i.Key == "ref-0010").Location);
        Assert.Contains(index.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Info && d.Message.Contains("exchange-set", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unreachable_service_lists_what_it_last_offered()
    {
        var server = new FakeSecomServer(Summaries(2));
        var cache = Path.Combine(_temp.Path, "cache");
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(DateTimeOffset.UtcNow);
        var indexer = new SecomSourceIndexer(new HttpClient(server), cache, timeProvider: time);
        var source = Source();
        await indexer.IndexAsync(source, null, Ct);

        server.Down = true;
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(await indexer.GetFingerprintAsync(source, Ct));
        var stale = await indexer.IndexAsync(source, null, Ct);

        Assert.Equal(2, stale.Items.Count);
        Assert.Null(stale.Fingerprint);
        Assert.Contains(stale.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Warning);

        // Nothing cached: an error, and no items.
        var fresh = new SecomSourceIndexer(new HttpClient(server));
        var failed = await fresh.IndexAsync(source, null, Ct);
        Assert.Empty(failed.Items);
        Assert.Contains(failed.Diagnostics, d => d.Severity == IndexDiagnosticSeverity.Error);
    }

    [Fact]
    public async Task Large_services_are_capped_with_a_warning()
    {
        var indexer = new SecomSourceIndexer(new HttpClient(new FakeSecomServer(Summaries(30))), maxItems: 10);

        var index = await indexer.IndexAsync(Source(), null, Ct);

        Assert.Equal(10, index.Items.Count);
        Assert.Contains(index.Diagnostics, d => d.Message.Contains("offers 30 objects", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Download_decodes_and_checks_the_object_then_indexes_its_bounds()
    {
        using var signer = Signer.Create();
        var data = Encoding.UTF8.GetBytes("<S124:Dataset/>");
        var server = new FakeSecomServer(Summaries(2));
        server.Objects["ref-0001"] = (data, signer.Sign(data));
        var downloads = Path.Combine(_temp.Path, "downloads");
        var probed = new List<string>();
        DatasetProbe probe = (path, _) =>
        {
            probed.Add(path);
            return new DatasetMetadata
            {
                Spec = new SpecRef("S-124", default),
                Extent = new BoundingBox(49, -127, 51, -125),
            };
        };
        var indexer = CollectionIndexer.CreateDefault(
            feeds: [new SecomSourceIndexer(new HttpClient(server), downloadsRoot: downloads, probe: probe)]);
        var source = Source();
        var index = await indexer.IndexAsync(source, cancellationToken: Ct);
        var item = index.Items.Single(i => i.Key == "ref-0001");
        var remote = (RemoteItemLocation)item.Location;

        var downloader = new EncCellDownloader(new HttpClient(server), Path.Combine(downloads, remote.DownloadFolder!));
        var downloaded = await downloader.DownloadAsync(item, cancellationToken: Ct);

        var file = Path.Combine(downloaded.Location.RootPath, downloaded.Location.RelativePath);
        Assert.Equal("NW-0001-26.gml", Path.GetFileName(file));
        Assert.Equal(data, File.ReadAllBytes(file));
        Assert.Equal(SecomSignatureStatus.Valid, downloaded.Signature!.Status);
        Assert.Equal(SecomSignatureStatus.Valid, downloader.TryGetDownloaded(item.Name)!.Signature!.Status);
        Assert.False(downloaded.IsOlderThan(item));

        // The download changes the fingerprint; the item now has bounds and its signature.
        var after = await indexer.IndexAsync(source, index, cancellationToken: Ct);
        Assert.NotSame(index, after);
        var indexed = after.Items.Single(i => i.Key == "ref-0001");
        Assert.Equal(new GeoBounds(49, -127, 51, -125), indexed.Bounds);
        Assert.Equal("valid", indexed.Properties["signature"]);
        Assert.Equal(file, Assert.Single(probed));
    }

    [Fact]
    public async Task Download_refuses_bad_signatures_and_encrypted_objects()
    {
        using var signer = Signer.Create();
        var data = "<S124:Dataset/>"u8.ToArray();
        var server = new FakeSecomServer(Summaries(3));
        server.Objects["ref-0001"] = ("<S124:Dataset>tampered</S124:Dataset>"u8.ToArray(), signer.Sign(data));
        server.Objects["ref-0002"] = (data, signer.Sign(data) with { DataProtection = true });
        var index = await new SecomSourceIndexer(new HttpClient(server)).IndexAsync(Source(), null, Ct);
        var downloader = new EncCellDownloader(new HttpClient(server), Path.Combine(_temp.Path, "downloads"));

        var bad = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync(index.Items.Single(i => i.Key == "ref-0001"), cancellationToken: Ct));
        Assert.Contains("signature", bad.Message, StringComparison.Ordinal);
        var encrypted = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync(index.Items.Single(i => i.Key == "ref-0002"), cancellationToken: Ct));
        Assert.Contains("encrypted", encrypted.Message, StringComparison.Ordinal);

        // Nothing is left behind.
        Assert.Empty(Directory.EnumerateFileSystemEntries(downloader.Root));
    }

    [Fact]
    public void Secom_sources_and_locations_round_trip_through_the_store()
    {
        var source = Source(new SecomFilter { ProductSpecs = ["S-124"], GeometryWkt = "POLYGON((0 0,1 0,1 1,0 0))" });
        var document = new CollectionStoreDocument(CollectionStoreDocument.CurrentVersion,
            [new DatasetCollection(Guid.NewGuid(), "Warnings", [source], DateTimeOffset.UnixEpoch)]);

        var json = CollectionJson.SerializeStore(document);
        Assert.Contains("\"kind\": \"secom\"", json, StringComparison.Ordinal);
        Assert.Equal(source, Assert.Single(Assert.Single(CollectionJson.DeserializeStore(json).Collections).Sources));

        var location = new RemoteItemLocation(new Uri("https://h.test/v2/object?dataReference=a"), Envelope: RemoteEnvelope.Secom);
        var locationJson = JsonSerializer.Serialize<ItemLocation>(location, CollectionJson.IndexOptions);
        Assert.Contains("\"envelope\":\"secom\"", locationJson, StringComparison.Ordinal);
        Assert.Equal(location, JsonSerializer.Deserialize<ItemLocation>(locationJson, CollectionJson.IndexOptions));
        Assert.DoesNotContain("envelope", JsonSerializer.Serialize<ItemLocation>(new RemoteItemLocation(location.Uri), CollectionJson.IndexOptions), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_secom_service_url_is_recognised_by_its_capability()
    {
        var server = new FakeSecomServer(Summaries(1));

        var probe = await KnownSources.CatalogueFormatDetector.ProbeAsync(
            new HttpClient(server), new Uri("https://secom.test/api/secom"), Ct);

        Assert.Equal(KnownSources.KnownCatalogueFormat.Secom, probe.Format);
        var known = KnownSources.KnownCatalogueSources.FromUrl(new Uri("https://secom.test/api/secom/v2/capability"), probe.Format!.Value);
        Assert.Equal(new Uri("https://secom.test/api/secom/"), known.CatalogUri);
        Assert.Equal(KnownSources.KnownCatalogueCoverage.None, known.Coverage);

        // Not a SECOM service: the fetch error stands.
        await Assert.ThrowsAsync<HttpRequestException>(() => KnownSources.CatalogueFormatDetector.ProbeAsync(
            new HttpClient(new FakeSecomServer([]) { Capability = false }), new Uri("https://secom.test/nothing"), Ct));
    }

    [Fact]
    public async Task Describe_counts_products_and_shares_its_read_with_the_index()
    {
        var server = new FakeSecomServer(Summaries(3).Append(Summary(9, "S-122")));
        var indexer = new SecomSourceIndexer(new HttpClient(server));

        var description = await indexer.DescribeAsync(ServiceUri, cancellationToken: Ct);

        Assert.Equal(["S-122", "S-124"], description.Products.Select(p => p.Value));
        Assert.Equal(3, description.Products[1].CellCount);
        Assert.Equal(SecomApiVersion.V2, description.ApiVersion);

        var requests = server.Requests.Count;
        var index = await indexer.IndexAsync(Source(), null, Ct);
        Assert.Equal(4, index.Items.Count);
        Assert.Equal(requests, server.Requests.Count);
    }

    private static SecomSource Source(SecomFilter? filter = null) =>
        new(Guid.NewGuid(), null, ServiceUri, filter ?? SecomFilter.All);

    private static IEnumerable<JsonObject> Summaries(int count) =>
        Enumerable.Range(1, count).Select(i => Summary(i, "S-124"));

    private static JsonObject Summary(int index, string product, int containerType = 0) => new()
    {
        ["dataReference"] = $"ref-{index:D4}",
        ["dataProtection"] = false,
        ["dataCompression"] = false,
        ["containerType"] = containerType,
        ["dataProductType"] = product,
        ["info_identifier"] = $"NW-{index:D4}-26",
        ["info_name"] = "Drifting Hazard",
        ["info_status"] = "PUBLISHED",
        ["info_lastModifiedDate"] = "2026-10-06T21:58:13Z",
        ["info_productVersion"] = "2.0.0",
        ["info_size"] = 5000 + index,
    };

    /// <summary>A self-signed P-256 signer, standing in for an MCP certificate.</summary>
    private sealed class Signer : IDisposable
    {
        private readonly ECDsa _key;

        private Signer(ECDsa key, X509Certificate2 certificate)
        {
            _key = key;
            Certificate = certificate;
        }

        public X509Certificate2 Certificate { get; }

        public static Signer Create()
        {
            var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var request = new CertificateRequest("CN=Test Service", key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
            var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
            return new Signer(key, certificate);
        }

        public SecomExchangeMetadata Sign(byte[] data) => new(
            false,
            "SECOM",
            "ecdsa-256-sha2-256",
            false,
            [Convert.ToBase64String(Certificate.RawData)],
            Convert.ToHexString(SHA256.HashData(Certificate.RawData)),
            Convert.ToHexString(_key.SignData(data, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence)));

        public void Dispose()
        {
            _key.Dispose();
            Certificate.Dispose();
        }
    }

    /// <summary>A SECOM service serving summaries (paged) and objects, on v2 and v1.</summary>
    private sealed class FakeSecomServer(IEnumerable<JsonObject> summaries) : HttpMessageHandler
    {
        private readonly JsonObject[] _summaries = summaries.ToArray();

        public bool V2 { get; init; } = true;

        public bool IgnorePaging { get; init; }

        public bool Paginate { get; init; } = true;

        public bool Down { get; set; }

        public bool Capability { get; init; } = true;

        public int? ReportedTotal { get; init; }

        public Dictionary<string, (byte[] Data, SecomExchangeMetadata Metadata)> Objects { get; } = new(StringComparer.Ordinal);

        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (Requests)
                Requests.Add(uri);
            if (Down)
                throw new HttpRequestException("Connection refused.");

            var path = uri.AbsolutePath;
            var isV2 = path.Contains("/v2/", StringComparison.Ordinal);
            if (isV2 && !V2)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            JsonNode body;
            if (path.EndsWith("/capability", StringComparison.Ordinal) && Capability)
            {
                body = new JsonObject
                {
                    ["capability"] = new JsonArray(new JsonObject
                    {
                        ["containerType"] = 0,
                        ["dataProductType"] = isV2 ? "S-124" : "S124",
                        ["serviceVersion"] = isV2 ? "2.0.0" : "1.0.0",
                        ["implementedInterfaces"] = new JsonObject { ["get"] = true, ["getSummary"] = true },
                    }),
                };
            }
            else if (path.EndsWith("/object/summary", StringComparison.Ordinal))
            {
                var page = int.Parse(query["page"] ?? "1", System.Globalization.CultureInfo.InvariantCulture);
                var size = int.Parse(query["pageSize"] ?? "100", System.Globalization.CultureInfo.InvariantCulture);
                var items = IgnorePaging ? _summaries : _summaries.Skip((page - 1) * size).Take(size);
                var list = new JsonArray(items.Select(s =>
                {
                    var copy = s.DeepClone().AsObject();
                    if (!isV2)
                        copy["dataProductType"] = copy["dataProductType"]!.GetValue<string>().Replace("-", string.Empty, StringComparison.Ordinal);
                    return (JsonNode)copy;
                }).ToArray());
                body = new JsonObject { [isV2 ? "summaryObject" : "informationSummaryObject"] = list };
                if (Paginate)
                    body["pagination"] = new JsonObject { ["totalItems"] = ReportedTotal ?? _summaries.Length, ["maxItemsPerPage"] = size };
            }
            else if (path.EndsWith("/object", StringComparison.Ordinal) && Objects.TryGetValue(query["dataReference"] ?? string.Empty, out var found))
            {
                var m = found.Metadata;
                body = new JsonObject
                {
                    ["dataResponseObject"] = new JsonArray(new JsonObject
                    {
                        ["data"] = Convert.ToBase64String(found.Data),
                        ["exchangeMetadata"] = new JsonObject
                        {
                            ["dataProtection"] = m.DataProtection,
                            ["protectionScheme"] = m.ProtectionScheme,
                            ["digitalSignatureReference"] = m.SignatureReference,
                            ["compressionFlag"] = m.Compressed,
                            ["digitalSignatureValue"] = new JsonObject
                            {
                                ["publicRootCertificateThumbprint"] = m.RootCertificateThumbprint,
                                ["publicCertificate"] = new JsonArray(m.PublicCertificates.Select(c => (JsonNode)JsonValue.Create(c)!).ToArray()),
                                ["digitalSignature"] = m.Signature,
                            },
                        },
                    }),
                };
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
            });
        }
    }
}
