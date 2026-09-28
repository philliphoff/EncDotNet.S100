using System.IO.Compression;
using System.Net;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Feeds;
using EncDotNet.S100.Collections;
using EncDotNet.S100.Collections.Downloads;
using EncDotNet.S100.Collections.Feeds;
using EncDotNet.S100.Collections.Indexing;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>s100 feed export</c> (issue #680): a static feed that a
/// plain file host can serve, updated incrementally on re-export.
/// </summary>
public sealed class FeedExportTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "feed-export-" + Guid.NewGuid().ToString("N"));

    public FeedExportTests() => Directory.CreateDirectory(_root);

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

    private string Output => Path.Combine(_root, "site");

    /// <summary>An S-57 exchange set with an update, a zipped S-101 exchange set, and a loose S-57 cell.</summary>
    private string PublishedFolder()
    {
        var folder = Path.Combine(_root, "published");
        ZipFile.ExtractToDirectory(TestData("US4OH1MK.zip"), Path.Combine(folder, "ohio"));
        File.Copy(TestData("S101.zip"), Path.Combine(folder, "S101.zip"));
        File.Copy(TestData("US5MA1BO.000"), Path.Combine(folder, "US5MA1BO.000"));
        return folder;
    }

    private static async Task<FeedExportResult> ExportAsync(string folder, string output) =>
        await FeedExporter.ExportAsync(await new FeedPublisher(folder, "Static charts").GetAsync(force: true), output);

    [Fact]
    public async Task An_export_is_a_feed_a_plain_file_host_can_serve()
    {
        var result = await ExportAsync(PublishedFolder(), Output);

        Assert.Equal(result.Items, result.Written);
        Assert.Equal(0, result.Failed);
        Assert.True(result.TotalBytes > 0);
        Assert.True(File.Exists(Path.Combine(Output, FeedExporter.ManifestFileName)));
        Assert.Empty(Directory.EnumerateFiles(Output, "*.tmp", SearchOption.AllDirectories));

        // Every listed item has its zip.
        await using (var json = File.OpenRead(Path.Combine(Output, S100Feed.FileName)))
        {
            var feed = S100Feed.Read(json);
            Assert.Equal("Static charts", feed.Title);
            Assert.All(feed.Items, i => Assert.True(File.Exists(
                Path.Combine(Output, ((RemoteItemLocation)i.Location).Uri.OriginalString))));
        }

        // A library reads it from a static host and downloads from it.
        var feedUri = new Uri("https://static.test/charts/feed.json");
        using var http = new HttpClient(new StaticHost(Output, "/charts/"));
        var index = await CollectionIndexer.CreateDefault(feeds: [new S100FeedIndexer(http, Path.Combine(_root, "cache"))])
            .IndexAsync(new S100FeedSource(Guid.NewGuid(), null, feedUri, S100FeedFilter.All));
        Assert.Equal(result.Items, index.Items.Count);

        var cell = index.Items.Single(i => i.Name == "US4OH1MK");
        var remote = (RemoteItemLocation)cell.Location;
        var downloaded = await new EncCellDownloader(http, Path.Combine(_root, "downloads", remote.DownloadFolder!)).DownloadAsync(cell);
        var local = downloaded.Datasets["US4OH1MK"];
        Assert.Equal(["US4OH1MK/US4OH1MK.001"], local.UpdateRelativePaths);
    }

    [Fact]
    public async Task Re_exporting_rewrites_only_what_changed_and_removes_what_is_gone()
    {
        var folder = PublishedFolder();
        var first = await ExportAsync(folder, Output);
        var userFile = Path.Combine(Output, "items", "README.txt");
        File.WriteAllText(userFile, "kept");

        // Unchanged: nothing is rewritten.
        var second = await ExportAsync(folder, Output);
        Assert.Equal((0, first.Items, 0), (second.Written, second.Unchanged, second.Removed));

        // A changed file rewrites its item; a deleted dataset loses its zip.
        var update = Path.Combine(folder, "ohio", "ENC_ROOT", "US4OH1MK", "US4OH1MK.001");
        File.SetLastWriteTimeUtc(update, File.GetLastWriteTimeUtc(update).AddMinutes(5));
        File.Delete(Path.Combine(folder, "US5MA1BO.000"));

        var third = await ExportAsync(folder, Output);

        Assert.Equal(1, third.Written);
        Assert.Equal(1, third.Removed);
        Assert.Equal(first.Items - 1, third.Items);
        Assert.Equal(third.Items, Directory.EnumerateFiles(Path.Combine(Output, "items"), "*.zip").Count());
        Assert.True(File.Exists(userFile));
        await using var json = File.OpenRead(Path.Combine(Output, S100Feed.FileName));
        Assert.DoesNotContain(S100Feed.Read(json).Items, i => i.Name == "US5MA1BO");
    }

    [Fact]
    public void Settings_are_validated()
    {
        var folder = PublishedFolder();

        Assert.Contains("--out is required",
            new FeedExportCommand.Settings { Path = folder }.Validate().Message);
        Assert.Contains("must not be inside",
            new FeedExportCommand.Settings { Path = folder, Output = Path.Combine(folder, "site") }.Validate().Message);
        Assert.True(new FeedExportCommand.Settings { Path = folder, Output = Output }.Validate().Successful);
    }

    /// <summary>Serves files under <paramref name="root"/> for URL paths under <paramref name="prefix"/>, like a static web host.</summary>
    private sealed class StaticHost(string root, string prefix) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri!.AbsolutePath;
            var file = path.StartsWith(prefix, StringComparison.Ordinal)
                ? Path.Combine(root, path[prefix.Length..].Replace('/', Path.DirectorySeparatorChar))
                : null;
            return Task.FromResult(file is not null && File.Exists(file)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
