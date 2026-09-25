using System.IO.Compression;
using System.Net;
using System.Xml;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Persistence;
using EncDotNet.S100.Collections.Usace;

namespace EncDotNet.S100.Collections.Tests;

public sealed class UsaceIencFeedIndexerTests : IDisposable
{
    private readonly TempDirectory _cache = new();
    private readonly CountingServer _server = new(File.ReadAllBytes(TestPaths.Fixture("usace-ienc-u37.xml")));
    private readonly UsaceIencFeedIndexer _feeds;
    private readonly CollectionIndexer _indexer;

    public UsaceIencFeedIndexerTests()
    {
        _feeds = new UsaceIencFeedIndexer(new HttpClient(_server), _cache.Path);
        _indexer = CollectionIndexer.CreateDefault(feeds: [_feeds]);
    }

    public void Dispose() => _cache.Dispose();

    private static UsaceIencFeedSource Feed(UsaceIencFilter? filter = null) =>
        new(Guid.NewGuid(), null, UsaceIencFeedSource.RiversCatalogUri, filter ?? UsaceIencFilter.All);

    [Fact]
    public void Reader_reads_river_cells()
    {
        var catalog = UsaceIencProductCatalogReader.Read(TestPaths.Fixture("usace-ienc-u37.xml"));

        Assert.Equal("IENC U37 Product Catalog", catalog.Title);
        Assert.Equal(new DateOnly(2026, 9, 17), catalog.CreatedOn);
        var cell = catalog.Cells[0];
        Assert.Equal("U37AG001", cell.Name);
        Assert.Equal("Allegheny", cell.River);
        Assert.Equal("Pittsburgh, PA", cell.From);
        Assert.Equal("Allegheny Lock No. 8", cell.To);
        Assert.Equal((1d, 46d), (cell.RiverMileBegin, cell.RiverMileEnd));
        Assert.Equal(new GeoBounds(40.44224, -80.012797, 40.821667, -79.514143), cell.Bounds);
        Assert.Equal((22, 16), (cell.Edition, cell.Update));
        Assert.Equal(new Uri("https://ienccloud.us/ienc/products/files/u37/ienc_s57/U37AG001.zip"), cell.ZipUri);
        Assert.Equal(new DateOnly(2026, 6, 30), cell.PostedOn);
        Assert.Equal((long)(7.7 * 1024 * 1024), cell.ZipSize);
    }

    [Fact]
    public void Reader_reads_the_buoy_catalogue()
    {
        var catalog = UsaceIencProductCatalogReader.Read(TestPaths.Fixture("usace-ienc-buoy.xml"));

        var cell = Assert.Single(catalog.Cells);
        Assert.Equal("3UABUOYS", cell.Name);
        Assert.Null(cell.River);
        Assert.Equal(82, cell.Edition);
        Assert.Equal(0, cell.Update);
        Assert.NotNull(cell.Bounds);
    }

    [Fact]
    public void Reader_rejects_other_catalogues()
    {
        using var stream = new MemoryStream("<EncProductCatalog/>"u8.ToArray());

        Assert.Throws<XmlException>(() => UsaceIencProductCatalogReader.Read(stream));
    }

    [Theory]
    [InlineData("22.16", 22, 16)]
    [InlineData("48.10", 48, 10)]
    [InlineData("82.0", 82, 0)]
    [InlineData("394", 394, 0)]
    [InlineData("", null, null)]
    public void ParseEdition_splits_edition_and_update(string text, int? edition, int? update)
    {
        Assert.Equal((edition, update), UsaceIencProductCatalogReader.ParseEdition(text));
    }

    [Theory]
    [InlineData("7.7 MB", 8_074_035L)]
    [InlineData("7. 7 MB", 8_074_035L)]  // as published for U37TN564
    [InlineData("0.3 MB", 314_573L)]
    [InlineData("512 KB", 524_288L)]
    [InlineData("", null)]
    [InlineData("big", null)]
    public void ParseSize_reads_human_sizes(string text, long? bytes)
    {
        Assert.Equal(bytes, UsaceIencProductCatalogReader.ParseSize(text));
    }

