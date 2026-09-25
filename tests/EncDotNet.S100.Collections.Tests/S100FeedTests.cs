using System.IO.Compression;
using System.Net;
using System.Text.Json;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Collections.Tests;

public sealed class S100FeedTests : IDisposable
{
    private static readonly Uri FeedUri = new("http://machine.test:8080/feed.json");

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    /// <summary>A published S-57 exchange set with an update: the NOAA US4OH1MK fixture, extracted.</summary>
    private string S57Set()
    {
        var root = Path.Combine(_temp.Path, "published");
        ZipFile.ExtractToDirectory(TestPaths.Fixture("US4OH1MK.zip"), root);
        return Path.Combine(root, "ENC_ROOT");
    }

    private static async Task<SourceIndex> IndexAsync(string path) =>
        await CollectionIndexer.CreateDefault().IndexAsync(new ExchangeSetSource(Guid.NewGuid(), null, path));

    [Fact]
    public async Task A_feed_publishes_local_items_as_relative_downloads_with_their_layout()
    {
        var index = await IndexAsync(S57Set());

        var feed = S100Feed.FromIndex(index, "Ohio charts", DateTimeOffset.UnixEpoch);

        Assert.Equal(S100Feed.FormatName, feed.Format);
        Assert.Equal(index.Fingerprint, feed.Fingerprint);
        var item = Assert.Single(feed.Items);
        Assert.Equal("US4OH1MK", item.Name);
        Assert.Equal(1, item.Update);
        var remote = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.False(remote.Uri.IsAbsoluteUri);
        Assert.Equal($"items/{remote.Package}.zip", remote.Uri.OriginalString);
        Assert.Equal(S100Feed.ItemId(index.Items[0]), remote.Package);
        Assert.Equal("US4OH1MK/US4OH1MK.000", remote.Layout!.RelativePath);
        Assert.Equal(["US4OH1MK/US4OH1MK.001"], remote.Layout.UpdateRelativePaths);
        Assert.Equal("CATALOG.031", remote.Layout.CatalogueRelativePath);
        Assert.True(remote.SizeBytes > 4_000);
        Assert.NotNull(remote.LastModified);
    }

    [Fact]
    public async Task A_feed_round_trips_and_resolves_item_urls_against_the_feed()
    {
        var feed = S100Feed.FromIndex(await IndexAsync(S57Set()), "Ohio charts");
        using var stream = new MemoryStream();
        S100Feed.Write(stream, feed);
        stream.Position = 0;

        var read = S100Feed.Read(stream, FeedUri);

        Assert.Equal("Ohio charts", read.Title);
        var item = Assert.Single(read.Items);
        var remote = Assert.IsType<RemoteItemLocation>(item.Location);
        Assert.Equal(new Uri(FeedUri, ((RemoteItemLocation)feed.Items[0].Location).Uri), remote.Uri);
        Assert.Equal(((RemoteItemLocation)feed.Items[0].Location).Layout!.RelativePath, remote.Layout!.RelativePath);
        Assert.Equal(feed.Items[0].Bounds, item.Bounds);
    }

    [Theory]
    [InlineData("""{ "format": "something-else", "version": 1, "items": [] }""", typeof(JsonException))]
    [InlineData("""{ "format": "encdotnet-s100-feed", "version": 99, "items": [] }""", typeof(NotSupportedException))]
    public void Other_documents_and_newer_versions_are_rejected(string json, Type exception)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Throws(exception, () => S100Feed.Read(stream));
    }

    [Fact]
    public async Task Item_zips_hold_the_catalogue_base_and_updates_from_a_folder_or_a_zip()
    {
        var folderItem = (await IndexAsync(S57Set())).Items[0];
        var zipItem = (await IndexAsync(TestPaths.Dataset("S101.zip"))).Items[0];
        Assert.True(((LocalItemLocation)zipItem.Location).IsZip);

        foreach (var item in new[] { folderItem, zipItem })
        {
            var location = (LocalItemLocation)item.Location;
            using var buffer = new MemoryStream();
            await S100FeedPackager.WriteZipAsync(buffer, location);
            buffer.Position = 0;

            using var zip = new ZipArchive(buffer);
            Assert.Equal(S100FeedPackager.Files(location), zip.Entries.Select(e => e.FullName));
            Assert.Contains(location.RelativePath, zip.Entries.Select(e => e.FullName));
        }
    }

    [Fact]
    public void Paths_that_leave_the_root_are_refused()
    {
        var location = new LocalItemLocation(_temp.Path, "../secret.000", []);

        Assert.Throws<InvalidDataException>(() => S100FeedPackager.Files(location));
    }

    [Fact]
    public async Task A_published_item_downloads_into_its_layout_on_another_machine()
    {
        var published = S100Feed.FromIndex(await IndexAsync(S57Set()), "Ohio charts");
        var source = (await IndexAsync(Path.Combine(_temp.Path, "published", "ENC_ROOT"))).Items[0];
        using var feedJson = new MemoryStream();
        S100Feed.Write(feedJson, published);
        feedJson.Position = 0;
        var item = S100Feed.Read(feedJson, FeedUri).Items[0];

        var downloader = new EncCellDownloader(
            new HttpClient(new PackagerServer((LocalItemLocation)source.Location)), Path.Combine(_temp.Path, "downloads"));
        var downloaded = await downloader.DownloadAsync(item);

        var remote = (RemoteItemLocation)item.Location;
        Assert.False(downloaded.IsPackage);
        Assert.Equal((1, 1), (downloaded.Edition, downloaded.Update));
        var location = downloaded.Datasets["US4OH1MK"];
        Assert.Equal(Path.Combine(downloader.Root, remote.Package!), location.RootPath);
        Assert.Equal(remote.Layout!.RelativePath, location.RelativePath);
        Assert.Equal(remote.Layout.UpdateRelativePaths, location.UpdateRelativePaths);
        Assert.Equal("CATALOG.031", location.CatalogueRelativePath);

        // The download re-indexes like the original.
        var reindexed = Assert.Single((await IndexAsync(location.RootPath)).Items);
        Assert.Equal((source.Name, source.Edition, source.Update, source.Bounds), (reindexed.Name, reindexed.Edition, reindexed.Update, reindexed.Bounds));
    }

    [Theory]
    [InlineData("../escape", "US4OH1MK/US4OH1MK.000")]
    [InlineData("abc", "../../US4OH1MK.000")]
    public async Task Unsafe_names_and_layouts_from_a_publisher_are_refused(string package, string relativePath)
    {
        var source = (await IndexAsync(S57Set())).Items[0];
        var item = source with
        {
            Location = new RemoteItemLocation(
                new Uri("http://machine.test/items/x.zip"), Package: package, Layout: new PackageLayout(relativePath, [])),
        };
        var downloader = new EncCellDownloader(
            new HttpClient(new PackagerServer((LocalItemLocation)source.Location)), Path.Combine(_temp.Path, "downloads"));

        await Assert.ThrowsAsync<InvalidDataException>(() => downloader.DownloadAsync(item));
        Assert.False(Directory.Exists(Path.Combine(_temp.Path, "escape")));
    }

    /// <summary>Serves the item zip the way <c>s100 feed serve</c> will.</summary>
    private sealed class PackagerServer(LocalItemLocation location) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var buffer = new MemoryStream();
            await S100FeedPackager.WriteZipAsync(buffer, location, cancellationToken);
            buffer.Position = 0;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(buffer) };
        }
    }
}
