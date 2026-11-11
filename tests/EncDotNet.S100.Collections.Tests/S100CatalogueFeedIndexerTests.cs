using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.RemoteCatalogues;

namespace EncDotNet.S100.Collections.Tests;

public sealed class S100CatalogueFeedIndexerTests : IDisposable
{
    /// <summary>NOAA's gzip-encoded copy of the S-102 catalogue; datasets are named <c>file:../Region/Area/…</c>.</summary>
    private static readonly Uri CatalogUri = new("https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/S100_ROOT/CATALOG.XML");

    private static readonly Uri Root = new("https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/");

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private static byte[] GzipCatalogue()
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest))
        using (var input = File.OpenRead(TestPaths.Fixture("noaa-s102-catalog.xml")))
            input.CopyTo(gzip);
        return output.ToArray();
    }

    private static RemoteS100Catalogue ReadFixture()
    {
        using var stream = File.OpenRead(TestPaths.Fixture("noaa-s102-catalog.xml"));
        return RemoteS100CatalogueReader.Read(stream, CatalogUri, "catalogues/test");
    }

    private static S100CatalogueFeedSource Source(S100CatalogueFilter? filter = null) =>
        new(Guid.NewGuid(), null, CatalogUri, filter ?? S100CatalogueFilter.All);

    [Fact]
    public void Datasets_resolve_against_the_catalogue_and_keep_their_folder()
    {
        var catalogue = ReadFixture();

        Assert.Equal(Root, catalogue.RootUri);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 18, 1, 17, TimeSpan.Zero), catalogue.IssuedAt);
        Assert.Equal(5, catalogue.Items.Count);

        var boston = catalogue.Items.Single(i => i.Name == "102US005MA1RF");
        Assert.Equal("Northeast/Boston/102US005MA1RF", boston.Key);
        Assert.Equal("S-102", boston.ProductSpec);
        Assert.Equal(20, boston.Edition);
        Assert.Equal("Northeast, Boston", boston.Title);
        Assert.Equal(CollectionItemStatus.Active, boston.Status);
        Assert.NotNull(boston.Bounds);
        Assert.NotNull(boston.Coverage);
        Assert.Equal("Northeast/Boston", RemoteS100Catalogue.FolderOf(boston));
        Assert.Equal("port", boston.Properties[RemoteS100Catalogue.NavigationPurposeProperty]);
        Assert.Equal("4", boston.Properties[RemoteS100Catalogue.GridResolutionProperty]);
        Assert.Equal("true", boston.Properties["notForNavigation"]);

        var remote = Assert.IsType<RemoteItemLocation>(boston.Location);
        Assert.Equal(new Uri(Root, "Northeast/Boston/102US005MA1RF262267.h5"), remote.Uri);
        Assert.Null(remote.SizeBytes);
        Assert.Equal("catalogues/test", remote.DownloadFolder);
        Assert.Equal("102US005MA1RF262267.h5", remote.Layout!.RelativePath);
        Assert.Empty(remote.Layout.UpdateRelativePaths);
    }

    [Fact]
    public void A_gzip_encoded_copy_reads_the_same()
    {
        using var stream = new MemoryStream(GzipCatalogue());

        var catalogue = RemoteS100CatalogueReader.Read(stream, CatalogUri);

        Assert.Equal(ReadFixture().Items.Select(i => i.Key), catalogue.Items.Select(i => i.Key));
    }

    [Theory]
    [InlineData("102US004SC1EV262247", "102US004SC1EV")]
    [InlineData("104US004SC1BO_20251217T12Z", "104US004SC1BO")]
    [InlineData("111US00_CBOFS_20260930T18Z_US4VA1DD", "111US00_CBOFS_US4VA1DD")]
    [InlineData("101GB005X01NE", "101GB005X01NE")]
    [InlineData("DATASET_1", "DATASET_1")]
    public void Stable_names_drop_version_and_run_time_tokens(string stem, string expected)
    {
        Assert.Equal(expected, RemoteS100CatalogueReader.StableName(stem));
    }

    [Fact]
    public void Facets_list_regions_with_their_areas_and_purposes_finest_first()
    {
        var catalogue = ReadFixture();

        var regions = S100CatalogueFacets.Regions(catalogue.Items);
        Assert.Equal(["California", "Northeast", "Oregon", "Southeast"], regions.Select(r => r.Name));
        var northeast = regions.Single(r => r.Folder == "Northeast");
        Assert.Equal("Northeast/Boston", Assert.Single(northeast.Areas).Value);
        Assert.Equal(2, northeast.Areas[0].CellCount);

        var purposes = S100CatalogueFacets.NavigationPurposes(catalogue.Items);
        Assert.Equal(["port", "transit"], purposes.Select(p => p.Value));
        Assert.Equal(4.0, purposes[0].GridResolution);
        Assert.Equal(3, purposes[0].Count);
        Assert.Equal(16.0, purposes[1].GridResolution);
    }

    [Fact]
    public async Task Everything_indexes_with_sizes_from_the_bucket_and_areas_as_groups()
    {
        var server = new BucketServer(GzipCatalogue());
        var indexer = CollectionIndexer.CreateDefault(feeds: [new S100CatalogueFeedIndexer(new HttpClient(server), _temp.Path)]);

        var index = await indexer.IndexAsync(Source(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(index.Diagnostics);
        Assert.Equal(5, index.Items.Count);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 18, 1, 17, TimeSpan.Zero), index.PublishedAt);
        var remote = Assert.IsType<RemoteItemLocation>(index.Items.Single(i => i.Name == "102US005MA1RF").Location);
        Assert.Equal(BucketServer.SizeOf("ed3.0.0/Northeast/Boston/102US005MA1RF262267.h5"), remote.SizeBytes);
        Assert.NotNull(remote.LastModified);
        Assert.StartsWith("catalogues/noaa-s102-pds.s3.amazonaws.com-", remote.DownloadFolder, StringComparison.Ordinal);

        // The whole set is listed once (over three pages); same-named areas are told apart.
        Assert.Equal(["ed3.0.0/", "ed3.0.0/", "ed3.0.0/"], server.ListedPrefixes);
        Assert.Equal(
            ["Newport (California)", "Boston", "Newport (Oregon)", "Wilmington"],
            index.Groups.Select(g => g.Name));
        Assert.Equal("California/Newport", index.Groups[0].Id);

        // Unchanged (a 304): the previous index is reused.
        var again = await indexer.IndexAsync(Source() with { Id = index.SourceId }, index, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Same(index, again);
    }

    [Fact]
    public async Task A_scoped_source_lists_only_its_regions()
    {
        var server = new BucketServer(GzipCatalogue());
        var indexer = new S100CatalogueFeedIndexer(new HttpClient(server), _temp.Path);

        var index = await indexer.IndexAsync(
            Source(new S100CatalogueFilter { Folders = ["Northeast/Boston", "Oregon"], NavigationPurposes = ["Port"] }),
            null,
            CancellationToken.None);

        Assert.Equal(["102US005MA1RF", "102US005OR2MC"], index.Items.Select(i => i.Name).Order());
        Assert.All(index.Items, i => Assert.NotNull(((RemoteItemLocation)i.Location).SizeBytes));
        Assert.Equal(["ed3.0.0/Northeast/", "ed3.0.0/Oregon/"], server.ListedPrefixes.Order());
        Assert.Equal(["Boston", "Newport"], index.Groups.Select(g => g.Name));
    }

    [Fact]
    public async Task Sizes_stay_unknown_when_the_bucket_cannot_be_listed()
    {
        var server = new BucketServer(GzipCatalogue()) { ListingFails = true };
        var indexer = new S100CatalogueFeedIndexer(new HttpClient(server), _temp.Path);

        var index = await indexer.IndexAsync(Source(), null, CancellationToken.None);

        Assert.Equal(5, index.Items.Count);
        Assert.All(index.Items, i => Assert.Null(((RemoteItemLocation)i.Location).SizeBytes));
        Assert.Equal(IndexDiagnosticSeverity.Info, Assert.Single(index.Diagnostics).Severity);
    }

    [Fact]
    public async Task Listings_are_reused_between_choosing_and_indexing()
    {
        var server = new BucketServer(GzipCatalogue());
        var indexer = new S100CatalogueFeedIndexer(new HttpClient(server), _temp.Path);
        var catalogue = await indexer.GetCatalogueAsync(CatalogUri, cancellationToken: TestContext.Current.CancellationToken);

        var sizes = await indexer.ListAsync(catalogue, ["Northeast"], TestContext.Current.CancellationToken);
        await indexer.IndexAsync(Source(new S100CatalogueFilter { Folders = ["Northeast/Boston"] }), null, CancellationToken.None);

        Assert.Equal(2, sizes!.Count);
        Assert.Equal(["ed3.0.0/Northeast/"], server.ListedPrefixes);
    }

    [Fact]
    public async Task Catalogues_outside_S3_have_no_listing()
    {
        using var stream = File.OpenRead(TestPaths.Fixture("noaa-s102-catalog.xml"));
        var catalogue = RemoteS100CatalogueReader.Read(stream, new Uri("https://charts.example.test/s102/_CATALOG/CATALOG.XML"));
        var indexer = new S100CatalogueFeedIndexer(new HttpClient(new BucketServer([])), _temp.Path);

        Assert.Equal(new Uri("https://charts.example.test/s102/"), catalogue.RootUri);
        Assert.Null(await indexer.ListAsync(catalogue, [string.Empty], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_dataset_downloads_as_its_own_file()
    {
        var item = ReadFixture().Items.Single(i => i.Name == "102US004SC1EV");
        var downloader = new EncCellDownloader(new HttpClient(new BucketServer([])), _temp.Path);

        var cell = await downloader.DownloadAsync(item, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(5, cell.Edition);
        Assert.Equal(Path.Combine(_temp.Path, "102US004SC1EV"), cell.Location.RootPath);
        Assert.Equal("102US004SC1EV262247.h5", cell.Location.RelativePath);
        Assert.True(File.Exists(Path.Combine(cell.Location.RootPath, cell.Location.RelativePath)));
        Assert.False(cell.IsOlderThan(item));
        Assert.True(cell.IsOlderThan(item with { Edition = 6 }));
    }

    [Theory]
    [InlineData("https://noaa-s102-pds.s3.amazonaws.com/ed3.0.0/x.h5", "https://noaa-s102-pds.s3.amazonaws.com/", "ed3.0.0/x.h5")]
    [InlineData("https://bucket.s3.us-east-1.amazonaws.com/a/b", "https://bucket.s3.us-east-1.amazonaws.com/", "a/b")]
    [InlineData("https://s3.amazonaws.com/bucket/a/b", "https://s3.amazonaws.com/bucket/", "a/b")]
    public void S3_urls_parse_into_bucket_and_key(string url, string bucket, string key)
    {
        Assert.True(S3ObjectListing.TryParse(new Uri(url), out var bucketUri, out var parsedKey));
        Assert.Equal(new Uri(bucket), bucketUri);
        Assert.Equal(key, parsedKey);
    }

    [Theory]
    [InlineData("https://charts.noaa.gov/ENCs/ENCProdCat.xml")]
    [InlineData("https://example.amazonaws.com/x")]
    public void Other_urls_are_not_S3(string url)
    {
        Assert.False(S3ObjectListing.TryParse(new Uri(url), out _, out _));
    }

    [Fact]
    public void The_filter_matches_folders_below_a_selection_and_any_listed_purpose()
    {
        var filter = new S100CatalogueFilter { Folders = ["Northeast/"], NavigationPurposes = ["port"] };

        Assert.True(filter.Matches("Northeast/Boston", "PORT"));
        Assert.False(filter.Matches("Northeast/Boston", "transit"));
        Assert.False(filter.Matches("Northeastern/Boston", "port"));
        Assert.True(S100CatalogueFilter.All.Matches("anything", null));
        Assert.Equal(filter, new S100CatalogueFilter { Folders = ["NORTHEAST"], NavigationPurposes = ["Port"] });
    }

    /// <summary>
    /// Serves the catalogue (gzip-encoded, with an ETag), a <c>ListObjectsV2</c>
    /// listing of the fixture's datasets (two per page), and the datasets themselves.
    /// </summary>
    private sealed class BucketServer(byte[] catalogue) : HttpMessageHandler
    {
        private const string Tag = "\"c1\"";

        private static readonly string[] Keys =
        [
            "ed3.0.0/California/Newport/102US005CA1LU262227.h5",
            "ed3.0.0/Northeast/Boston/102US004MA1HE262257.h5",
            "ed3.0.0/Northeast/Boston/102US005MA1RF262267.h5",
            "ed3.0.0/Oregon/Newport/102US005OR2MC262247.h5",
            "ed3.0.0/S100_ROOT/CATALOG.XML",
            "ed3.0.0/Southeast/Wilmington/102US004SC1EV262247.h5",
        ];

        public List<string> ListedPrefixes { get; } = [];

        public bool ListingFails { get; set; }

        public static long SizeOf(string key) => 1000 + key.Length;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (uri.AbsolutePath == "/" && uri.Query.Contains("list-type=2", StringComparison.Ordinal))
                return Task.FromResult(List(uri));

            if (uri.AbsolutePath.EndsWith("/CATALOG.XML", StringComparison.Ordinal))
            {
                if (request.Headers.IfNoneMatch.Any(t => t.Tag == Tag))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(catalogue) };
                response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(Tag);
                return Task.FromResult(response);
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent("\x89HDF\r\n\x1a\n"u8.ToArray()),
            });
        }

        private HttpResponseMessage List(Uri uri)
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
            var prefix = query["prefix"] ?? string.Empty;
            lock (ListedPrefixes)
                ListedPrefixes.Add(prefix);
            if (ListingFails)
                return new HttpResponseMessage(HttpStatusCode.Forbidden);

            var matching = Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            var start = int.Parse(query["continuation-token"] ?? "0", CultureInfo.InvariantCulture);
            var page = matching.Skip(start).Take(2).ToArray();
            var more = start + page.Length < matching.Length;

            var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>")
                .Append("<ListBucketResult xmlns=\"http://s3.amazonaws.com/doc/2006-03-01/\"><Name>noaa-s102-pds</Name>")
                .Append(CultureInfo.InvariantCulture, $"<Prefix>{prefix}</Prefix><IsTruncated>{(more ? "true" : "false")}</IsTruncated>");
            if (more)
                xml.Append(CultureInfo.InvariantCulture, $"<NextContinuationToken>{start + page.Length}</NextContinuationToken>");
            foreach (var key in page)
            {
                xml.Append(CultureInfo.InvariantCulture,
                    $"<Contents><Key>{key}</Key><LastModified>2026-09-04T23:56:57.000Z</LastModified><Size>{SizeOf(key)}</Size></Contents>");
            }

            xml.Append("</ListBucketResult>");
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(xml.ToString(), Encoding.UTF8, "application/xml") };
        }
    }
}
