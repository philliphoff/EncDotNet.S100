using System.Net;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Mirror;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// <c>s100 tides publish</c> and <c>s100 tides update --from</c> (#1674): a
/// mirror's files written from NOAA's Puget Sound stations
/// (tests/datasets/Tides/Stations/puget-sound.json), and read back through a
/// fake mirror.
/// </summary>
[Collection(ConsoleCollection.Name)]
public sealed class TidesMirrorCommandTests : IDisposable
{
    private const string Seattle = "9447130";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "s100-tides-mirror-" + Guid.NewGuid().ToString("N"));

    public TidesMirrorCommandTests() => Directory.CreateDirectory(_directory);

    private static string Catalogue => Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "puget-sound.json");

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        TidesUpdateCommand.HttpFactory = () => new HttpClient();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Publish_writes_the_catalogue_and_a_manifest_that_matches_it()
    {
        var output = Path.Combine(_directory, "out");
        var summary = Path.Combine(_directory, "summary.md");

        var (exit, stdout, _) = Run(["tides", "publish", output, "--catalogue", Catalogue, "--crawled", "--summary", summary]);

        Assert.Equal(0, exit);
        var content = await File.ReadAllBytesAsync(Path.Combine(output, TideCatalogueManifest.DefaultCatalogueFileName), Token);
        var manifest = await TideCatalogueManifest.LoadAsync(Path.Combine(output, TideCatalogueManifest.DefaultFileName), Token);
        Assert.True(manifest.Matches(content));
        Assert.Equal((byte)0x1f, content[0]);
        Assert.Equal("NOAA CO-OPS", manifest.Provider);
        Assert.Equal(new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero), manifest.Retrieved);
        Assert.True(manifest.Crawled > manifest.Retrieved);
        Assert.Equal(5, manifest.StationCount);
        Assert.Contains("First publication.", stdout);
        Assert.Contains("| Tide stations | 5 (2 harmonic, 3 subordinate) |", await File.ReadAllTextAsync(summary, Token));
    }

    [Fact]
    public async Task Publish_keeps_the_published_file_when_the_stations_have_not_changed()
    {
        var published = Publish("published", Catalogue, crawled: true);
        var before = await TideCatalogueManifest.LoadAsync(Path.Combine(published, TideCatalogueManifest.DefaultFileName), Token);

        // The same stations, downloaded again later.
        var redownloaded = await SaveAsync("redownloaded.json.gz", c => c, retrieved: new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        var output = Path.Combine(_directory, "out");
        var (exit, stdout, _) = Run(["tides", "publish", output, "--catalogue", redownloaded, "--previous", published]);

        Assert.Equal(0, exit);
        Assert.Contains("No change: the provider has no newer data.", stdout);
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(published, TideCatalogueManifest.DefaultCatalogueFileName), Token),
            await File.ReadAllBytesAsync(Path.Combine(output, TideCatalogueManifest.DefaultCatalogueFileName), Token));
        var after = await TideCatalogueManifest.LoadAsync(Path.Combine(output, TideCatalogueManifest.DefaultFileName), Token);
        Assert.Equal(before.Sha256, after.Sha256);
        Assert.Equal(before.Retrieved, after.Retrieved);
        Assert.Equal(before.Crawled, after.Crawled);
        Assert.True(after.Checked >= before.Checked);

        // Downloaded in full again: only the crawl time moves.
        var (again, againOut, _) = Run(["tides", "publish", output, "--catalogue", redownloaded, "--previous", published, "--crawled"]);
        Assert.Equal(0, again);
        Assert.Contains("No change: downloaded again", againOut);
        var crawled = await TideCatalogueManifest.LoadAsync(Path.Combine(output, TideCatalogueManifest.DefaultFileName), Token);
        Assert.Equal(before.Retrieved, crawled.Retrieved);
        Assert.True(crawled.Crawled > before.Crawled);
    }

    [Fact]
    public async Task Publish_publishes_changed_stations_and_lists_them()
    {
        var published = Publish("published", Catalogue, crawled: true);
        var changed = await SaveAsync(
            "changed.json.gz",
            stations => [.. stations.Select(s => s.Id == Seattle ? Renamed(s, "Seattle (renamed)") : s)],
            retrieved: new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero));
        var output = Path.Combine(_directory, "out");

        var (exit, stdout, _) = Run(["tides", "publish", output, "--catalogue", changed, "--previous", published, "--crawled"]);

        Assert.Equal(0, exit);
        Assert.Contains("New data: the catalogue changed.", stdout);
        Assert.Contains($"- Tide stations changed (1): {Seattle}", stdout);
        var manifest = await TideCatalogueManifest.LoadAsync(Path.Combine(output, TideCatalogueManifest.DefaultFileName), Token);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero), manifest.Retrieved);
        var reread = await TideStationCatalogueFile.LoadAsync(Path.Combine(output, TideCatalogueManifest.DefaultCatalogueFileName), Token);
        Assert.Equal("Seattle (renamed)", reread.Find(Seattle)?.Name);
    }

    [Fact]
    public async Task Publish_refuses_a_catalogue_that_lost_stations_and_writes_nothing()
    {
        var published = Publish("published", Catalogue, crawled: true);
        var shorter = await SaveAsync("shorter.json.gz", stations => [.. stations.Where(s => s.Id != "9444972")], retrieved: null);
        var output = Path.Combine(_directory, "out");
        var summary = Path.Combine(_directory, "summary.md");

        var (exit, _, stderr) = Run(["tides", "publish", output, "--catalogue", shorter, "--previous", published, "--summary", summary]);

        Assert.Equal(TidesPublishCommand.RefusedExitCode, exit);
        Assert.Contains("4 tide stations, down from 5", stderr);
        Assert.False(Directory.Exists(output));
        Assert.Contains("not published", await File.ReadAllTextAsync(summary, Token));

        // A looser limit lets it through.
        var (allowed, stdout, _) = Run(["tides", "publish", output, "--catalogue", shorter, "--previous", published, "--max-drop", "50"]);
        Assert.Equal(0, allowed);
        Assert.Contains("- Tide stations removed (1): 9444972", stdout);
    }

    [Fact]
    public async Task Update_from_a_mirror_downloads_its_file_and_checks_against_its_manifest()
    {
        var published = Publish("published", Catalogue, crawled: true);
        var mirror = new Uri("https://mirror.example/tide-stations/tide-stations.manifest.json");
        TidesUpdateCommand.HttpFactory = () => new HttpClient(new FolderHost(published, mirror));
        var local = Path.Combine(_directory, "local", "stations.json.gz");

        var (exit, _, stderr) = Run(["tides", "update", "--from", mirror.AbsoluteUri, "--catalogue", local]);

        Assert.Equal(0, exit);
        Assert.Contains($"Saved {local}.", stderr);
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(published, TideCatalogueManifest.DefaultCatalogueFileName), Token),
            await File.ReadAllBytesAsync(local, Token));

        var (current, currentOut, _) = Run(["tides", "update", "--from", mirror.AbsoluteUri, "--check", "--catalogue", local]);
        Assert.Equal(0, current);
        Assert.Contains("The mirror has no newer data", currentOut);

        // A local catalogue retrieved before the mirror's.
        var older = await SaveAsync("older.json.gz", c => c, retrieved: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        var (stale, staleOut, _) = Run(["tides", "update", "--from", mirror.AbsoluteUri, "--check", "--catalogue", older]);
        Assert.Equal(8, stale);
        Assert.Contains("The mirror has newer data", staleOut);
    }

    [Fact]
    public void Update_from_a_mirror_that_does_not_answer_fails_with_code_1()
    {
        TidesUpdateCommand.HttpFactory = () => new HttpClient(new FolderHost(Path.Combine(_directory, "empty"), new Uri("https://mirror.example/m.json")));

        var (exit, _, stderr) = Run(["tides", "update", "--from", "https://mirror.example/m.json", "--catalogue", Path.Combine(_directory, "x.json.gz")]);

        Assert.Equal(1, exit);
        Assert.Contains("The mirror at https://mirror.example/m.json didn't answer", stderr);
    }

    [Fact]
    public async Task Update_downloads_from_the_mirror_by_default()
    {
        var published = Publish("published", Catalogue, crawled: true);
        var host = new FolderHost(published, TideCatalogueMirrorClientOptions.DefaultManifestAddress);
        TidesUpdateCommand.HttpFactory = () => new HttpClient(host);
        var local = Path.Combine(_directory, "local", "stations.json.gz");

        var (exit, _, stderr) = Run(["tides", "update", "--catalogue", local]);

        Assert.Equal(0, exit);
        Assert.Contains($"from {TideCatalogueMirrorClientOptions.DefaultManifestAddress}", stderr);
        Assert.DoesNotContain("NOAA instead", stderr);
        Assert.All(host.Requests, uri => Assert.Equal("github.com", uri.Host));
        Assert.Equal(
            await File.ReadAllBytesAsync(Path.Combine(published, TideCatalogueManifest.DefaultCatalogueFileName), Token),
            await File.ReadAllBytesAsync(local, Token));

        // The newer-data hint names the plain command, since that's what reads the mirror.
        var older = await SaveAsync("older.json.gz", c => c, retrieved: new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        var (stale, staleOut, _) = Run(["tides", "update", "--check", "--catalogue", older]);
        Assert.Equal(8, stale);
        Assert.Contains("run 's100 tides update' to download it", staleOut);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Update_falls_back_to_noaa_when_the_default_mirror_fails(bool check)
    {
        // Neither the mirror nor NOAA answers: the default tries NOAA after the mirror.
        var host = new FolderHost(Path.Combine(_directory, "empty"), TideCatalogueMirrorClientOptions.DefaultManifestAddress);
        TidesUpdateCommand.HttpFactory = () => new HttpClient(host);
        string[] args = check
            ? ["tides", "update", "--check", "--catalogue", Catalogue]
            : ["tides", "update", "--catalogue", Path.Combine(_directory, "x.json.gz")];

        var (exit, _, stderr) = Run(args);

        Assert.Equal(1, exit);
        Assert.Contains($"The mirror at {TideCatalogueMirrorClientOptions.DefaultManifestAddress} didn't answer", stderr);
        Assert.Contains(check ? "Checking NOAA instead." : "Downloading from NOAA instead.", stderr);
        Assert.Contains("Error: NOAA's metadata API didn't answer", stderr);
        Assert.Contains(host.Requests, uri => uri.Host == "api.tidesandcurrents.noaa.gov");
    }

    [Fact]
    public void Update_from_a_named_mirror_does_not_fall_back()
    {
        var host = new FolderHost(Path.Combine(_directory, "empty"), TideCatalogueMirrorClientOptions.DefaultManifestAddress);
        TidesUpdateCommand.HttpFactory = () => new HttpClient(host);

        var (exit, _, stderr) = Run(["tides", "update", "--from", "mirror", "--catalogue", Path.Combine(_directory, "x.json.gz")]);

        Assert.Equal(1, exit);
        Assert.Contains("Error: The mirror at", stderr);
        Assert.DoesNotContain("NOAA", stderr);
        Assert.DoesNotContain(host.Requests, uri => uri.Host == "api.tidesandcurrents.noaa.gov");
    }

    [Theory]
    [InlineData(null, true, "https://github.com/philliphoff/EncDotNet.S100/releases/download/tide-stations/tide-stations.manifest.json")]
    [InlineData("", true, "https://github.com/philliphoff/EncDotNet.S100/releases/download/tide-stations/tide-stations.manifest.json")]
    [InlineData("noaa", true, null)]
    [InlineData("NOAA", true, null)]
    [InlineData("mirror", true, "https://github.com/philliphoff/EncDotNet.S100/releases/download/tide-stations/tide-stations.manifest.json")]
    [InlineData("https://example.org/m.json", true, "https://example.org/m.json")]
    [InlineData("ftp://example.org/m.json", false, null)]
    [InlineData("elsewhere", false, null)]
    public void From_names_noaa_the_mirror_or_a_manifest_url(string? from, bool valid, string? address)
    {
        Assert.Equal(valid, TidesUpdateCommand.TryGetMirrorAddress(from, out var uri));
        Assert.Equal(address, uri?.AbsoluteUri);
        Assert.Equal(valid, new TidesUpdateCommand.Settings { From = from }.Validate().Successful);
    }

    /// <summary>Publishes the catalogue into a folder under the test directory, as a first publication.</summary>
    private string Publish(string folder, string catalogue, bool crawled)
    {
        var output = Path.Combine(_directory, folder);
        string[] args = crawled
            ? ["tides", "publish", output, "--catalogue", catalogue, "--crawled"]
            : ["tides", "publish", output, "--catalogue", catalogue];
        var (exit, _, stderr) = Run(args);
        Assert.True(exit == 0, stderr);
        return output;
    }

    /// <summary>Saves the Puget Sound catalogue with its stations changed and a retrieval date.</summary>
    private async Task<string> SaveAsync(string name, Func<IReadOnlyList<TideStationEntry>, IReadOnlyList<TideStationEntry>> edit, DateTimeOffset? retrieved)
    {
        var catalogue = await TideStationCatalogueFile.LoadAsync(Catalogue, Token);
        var path = Path.Combine(_directory, name);
        await TideStationCatalogueFile.SaveAsync(
            path,
            new TideStationCatalogue(edit(catalogue.Stations), catalogue.CurrentStations, catalogue.Provider, retrieved ?? catalogue.Retrieved),
            Token);
        return path;
    }

    private static TideStationEntry Renamed(TideStationEntry s, string name) => new()
    {
        Id = s.Id,
        Name = name,
        Position = s.Position,
        Kind = s.Kind,
        Region = s.Region,
        UtcOffset = s.UtcOffset,
        Constituents = s.Constituents,
        Datums = s.Datums,
        ReferenceStationId = s.ReferenceStationId,
        Offsets = s.Offsets,
        OffsetsDatum = s.OffsetsDatum,
    };

    private static (int Exit, string Stdout, string Stderr) Run(string[] args)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            var exit = CliApp.Build().Run(args);
            return (exit, stdout.ToString().ReplaceLineEndings("\n"), stderr.ToString().ReplaceLineEndings("\n"));
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>Serves a mirror's files from a folder, beside the manifest address, and answers anything else, NOAA included, with 404.</summary>
    private sealed class FolderHost(string folder, Uri manifest) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            lock (Requests)
                Requests.Add(uri);
            var file = uri == manifest
                ? Path.Combine(folder, TideCatalogueManifest.DefaultFileName)
                : uri == new Uri(manifest, TideCatalogueManifest.DefaultCatalogueFileName)
                    ? Path.Combine(folder, TideCatalogueManifest.DefaultCatalogueFileName)
                    : null;
            return Task.FromResult(file is not null && File.Exists(file)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(File.ReadAllBytes(file)) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
