using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>tiles serve</c> and <c>tiles export</c> over a plain folder of
/// datasets and an S-57 exchange set (issue #1464): folders are searched
/// recursively as <c>s100 mcp serve</c> searches them, a real XYZ folder is
/// still served as a tile set, and adding or removing a file reloads.
/// </summary>
public sealed class TilesServeFolderTests : IDisposable
{
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-serve-folder-" + Guid.NewGuid().ToString("N"));

    public TilesServeFolderTests() => Directory.CreateDirectory(_root);

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

    /// <summary>Copies the S-57 fixture to <paramref name="relativePath"/> under the test folder.</summary>
    private string Place(string relativePath)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(S57, path, overwrite: true);
        return path;
    }

    /// <summary>An S-57 exchange set: <c>ENC_ROOT/CATALOG.031</c> and <c>ENC_ROOT/&lt;cell&gt;/&lt;cell&gt;.000</c>.</summary>
    private string EncRoot(string relativePath)
    {
        var encRoot = Path.Combine(_root, relativePath);
        Place(Path.Combine(relativePath, "US5MA1BO", "US5MA1BO.000"));
        File.WriteAllText(Path.Combine(encRoot, "CATALOG.031"), "catalogue");
        return encRoot;
    }

    private static TilesServeCommand.Settings Settings(string? input = null, string? from = null, string? cacheDirectory = null) => new()
    {
        Input = input,
        ExchangeSet = from,
        MinZoom = 14,
        MaxZoom = 14,
        Parallel = 2,
        CacheDirectory = cacheDirectory,
    };

    [Fact]
    public void A_plain_folder_is_rendered_from_every_dataset_in_it()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        Place(Path.Combine("charts", "US5MA1BO.000"));
        Place(Path.Combine("charts", "nested", "deeper", "US5MA1BO.000"));
        File.WriteAllText(Path.Combine(_root, "charts", "readme.txt"), "not a dataset");

        var settings = Settings(Path.Combine(_root, "charts"));

        Assert.False(settings.ServesTileSet);
        Assert.True(settings.IsFolder);
        Assert.False(settings.IsExchangeSet);
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        using (source)
            Assert.StartsWith("2 dataset(s) (S-57)", source!.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void A_plain_folder_is_exported()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        Place(Path.Combine("charts", "a", "US5MA1BO.000"));
        var output = Path.Combine(_root, "chart.mbtiles");

        Assert.Equal(0, CliApp.Build().Run(
            ["tiles", "export", Path.Combine(_root, "charts"), "-o", output, "--min-zoom", "13", "--max-zoom", "13", "--skip-empty"],
            TestContext.Current.CancellationToken));

        var tiles = TileSource.Open(output);
        Assert.Equal(13, (int?)tiles.ToTileJson()["minzoom"]);
    }

    [Fact]
    public void An_s57_exchange_set_nested_in_a_folder_is_rendered()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        EncRoot(Path.Combine("volume", "ENC_ROOT"));

        var settings = Settings(Path.Combine(_root, "volume"));

        Assert.True(settings.IsFolder);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        using (source)
            Assert.StartsWith("1 dataset(s) (S-57)", source!.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void An_s57_exchange_set_is_rendered_given_directly_or_with_from()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var encRoot = EncRoot("ENC_ROOT");

        foreach (var settings in new[]
                 {
                     Settings(encRoot),
                     Settings(from: encRoot),
                     Settings(Path.Combine(encRoot, "CATALOG.031")),
                     Settings(from: Path.Combine(encRoot, "CATALOG.031")),
                 })
        {
            Assert.False(settings.ServesTileSet);
            Assert.Equal(Path.GetFullPath(encRoot), Path.GetFullPath(settings.FolderSource!));
            Assert.True(settings.Validate().Successful, settings.Validate().Message);
            Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
            source!.Dispose();
        }
    }

    [Fact]
    public void A_folder_with_no_datasets_is_refused()
    {
        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        File.WriteAllText(Path.Combine(empty, "notes.txt"), "nothing to tile");

        Assert.False(TileSource.IsTileSet(empty));
        Assert.Equal(2, TilesServeCommand.OpenRendered(Settings(empty), out var source));
        Assert.Null(source);
    }

    [Fact]
    public void Only_a_folder_that_looks_like_an_xyz_tile_set_is_served_as_one()
    {
        // {z}/{x}/{y} tiles without a tiles.json.
        var xyz = Path.Combine(_root, "xyz");
        Directory.CreateDirectory(Path.Combine(xyz, "13", "2478"));
        File.WriteAllBytes(Path.Combine(xyz, "13", "2478", "3030.png"), [0x89, (byte)'P', (byte)'N', (byte)'G']);
        Assert.True(TileSource.IsTileSet(xyz));
        Assert.True(Settings(xyz).ServesTileSet);
        Assert.Equal(TileImageFormat.Png, TileSource.Open(xyz).Format);

        // A tiles.json alone (an export that skipped every empty tile).
        var described = Path.Combine(_root, "described");
        Directory.CreateDirectory(described);
        File.WriteAllText(Path.Combine(described, XyzDirectoryTileSink.TileJsonFileName), """{"format":"png"}""");
        Assert.True(TileSource.IsTileSet(described));

        // Numbered folders that aren't zoom/column pairs, and chart folders.
        var years = Path.Combine(_root, "years");
        Directory.CreateDirectory(Path.Combine(years, "2026", "11"));
        Directory.CreateDirectory(Path.Combine(years, "5", "charts"));
        Assert.False(TileSource.IsTileSet(years));
        Assert.False(TileSource.IsTileSet(Path.Combine(_root, "missing")));
    }

    [Fact]
    public void The_cache_directory_is_allowed_for_a_folder_of_datasets()
    {
        var folder = Path.Combine(_root, "charts");
        Directory.CreateDirectory(folder);

        var result = Settings(folder, cacheDirectory: Path.Combine(_root, "cache")).Validate();

        Assert.True(result.Successful, result.Message);
    }

    [Fact]
    public async Task Adding_or_removing_a_dataset_in_the_folder_reloads()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var folder = Path.Combine(_root, "charts");
        Place(Path.Combine("charts", "a", "US5MA1BO.000"));
        var settings = Settings(folder, cacheDirectory: Path.Combine(folder, "cache"));

        var fingerprint = TileRenderSession.Fingerprint(settings);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var initial));
        int opens = 0;
        await using var source = new RefreshingTileSource(
            initial!,
            fingerprint,
            () => TileRenderSession.Fingerprint(settings),
            () =>
            {
                Interlocked.Increment(ref opens);
                return TilesServeCommand.OpenRendered(settings, out var reopened) == 0 ? reopened : null;
            },
            Timeout.InfiniteTimeSpan,
            _ => { });

        // Tiles written to a cache kept inside the folder are not a change.
        Directory.CreateDirectory(Path.Combine(folder, "cache", "served"));
        File.WriteAllText(Path.Combine(folder, "cache", "served", "tile.png"), "tile");
        Assert.False(await source.CheckAsync());
        Assert.False(await source.CheckAsync());
        Assert.Equal(0, opens);

        // A new cell copied in: seen, confirmed settled, then reopened with it.
        var added = Place(Path.Combine("charts", "b", "US5MA1BO.000"));
        Assert.False(await source.CheckAsync());
        Assert.True(await source.CheckAsync());
        Assert.Equal(1, opens);
        Assert.StartsWith("2 dataset(s)", source.Current.Path, StringComparison.Ordinal);

        // And removed again.
        File.Delete(added);
        Assert.False(await source.CheckAsync());
        Assert.True(await source.CheckAsync());
        Assert.Equal(2, opens);
        Assert.StartsWith("1 dataset(s)", source.Current.Path, StringComparison.Ordinal);
    }
}
