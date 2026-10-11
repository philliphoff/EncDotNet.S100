using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Overlapping cells of different scales in the headless composite (issue
/// #859): as in the viewer, the finer cell hides the coarser one under its
/// coverage, in both <c>s100 render</c> and <c>s100 tiles export</c>.
/// </summary>
public sealed class OverlappingCellsTests : IDisposable
{
    // The coarse cell fills its whole coverage with land; the fine cell sits in
    // the middle of it and draws nothing, so wherever its coverage hides the
    // coarse cell the image stays blank.
    private static readonly SyntheticS101Cell.Box CoarseBox = new(10.0, 50.0, 10.2, 50.2);
    private static readonly SyntheticS101Cell.Box FineBox = new(10.05, 50.05, 10.15, 50.15);

    private readonly string _root = Path.Combine(Path.GetTempPath(), "overlapping-cells-" + Guid.NewGuid().ToString("N"));
    private readonly string _coarse;
    private readonly string _fine;

    public OverlappingCellsTests()
    {
        Directory.CreateDirectory(_root);
        _coarse = SyntheticS101Cell.Write(_root, "101AA00COARSE.000", CoarseBox, minimumDisplayScale: 90_000, land: CoarseBox);
        _fine = SyntheticS101Cell.Write(_root, "101AA00FINE.000", FineBox, minimumDisplayScale: 45_000, land: null);
    }

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
    public void Render_composite_hides_the_coarse_cell_under_the_fine_cell()
    {
        var output = Path.Combine(_root, "composite.png");
        int exit = CliApp.Build().Run(
            ["render", "--layer", _coarse, "--layer", _fine, output,
             "--bbox", "10,50,10.2,50.2", "--width", "200", "--height", "200"]);

        Assert.Equal(0, exit);
        using var bitmap = SKBitmap.Decode(output);

        // The box is taller than wide in Web Mercator, so the fit leaves the
        // land about 128 pixels wide, centred. The coarse cell's land shows
        // outside the fine coverage...
        var land = bitmap.GetPixel(45, 100);
        Assert.NotEqual(SKColors.White, land);
        Assert.Equal(land, bitmap.GetPixel(100, 20));

        // ...and the fine coverage, where the fine cell draws nothing, stays
        // the white background.
        Assert.Equal(SKColors.White, bitmap.GetPixel(100, 100));
    }

    [Fact]
    public void Tiles_export_hides_the_coarse_cell_under_the_fine_cell_while_the_fine_cell_draws()
    {
        var tiles = Path.Combine(_root, "tiles");

        // Zoom 13 (about 1:44,000 here) is inside both cells' scale; zoom 12
        // (about 1:88,000) is past the fine cell's 1:45,000, so it no longer
        // draws and no longer hides the coarse cell.
        int exit = CliApp.Build().Run(
            ["tiles", "export", "--layer", _coarse, "--layer", _fine, "-o", tiles,
             "--min-zoom", "12", "--max-zoom", "13"]);
        Assert.Equal(0, exit);

        Assert.True(IsOpaque(TileAt(tiles, 13, 10.17, 50.1)), "The coarse cell draws outside the fine coverage.");
        Assert.True(IsBlank(TileAt(tiles, 13, 10.08, 50.1)), "The fine cell hides the coarse cell under its coverage.");
        Assert.True(IsOpaque(TileAt(tiles, 12, 10.08, 50.1)), "Past its scale the fine cell hides nothing.");
    }

    // The tile holding a WGS-84 position, as written by tiles export.
    private static string TileAt(string root, int zoom, double longitude, double latitude)
    {
        var (worldX, worldY) = WebMercator.FromLonLat(longitude, latitude);
        double size = XyzTileGrid.TileWorldSize(zoom);
        int x = (int)Math.Floor((worldX + XyzTileGrid.Extent) / size);
        int y = (int)Math.Floor((XyzTileGrid.Extent - worldY) / size);
        return Path.Combine(root, zoom.ToString(System.Globalization.CultureInfo.InvariantCulture), x.ToString(System.Globalization.CultureInfo.InvariantCulture), y.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png");
    }

    private static bool IsOpaque(string path)
    {
        using var bitmap = SKBitmap.Decode(path);
        return bitmap.Pixels.All(p => p.Alpha == 255);
    }

    private static bool IsBlank(string path)
    {
        using var bitmap = SKBitmap.Decode(path);
        return bitmap.Pixels.All(p => p.Alpha == 0);
    }
}
