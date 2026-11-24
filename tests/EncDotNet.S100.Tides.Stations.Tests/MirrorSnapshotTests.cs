using System.Text;
using EncDotNet.S100.Tides.Stations.Mirror;

namespace EncDotNet.S100.Tides.Stations.Tests;

/// <summary>A snapshot shipped with an app fills a missing or older cache, and leaves any other alone (#1674, stage 5).</summary>
public sealed class MirrorSnapshotTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tide-snapshot-" + Guid.NewGuid().ToString("N"));

    public MirrorSnapshotTests() => Directory.CreateDirectory(_directory);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private string SnapshotDirectory => Path.Combine(_directory, TideCatalogueSnapshot.DirectoryName);

    private string CachePath => Path.Combine(_directory, "cache", "tide-stations.json.gz");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task A_missing_cache_gets_the_snapshots_bytes()
    {
        var snapshot = await ShipAsync(MirrorFixtures.Catalogue());

        Assert.True(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));

        Assert.Equal(snapshot, await File.ReadAllBytesAsync(CachePath, Token));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(CachePath)!, "*.tmp"));
    }

    [Fact]
    public async Task An_older_cache_is_replaced_and_a_newer_or_equal_one_kept()
    {
        var snapshot = await ShipAsync(MirrorFixtures.Catalogue());

        await TideStationCatalogueFile.SaveAsync(CachePath, MirrorFixtures.Catalogue(MirrorFixtures.Retrieved.AddDays(-30)), Token);
        Assert.True(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));
        Assert.Equal(snapshot, await File.ReadAllBytesAsync(CachePath, Token));

        // As new: kept.
        Assert.False(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));

        // Newer, such as a list downloaded since: kept.
        await TideStationCatalogueFile.SaveAsync(CachePath, MirrorFixtures.Catalogue(MirrorFixtures.Retrieved.AddDays(7)), Token);
        var newer = await File.ReadAllBytesAsync(CachePath, Token);
        Assert.False(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));
        Assert.Equal(newer, await File.ReadAllBytesAsync(CachePath, Token));
    }

    [Fact]
    public async Task A_cache_it_can_not_date_is_kept()
    {
        await ShipAsync(MirrorFixtures.Catalogue());
        var catalogue = MirrorFixtures.Catalogue(MirrorFixtures.Retrieved.AddDays(-30));
        var undated = new TideStationCatalogue(catalogue.Stations, catalogue.CurrentStations, catalogue.Provider, retrieved: null);
        var theirs = new TideStationCatalogue(catalogue.Stations, catalogue.CurrentStations, "Example harbour authority", catalogue.Retrieved);

        foreach (var cache in new[] { undated, theirs })
        {
            await TideStationCatalogueFile.SaveAsync(CachePath, cache, Token);
            Assert.False(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));
        }

        await File.WriteAllTextAsync(CachePath, "not a catalogue", Token);
        Assert.False(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));
        Assert.Equal("not a catalogue", await File.ReadAllTextAsync(CachePath, Token));
    }

    [Fact]
    public async Task Without_a_usable_snapshot_nothing_is_written()
    {
        // No folder.
        Assert.False(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));

        // A file that doesn't match its manifest.
        var snapshot = await ShipAsync(MirrorFixtures.Catalogue());
        snapshot[^3] ^= 0xff;
        await File.WriteAllBytesAsync(Path.Combine(SnapshotDirectory, TideCatalogueManifest.DefaultCatalogueFileName), snapshot, Token);
        Assert.False(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));

        // A manifest that isn't one.
        await File.WriteAllTextAsync(Path.Combine(SnapshotDirectory, TideCatalogueManifest.DefaultFileName), "{}", Token);
        Assert.False(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, CachePath, Token));

        Assert.False(File.Exists(CachePath));
    }

    [Fact]
    public async Task A_json_cache_gets_uncompressed_json()
    {
        await ShipAsync(MirrorFixtures.Catalogue());
        var json = Path.Combine(_directory, "cache.json");

        Assert.True(await TideCatalogueSnapshot.SeedAsync(SnapshotDirectory, json, Token));

        Assert.Equal((byte)'{', (await File.ReadAllBytesAsync(json, Token))[0]);
        Assert.Equal(MirrorFixtures.Retrieved, (await TideStationCatalogueFile.LoadAsync(json, Token)).Retrieved);
    }

    [Fact]
    public async Task The_header_is_read_before_the_stations_or_from_the_whole_file()
    {
        await TideStationCatalogueFile.SaveAsync(CachePath, MirrorFixtures.Catalogue(), Token);
        Assert.Equal(("NOAA CO-OPS", MirrorFixtures.Retrieved), await TideStationCatalogueFile.ReadHeaderAsync(CachePath, Token));

        // Written by hand, with the date after the stations.
        var path = Path.Combine(_directory, "own.json");
        await File.WriteAllTextAsync(
            path,
            """{ "format": "tide-stations", "version": 1, "stations": [], "provider": "Mine", "retrieved": "2026-01-02T00:00:00+00:00" }""",
            Encoding.UTF8,
            Token);
        Assert.Equal(("Mine", new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero)), await TideStationCatalogueFile.ReadHeaderAsync(path, Token));
    }

    /// <summary>Ships a catalogue as an app would: its gzip file and manifest in the snapshot folder.</summary>
    private async Task<byte[]> ShipAsync(TideStationCatalogue catalogue)
    {
        Directory.CreateDirectory(SnapshotDirectory);
        var content = MirrorFixtures.Gzip(catalogue);
        await File.WriteAllBytesAsync(Path.Combine(SnapshotDirectory, TideCatalogueManifest.DefaultCatalogueFileName), content, Token);
        await MirrorFixtures.Manifest(content, catalogue).SaveAsync(Path.Combine(SnapshotDirectory, TideCatalogueManifest.DefaultFileName), Token);
        return content;
    }
}
