using System.IO.Compression;
using EncDotNet.S100.Mcp.Tools.Tides;
using EncDotNet.S100.Tides.Stations;
using EncDotNet.S100.Tides.Stations.Mirror;

namespace EncDotNet.S100.Mcp.Tools.Tests;

/// <summary>A catalogue file source fills a missing file from a shipped snapshot, once (#1674, stage 5).</summary>
public sealed class TideCatalogueFileSourceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tide-source-" + Guid.NewGuid().ToString("N"));

    public TideCatalogueFileSourceTests() => Directory.CreateDirectory(_directory);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_missing_file_is_filled_from_the_snapshot()
    {
        var snapshot = await ShipAsync();
        var path = Path.Combine(_directory, "cache", "tide-stations.json.gz");

        var catalogue = await new TideCatalogueFileSource(path, "Get one.", snapshot).GetAsync(Token);

        Assert.NotNull(catalogue);
        Assert.Equal("NOAA CO-OPS", catalogue.Provider);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Without_a_snapshot_a_missing_file_stays_missing()
    {
        await ShipAsync();
        var path = Path.Combine(_directory, "cache", "tide-stations.json.gz");

        Assert.Null(await new TideCatalogueFileSource(path, "Get one.").GetAsync(Token));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_snapshot_folder_with_nothing_in_it_is_ignored()
    {
        var path = Path.Combine(_directory, "cache", "tide-stations.json.gz");

        Assert.Null(await new TideCatalogueFileSource(path, "Get one.", Path.Combine(_directory, "none")).GetAsync(Token));
    }

    /// <summary>Ships the Puget Sound catalogue as an app would, and returns the folder.</summary>
    private async Task<string> ShipAsync()
    {
        var folder = Path.Combine(_directory, TideCatalogueSnapshot.DirectoryName);
        Directory.CreateDirectory(folder);
        var catalogue = await TideStationCatalogueFile.LoadAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Tides", "puget-sound.json"), Token);
        var buffer = new MemoryStream();
        await using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            await TideStationCatalogueFile.WriteAsync(gzip, catalogue, Token);
        }

        var content = buffer.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(folder, TideCatalogueManifest.DefaultCatalogueFileName), content, Token);
        var retrieved = catalogue.Retrieved ?? DateTimeOffset.UnixEpoch;
        await TideCatalogueManifest.Create(content, catalogue, retrieved, retrieved).SaveAsync(Path.Combine(folder, TideCatalogueManifest.DefaultFileName), Token);
        return folder;
    }
}
