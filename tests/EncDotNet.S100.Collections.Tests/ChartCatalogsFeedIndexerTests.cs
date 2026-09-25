using System.IO.Compression;
using System.Net;
using System.Xml;
using EncDotNet.S100.Collections.ChartCatalogs;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Indexing;
using EncDotNet.S100.Collections.Persistence;

namespace EncDotNet.S100.Collections.Tests;

public sealed class ChartCatalogsFeedIndexerTests : IDisposable
{
    private static readonly Uri ListUri = new("https://example.test/lists/TEST_IENC_Catalog.xml");

    private readonly TempDirectory _cache = new();
    private readonly TempDirectory _downloads = new();
    private readonly ListServer _server = new(File.ReadAllBytes(TestPaths.Fixture("chartcatalogs-list.xml")), PackageZip());
    private readonly ChartCatalogsFeedIndexer _feeds;
    private readonly CollectionIndexer _indexer;

    public ChartCatalogsFeedIndexerTests()
    {
        _feeds = new ChartCatalogsFeedIndexer(new HttpClient(_server), _cache.Path, _downloads.Path);
        _indexer = CollectionIndexer.CreateDefault(feeds: [_feeds]);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _downloads.Dispose();
    }

    private static ChartCatalogsFeedSource Feed(ChartCatalogsFilter? filter = null) =>
        new(Guid.NewGuid(), null, ListUri, filter ?? ChartCatalogsFilter.All);

    /// <summary>A package: the US4OH1MK exchange set nested one folder deep, as some authorities zip them.</summary>
    private static byte[] PackageZip()
    {
        using var source = ZipFile.OpenRead(TestPaths.Fixture("US4OH1MK.zip"));
        using var buffer = new MemoryStream();
        using (var package = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in source.Entries)
            {
                using var from = entry.Open();
                using var to = package.CreateEntry("Base1/" + entry.FullName).Open();
                from.CopyTo(to);
            }
        }

