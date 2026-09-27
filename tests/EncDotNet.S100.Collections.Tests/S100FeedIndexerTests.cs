using System.IO.Compression;
using System.Net;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.KnownSources;
using EncDotNet.S100.Collections.Persistence;

namespace EncDotNet.S100.Collections.Tests;

public sealed class S100FeedIndexerTests : IDisposable
{
    private static readonly Uri FeedUri = new("http://machine.test:8100/token/feed.json");

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A feed of the NOAA US4OH1MK exchange set, plus one item whose location is not an http(s) download.</summary>
    private async Task<byte[]> FeedJsonAsync()
    {
        var root = Path.Combine(_temp.Path, "published");
        ZipFile.ExtractToDirectory(TestPaths.Fixture("US4OH1MK.zip"), root);
        var index = await CollectionIndexer.CreateDefault().IndexAsync(new LocalFolderSource(Guid.NewGuid(), null, root));
        var feed = S100Feed.FromIndex(index, "Ohio charts");
        var odd = feed.Items[0] with
        {
            Key = "odd",
            Name = "ODD",
            ProductSpec = "S-102",
            Location = new RemoteItemLocation(new Uri("file:///etc/passwd")),
        };
        feed = feed with { Items = [.. feed.Items, odd] };

        using var json = new MemoryStream();
        S100Feed.Write(json, feed);
        return json.ToArray();
    }

    private static S100FeedSource Source(S100FeedFilter? filter = null) =>
        new(Guid.NewGuid(), null, FeedUri, filter ?? S100FeedFilter.All);

    [Fact]
    public async Task Feed_items_index_as_online_downloads_with_their_coverage()
    {
        var server = new FeedServer(await FeedJsonAsync());
        var indexer = CollectionIndexer.CreateDefault(feeds: [new S100FeedIndexer(new HttpClient(server), _temp.Path)]);

        var index = await indexer.IndexAsync(Source());

        Assert.Empty(index.Diagnostics);
        var cell = index.Items.Single(i => i.Name == "US4OH1MK");
        Assert.NotNull(cell.Bounds);
        var remote = Assert.IsType<RemoteItemLocation>(cell.Location);
        Assert.Equal(new Uri(FeedUri, $"items/{remote.Package}.zip"), remote.Uri);
        Assert.Equal(S100FeedIndexer.DownloadFolderFor(FeedUri), remote.DownloadFolder);
        Assert.StartsWith("feeds/machine.test-8100-", remote.DownloadFolder, StringComparison.Ordinal);
        Assert.NotNull(remote.Layout);

        // A location that is not an http(s) download is listed only.
        Assert.IsType<NoItemLocation>(index.Items.Single(i => i.Name == "ODD").Location);

        // Unchanged (a 304): the previous index is reused.
        var again = await indexer.IndexAsync(Source() with { Id = index.SourceId }, index);
        Assert.Same(index, again);
    }

    [Fact]
    public async Task The_filter_selects_products_and_facets_count_them()
    {
        var server = new FeedServer(await FeedJsonAsync());
        var feeds = new S100FeedIndexer(new HttpClient(server), _temp.Path);

        var index = await CollectionIndexer.CreateDefault(feeds: [feeds])
            .IndexAsync(Source(new S100FeedFilter { ProductSpecs = ["s-57"] }));
        var products = S100FeedIndexer.Products(await feeds.GetFeedAsync(FeedUri));

        Assert.Equal("US4OH1MK", Assert.Single(index.Items).Name);
        Assert.Equal(["S-102", "S-57"], products.Select(p => p.Value));
        Assert.Equal(1, products.Single(p => p.Value == "S-57").CellCount);
    }

    [Fact]
    public void Source_round_trips_through_json()
    {
        var collection = new DatasetCollection(
            Guid.NewGuid(), "Shared", [Source(new S100FeedFilter { ProductSpecs = ["S-101"] })], DateTimeOffset.UnixEpoch);

        var json = CollectionJson.SerializeStore(new CollectionStoreDocument(CollectionStoreDocument.CurrentVersion, [collection]));
        var loaded = Assert.IsType<S100FeedSource>(Assert.Single(Assert.Single(CollectionJson.DeserializeStore(json).Collections).Sources));

        Assert.Contains("\"s100Feed\"", json);
        Assert.Equal(FeedUri, loaded.FeedUri);
        Assert.Equal(["S-101"], loaded.Filter.ProductSpecs);
    }

    [Fact]
    public async Task Feeds_are_recognised_by_their_format_property()
    {
        var json = await FeedJsonAsync();
        byte[] bom = [0xEF, 0xBB, 0xBF, (byte)'\n', (byte)' '];

        foreach (var content in new[] { json, json[..200], [.. bom, .. json] })
        {
            var probe = CatalogueFormatDetector.Probe(new MemoryStream(content));
            Assert.Equal(KnownCatalogueFormat.S100Feed, probe.Format);
            Assert.Equal("Ohio charts", probe.Title);
            Assert.True(probe.IsJson);
        }

        var other = CatalogueFormatDetector.Probe(new MemoryStream("""{ "items": [1, 2], "format": "geojson" }"""u8.ToArray()));
        Assert.Null(other.Format);
        Assert.True(other.IsJson);

        var source = KnownCatalogueSources.FromUrl(FeedUri, KnownCatalogueFormat.S100Feed, "Ohio charts");
        Assert.Equal(KnownCatalogueCoverage.Polygons, source.Coverage);
    }

    /// <summary>Serves the feed with an ETag, answering If-None-Match with 304.</summary>
    private sealed class FeedServer(byte[] json) : HttpMessageHandler
    {
        private const string Tag = "\"v1\"";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Headers.IfNoneMatch.Any(t => t.Tag == Tag))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));

            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(json) };
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(Tag);
            return Task.FromResult(response);
        }
    }
}
