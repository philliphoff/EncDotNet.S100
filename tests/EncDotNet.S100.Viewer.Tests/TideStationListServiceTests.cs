using System.IO.Compression;
using System.Net;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Mirror;
using EncDotNet.S100.Tides.Stations.Noaa;
using EncDotNet.S100.Viewer.Services.Tides;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The station list downloads from the mirror of NOAA's catalogue first, and
/// from NOAA when the mirror can't be used (#1674, stage 4), over NOAA's Puget
/// Sound stations (tests/datasets/Tides/Stations/puget-sound.json).
/// </summary>
public sealed class TideStationListServiceTests : IDisposable
{
    private static readonly Uri Manifest = new("https://mirror.example/tide-stations/tide-stations.manifest.json");
    private static readonly Uri File = new("https://mirror.example/tide-stations/tide-stations.json.gz");
    private static readonly DateTimeOffset Retrieved = new(2026, 11, 12, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Fixture = Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "puget-sound.json");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "s100-tide-list-" + Guid.NewGuid().ToString("N"));

    public TideStationListServiceTests() => Directory.CreateDirectory(_folder);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string ListPath => Path.Combine(_folder, "tide-stations.json.gz");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public async Task The_first_download_comes_from_the_mirror()
    {
        var content = await PublishedAsync(Retrieved);
        var mirror = new MirrorHost().Serve(Manifest, ManifestFor(content.Bytes, content.Catalogue)).Serve(File, content.Bytes);
        var list = Service(mirror);
        var sources = Record(list);

        Assert.True(await list.DownloadAsync(Token));

        Assert.Equal(content.Bytes, await System.IO.File.ReadAllBytesAsync(ListPath, Token));
        Assert.Equal(Retrieved, list.Catalogue?.Retrieved);
        Assert.Equal("NOAA CO-OPS", list.Catalogue?.Provider);
        Assert.Contains(TideStationListSource.Mirror, sources);
        Assert.DoesNotContain(TideStationListSource.Noaa, sources);
        Assert.DoesNotContain(mirror.Requests, uri => uri.Host != "mirror.example");
        Assert.Null(list.LastError);
        Assert.Null(list.DownloadingFrom);
        Assert.Equal(TideStationListActivity.Idle, list.Activity);
    }

    [Fact]
    public async Task A_refresh_keeps_the_list_when_the_mirror_has_nothing_newer()
    {
        var content = await PublishedAsync(Retrieved);
        await System.IO.File.WriteAllBytesAsync(ListPath, content.Bytes, Token);
        var mirror = new MirrorHost().Serve(Manifest, ManifestFor(content.Bytes, content.Catalogue)).Serve(File, content.Bytes);
        var list = Service(mirror);
        await list.EnsureLoadedAsync();

        Assert.False(await list.DownloadAsync(Token));

        Assert.True(list.IsUpToDate);
        Assert.False(list.IsStale);
        Assert.Null(list.LastError);
        Assert.Equal([Manifest], mirror.Requests);

        // The next read clears it.
        await list.ReloadAsync();
        Assert.False(list.IsUpToDate);
    }

    [Fact]
    public async Task A_refresh_replaces_the_list_when_the_mirror_has_newer_data()
    {
        await System.IO.File.WriteAllBytesAsync(ListPath, (await PublishedAsync(Retrieved)).Bytes, Token);
        var newer = await PublishedAsync(Retrieved.AddDays(7));
        var mirror = new MirrorHost().Serve(Manifest, ManifestFor(newer.Bytes, newer.Catalogue)).Serve(File, newer.Bytes);
        var list = Service(mirror);
        await list.EnsureLoadedAsync();

        Assert.True(await list.CheckForUpdatesAsync(Token));
        Assert.True(list.IsStale);

        Assert.True(await list.DownloadAsync(Token));

        Assert.Equal(Retrieved.AddDays(7), list.Catalogue?.Retrieved);
        Assert.False(list.IsStale);
        Assert.False(list.IsUpToDate);
        Assert.Equal(newer.Bytes, await System.IO.File.ReadAllBytesAsync(ListPath, Token));
    }

    [Fact]
    public async Task A_download_falls_back_to_noaa_when_the_mirror_can_not_be_used()
    {
        // The mirror's file doesn't match its manifest; NOAA doesn't answer either.
        var content = await PublishedAsync(Retrieved);
        var corrupt = content.Bytes.ToArray();
        corrupt[^5] ^= 0xff;
        var mirror = new MirrorHost().Serve(Manifest, ManifestFor(content.Bytes, content.Catalogue)).Serve(File, corrupt);
        var list = Service(mirror);
        var sources = Record(list);

        Assert.False(await list.DownloadAsync(Token));

        Assert.Equal([TideStationListSource.Mirror, TideStationListSource.Noaa], sources.Distinct());
        Assert.Contains(mirror.Requests, uri => uri.Host == "api.tidesandcurrents.noaa.gov");
        Assert.NotNull(list.LastError);
        Assert.Null(list.Catalogue);
        Assert.False(System.IO.File.Exists(ListPath));
    }

    [Fact]
    public async Task A_check_asks_noaa_when_the_mirror_does_not_answer()
    {
        await System.IO.File.WriteAllBytesAsync(ListPath, (await PublishedAsync(Retrieved)).Bytes, Token);
        var mirror = new MirrorHost();
        var list = Service(mirror);
        await list.EnsureLoadedAsync();

        // NOAA doesn't answer either: the offline rule stays.
        Assert.Null(await list.CheckForUpdatesAsync(Token));

        Assert.Equal(Manifest, mirror.Requests[0]);
        Assert.Contains(mirror.Requests, uri => uri.Host == "api.tidesandcurrents.noaa.gov");
        Assert.Equal(list.Catalogue!.IsOlderThan(TideStationCatalogue.OfflineStaleAfter, DateTimeOffset.UtcNow), list.IsStale);
    }

    [Fact]
    public async Task Without_a_mirror_the_list_downloads_from_noaa()
    {
        var mirror = new MirrorHost();
        var list = Service(mirror, withMirror: false);
        var sources = Record(list);

        Assert.False(await list.DownloadAsync(Token));

        Assert.Equal([TideStationListSource.Noaa], sources.Distinct());
        Assert.All(mirror.Requests, uri => Assert.Equal("api.tidesandcurrents.noaa.gov", uri.Host));
    }

    [Fact]
    public async Task The_list_shipped_with_SoundCharts_needs_no_download()
    {
        var content = await PublishedAsync(Retrieved);
        var snapshot = Path.Combine(_folder, TideCatalogueSnapshot.DirectoryName);
        Directory.CreateDirectory(snapshot);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(snapshot, TideCatalogueManifest.DefaultCatalogueFileName), content.Bytes, Token);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(snapshot, TideCatalogueManifest.DefaultFileName), ManifestFor(content.Bytes, content.Catalogue), Token);
        var host = new MirrorHost();
        var list = Service(host, snapshot: snapshot);

        var catalogue = await list.EnsureAvailableAsync();

        Assert.Equal(Retrieved, catalogue?.Retrieved);
        Assert.Equal(content.Bytes, await System.IO.File.ReadAllBytesAsync(ListPath, Token));
        Assert.Empty(host.Requests);
    }

    [Fact]
    public async Task A_newer_cached_list_is_kept_over_the_shipped_one()
    {
        var shipped = await PublishedAsync(Retrieved);
        var snapshot = Path.Combine(_folder, TideCatalogueSnapshot.DirectoryName);
        Directory.CreateDirectory(snapshot);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(snapshot, TideCatalogueManifest.DefaultCatalogueFileName), shipped.Bytes, Token);
        await System.IO.File.WriteAllBytesAsync(Path.Combine(snapshot, TideCatalogueManifest.DefaultFileName), ManifestFor(shipped.Bytes, shipped.Catalogue), Token);
        await System.IO.File.WriteAllBytesAsync(ListPath, (await PublishedAsync(Retrieved.AddDays(7))).Bytes, Token);

        var catalogue = await Service(new MirrorHost(), snapshot: snapshot).EnsureLoadedAsync();

        Assert.Equal(Retrieved.AddDays(7), catalogue?.Retrieved);
    }

    [Theory]
    [InlineData(null, "https://github.com/philliphoff/EncDotNet.S100/releases/download/tide-stations/tide-stations.manifest.json")]
    [InlineData("https://example.org/m.json", "https://example.org/m.json")]
    [InlineData("  https://example.org/m.json ", "https://example.org/m.json")]
    [InlineData("", null)]
    [InlineData("ftp://example.org/m.json", null)]
    [InlineData("not a url", null)]
    public void The_mirror_setting_names_the_default_another_mirror_or_none(string? setting, string? expected) =>
        Assert.Equal(expected, TideStationsServiceCollectionExtensions.MirrorOptions(setting)?.ManifestAddress.AbsoluteUri);

    private TideStationListService Service(MirrorHost host, bool withMirror = true, string? snapshot = null) => new(
        ListPath,
        () => new HttpClient(host),
        TimeProvider.System,
        action => action(),
        logger: null,
        withMirror ? new TideCatalogueMirrorClientOptions { ManifestAddress = Manifest, MaxRetries = 0, RetryDelay = TimeSpan.Zero } : null,
        new NoaaTideStationClientOptions { MaxRetries = 0, RetryDelay = TimeSpan.Zero, RequestInterval = TimeSpan.Zero },
        snapshot);

    /// <summary>Every source the list downloads from, as it changes.</summary>
    private static List<TideStationListSource> Record(TideStationListService list)
    {
        var sources = new List<TideStationListSource>();
        list.Changed += (_, _) =>
        {
            if (list.DownloadingFrom is { } source)
                sources.Add(source);
        };
        return sources;
    }

    /// <summary>The Puget Sound catalogue as a mirror publishes it, retrieved at a time.</summary>
    private static async Task<(TideStationCatalogue Catalogue, byte[] Bytes)> PublishedAsync(DateTimeOffset retrieved)
    {
        var fixture = await TideStationCatalogueFile.LoadAsync(Fixture, Token);
        var catalogue = new TideStationCatalogue(fixture.Stations, fixture.CurrentStations, fixture.Provider, retrieved);
        var buffer = new MemoryStream();
        await using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await TideStationCatalogueFile.WriteAsync(gzip, catalogue, Token);
        }

        return (catalogue, buffer.ToArray());
    }

    private static byte[] ManifestFor(byte[] content, TideStationCatalogue catalogue)
    {
        var manifest = TideCatalogueManifest.Create(content, catalogue, catalogue.Retrieved!.Value, catalogue.Retrieved!.Value);
        var buffer = new MemoryStream();
        manifest.WriteAsync(buffer, Token).GetAwaiter().GetResult();
        return buffer.ToArray();
    }

    /// <summary>Serves a mirror's files; anything else, NOAA included, gets 404.</summary>
    private sealed class MirrorHost : HttpMessageHandler
    {
        private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

        public List<Uri> Requests { get; } = [];

        public MirrorHost Serve(Uri address, byte[] content)
        {
            _files[address.AbsoluteUri] = content;
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (Requests)
                Requests.Add(request.RequestUri!);
            return Task.FromResult(_files.TryGetValue(request.RequestUri!.AbsoluteUri, out var content)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
