using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Cli.Web;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for what the <c>tiles serve</c> web page reads about what's served
/// (<see cref="TileSourcePageData"/>, issue #1575): the tile set, palettes,
/// times, datasets and layers, from rendered datasets and from a built tile set.
/// </summary>
public sealed class ServePageDataTests : IDisposable
{
    // Off Nantucket.
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    // Surface currents in Delaware Bay.
    private static string S111 => Path.Combine(AppContext.BaseDirectory, "TestData", "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "serve-page-data-" + Guid.NewGuid().ToString("N"));

    public ServePageDataTests() => Directory.CreateDirectory(_root);

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

    [Fact]
    public void Rendered_datasets_are_described_with_their_palettes_times_and_layers()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S111), "S-57 or S-111 fixture not present.");
        var settings = new TilesServeCommand.Settings
        {
            Layers = [S57, S111],
            LayersBy = TileLayers.Product,
            MinZoom = 11,
            MaxZoom = 14,
            Metatile = 1,
            Parallel = 2,
        };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        using var _ = source!;
        var data = new TileSourcePageData(source!, hasToken: false);

        var tileSet = data.TileSet;
        Assert.Equal(11, tileSet.MinZoom);
        Assert.Equal(14, tileSet.MaxZoom);
        Assert.Equal("png", tileSet.Format);
        Assert.Equal(4, tileSet.Bounds!.Length);
        Assert.Equal("day", tileSet.DefaultPalette);
        Assert.Equal(["day", "dusk", "night"], data.Palettes.Take(3));

        Assert.True(data.HasDatasets);
        Assert.NotNull(data.Revision);
        Assert.Equal(2, data.Datasets.Count);
        var chart = Assert.Single(data.Datasets, d => d.Product == "S-57");
        Assert.Equal("US5MA1BO.000", chart.File);
        Assert.Equal(4, chart.Bounds!.Length);
        Assert.Empty(chart.Times);
        var currents = Assert.Single(data.Datasets, d => d.Product == "S-111");
        Assert.NotEmpty(currents.Times);
        Assert.NotNull(currents.Dynamicity);
        Assert.Equal(currents.Times.Count, data.Times.Intersect(currents.Times).Count());

        Assert.NotEmpty(data.Layers);
        Assert.All(data.Datasets, d => Assert.Contains(d.Layer, data.Layers));
    }

    [Fact]
    public async Task A_built_tile_set_has_no_datasets_or_features()
    {
        // An XYZ directory with one tile, and no metadata: the directory's name names it.
        var folder = Path.Combine(_root, "harbour");
        Directory.CreateDirectory(Path.Combine(folder, "3", "4"));
        await File.WriteAllBytesAsync(Path.Combine(folder, "3", "4", "2.png"), [0x89, (byte)'P', (byte)'N', (byte)'G'], TestContext.Current.CancellationToken);
        var data = new TileSourcePageData(TileSource.Open(folder), hasToken: true);

        Assert.False(data.HasDatasets);
        Assert.Empty(data.Datasets);
        Assert.Empty(data.Palettes);
        Assert.Empty(data.Times);
        Assert.Empty(data.Layers);
        Assert.Null(data.TileSet.DefaultPalette);
        Assert.False(data.TileSet.HighDpi);
        Assert.Null(await data.QueryFeaturesAsync(0, 0, 10, 5, null, TestContext.Current.CancellationToken));
    }
}