        return buffer.ToArray();
    }

    [Fact]
    public void Reader_reads_entries_and_skips_those_without_a_download()
    {
        var catalog = ChartCatalogsProductCatalogReader.Read(TestPaths.Fixture("chartcatalogs-list.xml"));

        Assert.Equal("Test Inland ENC Charts", catalog.Title);
        Assert.Equal(new DateTimeOffset(2026, 9, 20, 3, 35, 8, TimeSpan.Zero), catalog.ValidAt);
        Assert.Equal(["Base1", "Base2", "XX5RIV01", "XX5RIV01"], catalog.Charts.Select(c => c.Number));

        var first = catalog.Charts[0];
        Assert.Equal("River 790 - 0 (Base1)", first.Title);
        Assert.Equal(new Uri("https://example.test/charts/1726821571.zip"), first.DownloadUri);
        Assert.Equal(new DateTimeOffset(2024, 6, 12, 0, 0, 0, TimeSpan.Zero), first.PublishedAt);

        // Falls back to the compact date form; keeps the target file name.
        var second = catalog.Charts[1];
        Assert.Equal(new DateTimeOffset(2024, 8, 23, 12, 0, 0, TimeSpan.Zero), second.PublishedAt);
        Assert.Equal("20240823_River_week 34.zip", second.TargetFileName);
    }

    [Fact]
    public void Reader_rejects_other_catalogues()
    {
        using var stream = new MemoryStream("<EncProductCatalog/>"u8.ToArray());

        Assert.Throws<XmlException>(() => ChartCatalogsProductCatalogReader.Read(stream));
    }

    [Fact]
    public async Task Entries_index_as_online_packages_without_bounds()
    {
        var index = await _indexer.IndexAsync(Feed());

        Assert.Empty(index.Diagnostics);
        // The repeated entry appears once.
        Assert.Equal(["Base1", "Base2", "XX5RIV01"], index.Items.Select(i => i.Name));
        var item = index.Items[0];
        Assert.Equal("River 790 - 0 (Base1)", item.Title);
        Assert.Null(item.Bounds);
        Assert.Equal(new DateOnly(2024, 6, 12), item.IssueDate);
        var remote = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.Equal("community/TEST_IENC_Catalog", remote.DownloadFolder);
        Assert.Equal("Base1", remote.Package);
        Assert.Equal(new DateTimeOffset(2024, 6, 12, 0, 0, 0, TimeSpan.Zero), remote.LastModified);
    }

    [Fact]
    public async Task Filter_selects_entries_by_number()
    {
        var index = await _indexer.IndexAsync(Feed(new ChartCatalogsFilter { Charts = ["base2"] }));

        Assert.Equal("Base2", Assert.Single(index.Items).Name);
    }

    [Fact]
    public async Task A_downloaded_package_lists_its_cells_with_bounds()
    {
        var source = Feed(new ChartCatalogsFilter { Charts = ["Base1"] });
        var before = await _indexer.IndexAsync(source);
        var package = Assert.Single(before.Items);

        var downloader = new EncCellDownloader(
            new HttpClient(_server), Path.Combine(_downloads.Path, ((RemoteItemLocation)package.Location).DownloadFolder!));
        var downloaded = await downloader.DownloadAsync(package);

        Assert.True(downloaded.IsPackage);
        Assert.Equal("Base1", downloaded.Name);
        var cellLocation = downloaded.Datasets["US4OH1MK"];
        Assert.Equal(Path.Combine(downloader.Root, "Base1", "Base1", "ENC_ROOT"), cellLocation.RootPath);
        Assert.Equal("US4OH1MK/US4OH1MK.000", cellLocation.RelativePath);

        // The download changes the fingerprint, so the source re-indexes.
        var after = await _indexer.IndexAsync(source, before);

        Assert.NotSame(before, after);
        var cell = Assert.Single(after.Items);
        Assert.Equal("US4OH1MK", cell.Name);
        Assert.Equal("Base1/US4OH1MK", cell.Key);
        Assert.StartsWith("Lake Erie", cell.Title); // the cell's own title, from its catalogue
        Assert.Equal("River 790 - 0 (Base1)", cell.Properties["packageTitle"]);
        Assert.Equal(1, cell.Update);
        Assert.NotNull(cell.Bounds);
        var remote = Assert.IsType<RemoteItemLocation>(cell.Location);
        Assert.Equal("Base1", remote.Package);

        // Unchanged afterwards.
        Assert.Same(after, await _indexer.IndexAsync(source, after));
    }

    [Fact]
    public void Source_round_trips_through_json()
    {
        var collection = new DatasetCollection(
            Guid.NewGuid(), "Test", [Feed(new ChartCatalogsFilter { Charts = ["Base1"] })], DateTimeOffset.UnixEpoch);

        var json = CollectionJson.SerializeStore(new CollectionStoreDocument(CollectionStoreDocument.CurrentVersion, [collection]));
        var loaded = Assert.IsType<ChartCatalogsFeedSource>(Assert.Single(Assert.Single(
            CollectionJson.DeserializeStore(json).Collections).Sources));

        Assert.Contains("\"chartCatalogsFeed\"", json);
        Assert.Equal(ListUri, loaded.CatalogUri);
        Assert.Equal(["Base1"], loaded.Filter.Charts);
    }

    [Theory]
    [InlineData("Base1", "Base1")]
    [InlineData("a/b:c", "a_b_c")]
    [InlineData(" ..", "_")]
    public void Package_names_are_safe_for_the_file_system(string number, string expected)
    {
        var chart = new ChartCatalogsChart { Number = number, DownloadUri = new Uri("https://example.test/x.zip") };

        Assert.Equal(expected, ChartCatalogsFeedIndexer.PackageName(chart));
    }

    /// <summary>Serves the list for <c>.xml</c> requests, a bare cell for <c>.000</c>, and the package zip otherwise.</summary>
    private sealed class ListServer(byte[] list, byte[] package) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            byte[] body = path.EndsWith(".xml", StringComparison.Ordinal) ? list
                : path.EndsWith(".000", StringComparison.Ordinal) ? File.ReadAllBytes(TestPaths.Fixture("US4OH1MK.zip"))
                : package;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
    }
}
