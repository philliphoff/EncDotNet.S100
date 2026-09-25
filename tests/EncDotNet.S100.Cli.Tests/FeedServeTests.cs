using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Feeds;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;
using Microsoft.Extensions.Time.Testing;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>s100 feed serve</c> (issue #680): the publisher's throttled
/// re-indexing, and the HTTP server end to end on a free local port — feed,
/// ETag revalidation, the token prefix, and item downloads that land in their
/// stated layout on the "other machine".
/// </summary>
public sealed class FeedServeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "feed-serve-" + Guid.NewGuid().ToString("N"));

    public FeedServeTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static string TestData(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", name);

    /// <summary>A folder holding an S-57 exchange set (NOAA US4OH1MK with an update) and a zipped S-101 exchange set.</summary>
    private string PublishedFolder()
    {
        var folder = Path.Combine(_root, "published");
        ZipFile.ExtractToDirectory(TestData("US4OH1MK.zip"), Path.Combine(folder, "ohio"));
        File.Copy(TestData("S101.zip"), Path.Combine(folder, "S101.zip"));
        return folder;
    }

    [Fact]
    public async Task The_publisher_reindexes_at_most_once_per_interval()
    {
        var folder = PublishedFolder();
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch);
        var publisher = new FeedPublisher(folder, refreshInterval: TimeSpan.FromSeconds(10), timeProvider: clock);

        var first = await publisher.GetAsync();
        Assert.Equal("published", first.Document.Title);
        Assert.Contains(first.Document.Items, i => i.Name == "US4OH1MK");
        Assert.Contains(first.Document.Items, i => i.ProductSpec == "S-101");

        // A new dataset is only noticed once the interval has passed.
        File.Copy(TestData("US5MA1BO.000"), Path.Combine(folder, "US5MA1BO.000"));
        Assert.Same(first, await publisher.GetAsync());

        clock.Advance(TimeSpan.FromSeconds(11));
        var second = await publisher.GetAsync();

        Assert.NotEqual(first.ETag, second.ETag);
        Assert.Contains(second.Document.Items, i => i.Name == "US5MA1BO");

        // Unchanged: the same feed, even after the interval.
        clock.Advance(TimeSpan.FromSeconds(11));
        Assert.Same(second, await publisher.GetAsync());
    }

    [Fact]
    public async Task A_served_feed_revalidates_and_its_items_download_on_another_machine()
    {
        var publisher = new FeedPublisher(PublishedFolder(), "Test charts");
        await using var server = await FeedServer.StartAsync(publisher, IPAddress.Loopback, 0, "secret-token");
        using var http = new HttpClient();

        Assert.Equal($"http://127.0.0.1:{server.Port}/secret-token/feed.json", server.FeedUri.AbsoluteUri);

        // The feed, with an ETag that revalidates.
        using var response = await http.GetAsync(server.FeedUri);
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var etag = response.Headers.ETag!;
        var feed = S100Feed.Read(await response.Content.ReadAsStreamAsync(), server.FeedUri);
        Assert.Equal("Test charts", feed.Title);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, server.FeedUri);
        conditional.Headers.IfNoneMatch.Add(etag);
        using var notModified = await http.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);

        // Without the token, nothing is served; an unknown item is not found.
        Assert.Equal(HttpStatusCode.NotFound,
            (await http.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/feed.json"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await http.GetAsync(new Uri(server.FeedUri, "items/0000.zip"))).StatusCode);

        // Each product downloads into its stated layout and re-indexes like the original.
        var downloads = new EncCellDownloader(http, Path.Combine(_root, "other-machine"));
        foreach (var item in new[]
        {
            feed.Items.Single(i => i.Name == "US4OH1MK"),
            feed.Items.First(i => i.ProductSpec == "S-101"),
        })
        {
            var remote = Assert.IsType<RemoteItemLocation>(item.Location);
            Assert.StartsWith(server.FeedUri.AbsoluteUri.Replace("feed.json", "items/", StringComparison.Ordinal), remote.Uri.AbsoluteUri);

            var downloaded = await downloads.DownloadAsync(item);

            var location = downloaded.Datasets[item.Name];
            Assert.True(File.Exists(Path.Combine(location.RootPath, location.RelativePath)));
            var copy = (await CollectionIndexer.CreateDefault()
                .IndexAsync(new LocalFolderSource(Guid.NewGuid(), null, location.RootPath))).Items.Single(i => i.Name == item.Name);
            Assert.Equal((item.ProductSpec, item.Edition, item.Update), (copy.ProductSpec, copy.Edition, copy.Update));
        }
    }

    [Fact]
    public async Task The_feed_is_compressed_when_the_client_asks()
    {
        var publisher = new FeedPublisher(PublishedFolder());
        await using var server = await FeedServer.StartAsync(publisher, IPAddress.Loopback, 0, token: null);
        using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None });
        using var request = new HttpRequestMessage(HttpMethod.Get, server.FeedUri);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));

        using var response = await http.SendAsync(request);

        Assert.Equal($"http://127.0.0.1:{server.Port}/feed.json", server.FeedUri.AbsoluteUri);
        Assert.Contains("gzip", response.Content.Headers.ContentEncoding);
        await using var gzip = new GZipStream(await response.Content.ReadAsStreamAsync(), CompressionMode.Decompress);
        Assert.NotEmpty(S100Feed.Read(gzip).Items);
    }

    [Theory]
    [InlineData("127.0.0.1", null, false, false)]
    [InlineData("0.0.0.0", null, false, true)]
    [InlineData("0.0.0.0", null, true, false)]
    [InlineData("127.0.0.1", "given", false, true)]
    public void A_token_is_generated_only_when_serving_beyond_this_machine(string host, string? token, bool noToken, bool expectToken)
    {
        var settings = new FeedServeCommand.Settings { Path = ".", Host = host, Token = token, NoToken = noToken };

        var resolved = FeedServeCommand.ResolveToken(settings, IPAddress.Parse(host));

        Assert.Equal(expectToken, resolved is not null);
        if (token is not null)
            Assert.Equal(token, resolved);
        else if (resolved is not null)
            Assert.Matches("^[A-Za-z0-9_-]{16}$", resolved);
    }

    [Theory]
    [InlineData("missing-folder", "127.0.0.1", null, "does not exist")]
    [InlineData(".", "localhost", null, "not an IP address")]
    [InlineData(".", "127.0.0.1", "bad/token", "--token may contain")]
    public void Settings_are_validated(string path, string host, string? token, string error)
    {
        var settings = new FeedServeCommand.Settings { Path = path, Host = host, Token = token };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains(error, result.Message);
    }
}
