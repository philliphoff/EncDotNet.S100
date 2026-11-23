using System.Net;
using EncDotNet.S100.Tides.Stations.Mirror;
using EncDotNet.S100.Tides.Stations.Noaa;

namespace EncDotNet.S100.Tides.Stations.Tests;

public sealed class MirrorClientTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tide-mirror-" + Guid.NewGuid().ToString("N"));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public MirrorClientTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void Default_IsTheProjectsRollingReleaseAsset()
    {
        var address = new TideCatalogueMirrorClientOptions().ManifestAddress;
        Assert.Equal(
            "https://github.com/philliphoff/EncDotNet.S100/releases/download/tide-stations/tide-stations.manifest.json",
            address.AbsoluteUri);
        Assert.Equal(
            "https://github.com/philliphoff/EncDotNet.S100/releases/download/tide-stations/tide-stations.json.gz",
            new Uri(address, TideCatalogueManifest.DefaultCatalogueFileName).AbsoluteUri);
    }

    [Fact]
    public async Task Download_ChecksTheFileAgainstTheManifest_AndKeepsNoaasProvenance()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var content = MirrorFixtures.Gzip(catalogue);
        var handler = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(content, catalogue)))
            .Serve(MirrorFixtures.CatalogueAddress, content);

        var download = await MirrorFixtures.Client(handler).DownloadCatalogueAsync(Token);

        Assert.Equal(NoaaTideStationClient.ProviderName, download.Catalogue.Provider);
        Assert.Equal(MirrorFixtures.Retrieved, download.Catalogue.Retrieved);
        Assert.Equal(catalogue.Stations.Count, download.Catalogue.Stations.Count);
        Assert.Equal(catalogue.CurrentStations.Count, download.Catalogue.CurrentStations.Count);
        Assert.False(TideCatalogueComparison.Compare(catalogue, download.Catalogue).HasChanges);
        Assert.Equal(content.Length, download.Manifest.Size);
        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.True(r.Headers.CacheControl?.NoCache, r.RequestUri?.ToString()));
    }

    [Fact]
    public async Task Download_ToAGzPath_SavesThePublishedBytes_AndToAJsonPath_PlainJson()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var content = MirrorFixtures.Gzip(catalogue);
        var handler = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(content, catalogue)))
            .Serve(MirrorFixtures.CatalogueAddress, content);
        var client = MirrorFixtures.Client(handler);

        var gz = Path.Combine(_directory, "stations.json.gz");
        await client.DownloadCatalogueAsync(gz, Token);
        Assert.Equal(content, await File.ReadAllBytesAsync(gz, Token));

        var json = Path.Combine(_directory, "stations.json");
        await client.DownloadCatalogueAsync(json, Token);
        Assert.Equal((byte)'{', (await File.ReadAllBytesAsync(json, Token))[0]);
        var loaded = await TideStationCatalogueFile.LoadAsync(json, Token);
        Assert.False(TideCatalogueComparison.Compare(catalogue, loaded).HasChanges);
        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
    }

    [Fact]
    public async Task Download_ReadsTheManifestAgain_WhenTheFileWasReplacedBetweenThem()
    {
        // The mirror uploaded a new file but not yet its manifest: the first
        // manifest describes the old file.
        var old = MirrorFixtures.Catalogue();
        var oldContent = MirrorFixtures.Gzip(old);
        var fresh = MirrorFixtures.Catalogue(MirrorFixtures.Retrieved.AddDays(30));
        var freshContent = MirrorFixtures.Gzip(fresh);
        var handler = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(oldContent, old)))
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(freshContent, fresh)))
            .Serve(MirrorFixtures.CatalogueAddress, freshContent);

        var download = await MirrorFixtures.Client(handler).DownloadCatalogueAsync(Token);

        Assert.Equal(fresh.Retrieved, download.Catalogue.Retrieved);
        Assert.Equal(2, handler.RequestsFor(MirrorFixtures.ManifestAddress));
    }

    [Fact]
    public async Task Download_Fails_WhenTheFileNeverMatchesItsManifest()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var content = MirrorFixtures.Gzip(catalogue);
        var corrupt = content.ToArray();
        corrupt[^10] ^= 0xff;
        var handler = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(content, catalogue)))
            .Serve(MirrorFixtures.CatalogueAddress, corrupt);

        await Assert.ThrowsAsync<InvalidDataException>(() => MirrorFixtures.Client(handler, maxRetries: 1).DownloadCatalogueAsync(Token));
        Assert.Equal(2, handler.RequestsFor(MirrorFixtures.CatalogueAddress));
    }

    [Fact]
    public async Task Download_Refuses_ACatalogueVersionItCantRead()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var content = MirrorFixtures.Gzip(catalogue);
        var manifest = MirrorFixtures.Manifest(content, catalogue);
        var newer = new TideCatalogueManifest
        {
            File = manifest.File,
            CatalogueFormat = manifest.CatalogueFormat,
            CatalogueVersion = TideStationCatalogueFile.CurrentVersion + 1,
            Size = manifest.Size,
            Sha256 = manifest.Sha256,
            Retrieved = manifest.Retrieved.AddDays(30),
            Crawled = manifest.Crawled,
            Checked = manifest.Checked,
        };
        var handler = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(newer))
            .Serve(MirrorFixtures.CatalogueAddress, content);
        var client = MirrorFixtures.Client(handler);

        await Assert.ThrowsAsync<NotSupportedException>(() => client.DownloadCatalogueAsync(Token));
        Assert.Equal(0, handler.RequestsFor(MirrorFixtures.CatalogueAddress));

        // Nor does a check offer it as newer data.
        var check = await client.CheckForUpdatesAsync(catalogue, Token);
        Assert.False(check.Manifest.IsSupported);
        Assert.False(check.HasNewerData);
    }

    [Fact]
    public async Task Download_Refuses_AFileLargerThanAllowed()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var content = MirrorFixtures.Gzip(catalogue);
        var handler = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(content, catalogue)));
        var client = new TideCatalogueMirrorClient(new HttpClient(handler), new TideCatalogueMirrorClientOptions
        {
            ManifestAddress = MirrorFixtures.ManifestAddress,
            MaxCatalogueBytes = content.Length - 1,
        });

        await Assert.ThrowsAsync<InvalidDataException>(() => client.DownloadCatalogueAsync(Token));
        Assert.Equal(0, handler.RequestsFor(MirrorFixtures.CatalogueAddress));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(-1, false)]
    public async Task Check_IsNewer_OnlyWhenTheMirrorsDataWasRetrievedLater(int days, bool newer)
    {
        var published = MirrorFixtures.Catalogue(MirrorFixtures.Retrieved.AddDays(days));
        var content = MirrorFixtures.Gzip(published);
        var handler = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(content, published)));

        var check = await MirrorFixtures.Client(handler).CheckForUpdatesAsync(MirrorFixtures.Catalogue(), Token);

        Assert.Equal(newer, check.HasNewerData);
        Assert.Equal(published.Retrieved, check.Manifest.Retrieved);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Check_IsNewer_WhenThereIsNoCatalogueYet_OrItIsUndated()
    {
        var published = MirrorFixtures.Catalogue();
        var content = MirrorFixtures.Gzip(published);
        var handler = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(content, published)));
        var client = MirrorFixtures.Client(handler);

        Assert.True((await client.CheckForUpdatesAsync(null, Token)).HasNewerData);
        var undated = new TideStationCatalogue(published.Stations, published.CurrentStations, published.Provider, retrieved: null);
        Assert.True((await client.CheckForUpdatesAsync(undated, Token)).HasNewerData);
    }

    [Fact]
    public async Task AMissingManifest_FailsAtOnce_ButAMissingFile_IsTriedAgain()
    {
        var catalogue = MirrorFixtures.Catalogue();
        var content = MirrorFixtures.Gzip(catalogue);

        var none = new MirrorHandler();
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => MirrorFixtures.Client(none).CheckForUpdatesAsync(catalogue, Token));
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        Assert.Single(none.Requests);

        // The mirror replaces a file by deleting it and uploading it again.
        var replacing = new MirrorHandler()
            .Serve(MirrorFixtures.ManifestAddress, MirrorFixtures.ToJson(MirrorFixtures.Manifest(content, catalogue)))
            .Serve(MirrorFixtures.CatalogueAddress, HttpStatusCode.NotFound)
            .Serve(MirrorFixtures.CatalogueAddress, HttpStatusCode.ServiceUnavailable)
            .Serve(MirrorFixtures.CatalogueAddress, content);
        var download = await MirrorFixtures.Client(replacing).DownloadCatalogueAsync(Token);
        Assert.Equal(catalogue.Stations.Count, download.Catalogue.Stations.Count);
        Assert.Equal(3, replacing.RequestsFor(MirrorFixtures.CatalogueAddress));
    }

    [Fact]
    public void Options_AreChecked()
    {
        using var http = new HttpClient();
        Assert.Throws<ArgumentException>(() => new TideCatalogueMirrorClient(http, new TideCatalogueMirrorClientOptions { ManifestAddress = new Uri("manifest.json", UriKind.Relative) }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TideCatalogueMirrorClient(http, new TideCatalogueMirrorClientOptions { MaxRetries = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new TideCatalogueMirrorClient(http, new TideCatalogueMirrorClientOptions { MaxCatalogueBytes = 0 }));
    }
}