    [Fact]
    public async Task Indexes_cells_as_online_items_with_bounds_and_titles()
    {
        var index = await _indexer.IndexAsync(Feed());

        Assert.Equal(["U37AG001", "U37AR001", "U37OH001", "U37OH012"], index.Items.Select(i => i.Name));
        var item = index.Items[0];
        Assert.Equal("S-57", item.ProductSpec);
        Assert.Equal("Pittsburgh, PA → Allegheny Lock No. 8 (Allegheny, mi 1–46)", item.Title);
        Assert.Equal((22, 16), (item.Edition, item.Update));
        Assert.NotNull(item.Bounds);
        Assert.Null(item.Coverage);
        Assert.Equal("Allegheny", item.Properties["river"]);
        var location = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.Equal((long)(7.7 * 1024 * 1024), location.SizeBytes);
        Assert.StartsWith("usace-v1:", index.Fingerprint);
    }

    [Fact]
    public async Task River_filter_selects_cells_and_changing_it_does_not_redownload()
    {
        var source = Feed();
        var all = await _indexer.IndexAsync(source);

        var ohio = await _indexer.IndexAsync(source with { Filter = new UsaceIencFilter { Rivers = ["ohio"] } }, all);

        Assert.Equal(["U37OH001", "U37OH012"], ohio.Items.Select(i => i.Name));
        Assert.Equal(1, _server.Requests);
    }

    [Fact]
    public async Task Rivers_facet_counts_cells_and_sizes()
    {
        var catalog = await _feeds.GetCatalogAsync(UsaceIencFeedSource.RiversCatalogUri);

        var rivers = UsaceIencFeedIndexer.Rivers(catalog);

        Assert.Equal(["Allegheny", "Arkansas", "Ohio"], rivers.Select(r => r.Value));
        var ohio = rivers.Single(r => r.Value == "Ohio");
        Assert.Equal(2, ohio.CellCount);
        Assert.Equal((long)(2.4 * 1024 * 1024) + (long)(3.9 * 1024 * 1024), ohio.TotalBytes);
    }

    [Fact]
    public void Source_round_trips_through_the_store()
    {
        var source = new UsaceIencFeedSource(Guid.NewGuid(), "Ohio", UsaceIencFeedSource.RiversCatalogUri,
            new UsaceIencFilter { Rivers = ["Ohio", "Tennessee"] });
        var document = new CollectionStoreDocument(1, [new DatasetCollection(Guid.NewGuid(), "Inland", [source], DateTimeOffset.UnixEpoch)]);

        var json = CollectionJson.SerializeStore(document);
        var restored = CollectionJson.DeserializeStore(json).Collections[0].Sources[0];

        Assert.Contains("\"kind\": \"usaceIencFeed\"", json);
        Assert.Equal(source, restored);
    }

    [Theory]
    [InlineData("ENC_ROOT/U37AR001/CATALOG.031", "ENC_ROOT/U37AR001/U37AR001.000", "ENC_ROOT/U37AR001", "U37AR001.000")]  // river cell
    [InlineData("ENC_ROOT/CATALOG.031", "ENC_ROOT/3UABUOYS.000", "ENC_ROOT", "3UABUOYS.000")]                            // buoy overlay
    public async Task Downloader_handles_USACE_zip_layouts(string catalogue, string cell, string root, string relative)
    {
        // Real USACE layouts, synthesised around a real cell's bytes.
        var name = Path.GetFileNameWithoutExtension(cell);
        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var source = ZipFile.OpenRead(TestPaths.Fixture("US4OH1MK.zip")))
            {
                await using var from = source.GetEntry("ENC_ROOT/US4OH1MK/US4OH1MK.000")!.Open();
                await using var to = archive.CreateEntry(cell).Open();
                await from.CopyToAsync(to);
            }
            using var writer = new StreamWriter(archive.CreateEntry(catalogue).Open());
            await writer.WriteAsync("catalogue");
        }

        using var downloads = new TempDirectory();
        var downloader = new EncCellDownloader(new HttpClient(new CountingServer(zip.ToArray())), downloads.Path);
        var item = new CollectionItem
        {
            Key = name,
            ProductSpec = "S-57",
            Name = name,
            Location = new RemoteItemLocation(new Uri($"https://ienccloud.us/x/{name}.zip")),
        };

        var downloaded = await downloader.DownloadAsync(item);

        Assert.Equal(Path.Combine(downloads.Path, name, root.Replace('/', Path.DirectorySeparatorChar)), downloaded.Location.RootPath);
        Assert.Equal(relative, downloaded.Location.RelativePath);
        Assert.Equal("CATALOG.031", downloaded.Location.CatalogueRelativePath);
    }

    private sealed class CountingServer(byte[] body) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }
}
