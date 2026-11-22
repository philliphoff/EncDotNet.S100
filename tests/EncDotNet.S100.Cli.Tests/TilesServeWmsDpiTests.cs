using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for DPI-aware WMS GetMap (issue #1500): the <c>DPI</c>,
/// <c>MAP_RESOLUTION</c> and <c>FORMAT_OPTIONS=dpi:N</c> parameters, the
/// larger images allowed above 96 DPI, and a print-resolution map that is the
/// 96 DPI map of the same view, sharper.
/// </summary>
public sealed class TilesServeWmsDpiTests
{
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    /// <summary>Red tiles everywhere: a built tile set, which can't render a box itself.</summary>
    private sealed class RedSource : ITileSource
    {
        private readonly byte[] _tile = Png();

        public string Path => "red";

        public TileImageFormat Format => TileImageFormat.Png;

        public List<int> ZoomsRead { get; } = [];

        public ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken)
        {
            lock (ZoomsRead)
                ZoomsRead.Add(zoom);
            return ValueTask.FromResult<byte[]?>(_tile);
        }

        public JsonObject ToTileJson(string? palette, DateTime? time = null) => new()
        {
            ["name"] = "Red",
            ["minzoom"] = 0,
            ["maxzoom"] = 10,
            ["tileSize"] = 256,
        };

        private static byte[] Png()
        {
            using var bitmap = new SKBitmap(256, 256);
            bitmap.Erase(SKColors.Red);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    [Theory]
    [InlineData(null, null, null, 96.0)]
    [InlineData("300", null, null, 300.0)]
    [InlineData(null, "150", null, 150.0)]
    [InlineData(null, null, "dpi:200", 200.0)]
    [InlineData(null, null, "antialiasing:on;dpi:72", 72.0)]
    [InlineData("300", "150", "dpi:200", 300.0)]
    [InlineData(null, "120.5", "dpi:200", 120.5)]
    public void ParseDpi_takes_the_first_of_the_vendor_parameters(string? dpi, string? mapResolution, string? formatOptions, double expected)
    {
        var parameters = new Dictionary<string, string?>
        {
            ["DPI"] = dpi,
            ["MAP_RESOLUTION"] = mapResolution,
            ["FORMAT_OPTIONS"] = formatOptions,
        };

        var (parsed, error) = WmsService.ParseDpi(name => parameters[name]);

        Assert.Null(error);
        Assert.Equal(expected, parsed);
    }

    [Theory]
    [InlineData("DPI", "abc")]
    [InlineData("DPI", "0")]
    [InlineData("DPI", "5000")]
    [InlineData("MAP_RESOLUTION", "-96")]
    [InlineData("FORMAT_OPTIONS", "dpi:NaN")]
    public void ParseDpi_refuses_a_value_out_of_range(string name, string value)
    {
        var (_, error) = WmsService.ParseDpi(n => n == name ? value : null);

        Assert.NotNull(error);
    }

    [Fact]
    public void The_largest_image_grows_with_the_dpi_up_to_a_limit()
    {
        Assert.Equal(WmsMapComposer.MaxSize, WmsMapComposer.MaxSizeFor(72));
        Assert.Equal(WmsMapComposer.MaxSize, WmsMapComposer.MaxSizeFor(96));
        Assert.Equal(12800, WmsMapComposer.MaxSizeFor(300));
        Assert.Equal(WmsMapComposer.MaxPrintSize, WmsMapComposer.MaxSizeFor(600));
    }

    [Fact]
    public async Task GetMap_allows_larger_images_at_a_higher_dpi_and_picks_the_display_zoom()
    {
        var source = new RedSource();
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        // Zoom 2's resolution at 96 DPI: one 256-pixel tile across 90°.
        string Query(int width, string extra) =>
            $"SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=s100&STYLES=&CRS=CRS:84&BBOX=0,0,90,10&WIDTH={width}&HEIGHT=8&FORMAT=image/png{extra}";

        // Past 4096 pixels at 96 DPI is refused, as before.
        using (var refused = await http.GetAsync(new Uri(server.BaseUri, "wms?" + Query(5000, string.Empty)), ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("4096", await refused.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }

        // At 300 DPI the same view is 800 pixels across, drawn from zoom 2
        // (not zoom 3, which 800 pixels at 96 DPI would pick).
        source.ZoomsRead.Clear();
        using (var map = await GetBitmapAsync(http, server.BaseUri, Query(800, "&DPI=300")))
        {
            Assert.Equal(800, map.Width);
            Assert.Equal(SKColors.Red, map.GetPixel(400, 4));
        }

        Assert.All(source.ZoomsRead, zoom => Assert.Equal(2, zoom));

        source.ZoomsRead.Clear();
        (await GetBitmapAsync(http, server.BaseUri, Query(800, string.Empty))).Dispose();
        Assert.All(source.ZoomsRead, zoom => Assert.Equal(3, zoom));

        // Wider than 4096 pixels is allowed at 300 DPI, through any of the parameters.
        foreach (var dpi in (string[])["&DPI=300", "&MAP_RESOLUTION=300", "&FORMAT_OPTIONS=dpi:300"])
        {
            using var wide = await GetBitmapAsync(http, server.BaseUri, Query(5000, dpi));
            Assert.Equal(5000, wide.Width);
        }

        // But not past 4096 × 300 / 96, nor an unreadable DPI.
        using (var tooWide = await http.GetAsync(new Uri(server.BaseUri, "wms?" + Query(12801, "&DPI=300")), ct))
            Assert.Equal(HttpStatusCode.BadRequest, tooWide.StatusCode);
        using (var badDpi = await http.GetAsync(new Uri(server.BaseUri, "wms?" + Query(800, "&DPI=lots")), ct))
        {
            Assert.Equal(HttpStatusCode.BadRequest, badDpi.StatusCode);
            Assert.Contains("DPI", await badDpi.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task GetMap_at_300_dpi_is_the_96_dpi_map_of_the_same_view_sharper()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        // Every viewing group, so the cell's data-quality pattern gives the
        // fine detail the sharpness check below measures.
        var settings = new TilesServeCommand.Settings { Input = S57, MinZoom = 12, MaxZoom = 15, HideViewingGroups = ["none"] };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var rendered));
        using var source = rendered!;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();

        // Two by two zoom-13 tiles at the middle of the cell: 512 pixels at 96 DPI.
        var (west, south, east, north) = source.Layout.Bounds;
        var (centreX, centreY) = WebMercator.FromLonLat((west + east) / 2, (south + north) / 2);
        double tile = XyzTileGrid.TileWorldSize(13);
        double minX = (Math.Floor((centreX + XyzTileGrid.Extent) / tile) * tile) - XyzTileGrid.Extent - tile;
        double minY = XyzTileGrid.Extent - (Math.Floor((XyzTileGrid.Extent - centreY) / tile) * tile) - tile;
        var box = string.Create(CultureInfo.InvariantCulture, $"{minX:R},{minY:R},{minX + (2 * tile):R},{minY + (2 * tile):R}");
        string Query(int size, string extra) =>
            $"SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=s100&STYLES=&CRS=EPSG:3857&BBOX={box}&WIDTH={size}&HEIGHT={size}&FORMAT=image/png&TRANSPARENT=TRUE{extra}";

        using var screen = await GetBitmapAsync(http, server.BaseUri, Query(512, string.Empty));
        using var print = await GetBitmapAsync(http, server.BaseUri, Query(1600, "&DPI=300"));
        using var naive = await GetBitmapAsync(http, server.BaseUri, Query(1600, string.Empty));
        Assert.Equal(1600, print.Width);

        // Shrunk back to the screen's pixels, the print matches the screen:
        // symbols and text keep their physical size and the same features show.
        // Asking for the same pixels without the DPI draws zoom 14, with smaller
        // symbols and more of them.
        double printDifference = Difference(screen, print);
        double naiveDifference = Difference(screen, naive);
        Assert.True(Coverage(screen) > 0.01, "The map should show the chart.");
        Assert.InRange(Coverage(print) / Coverage(screen), 0.85, 1.15);
        Assert.True(printDifference < naiveDifference / 2,
            $"300 DPI differs from 96 DPI by {printDifference:F2}; without DPI by {naiveDifference:F2}.");

        // And it's sharper: more fine detail than the screen map enlarged.
        using var enlarged = screen.Resize(new SKImageInfo(1600, 1600), new SKSamplingOptions(SKFilterMode.Linear));
        Assert.True(Detail(print) > Detail(enlarged) * 1.5, $"{Detail(print):F2} vs {Detail(enlarged):F2}");
    }

    private static async Task<SKBitmap> GetBitmapAsync(HttpClient http, Uri baseUri, string query)
    {
        using var response = await http.GetAsync(new Uri(baseUri, "wms?" + query), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, System.Text.Encoding.UTF8.GetString(body));
        return SKBitmap.Decode(body);
    }

    /// <summary>The mean absolute difference per channel of <paramref name="other"/>, shrunk to <paramref name="reference"/>'s size, from it.</summary>
    private static double Difference(SKBitmap reference, SKBitmap other)
    {
        using var shrunk = other.Resize(reference.Info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        double total = 0;
        for (int y = 0; y < reference.Height; y++)
        {
            for (int x = 0; x < reference.Width; x++)
            {
                SKColor a = reference.GetPixel(x, y), b = shrunk.GetPixel(x, y);
                total += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) + Math.Abs(a.Alpha - b.Alpha);
            }
        }

        return total / (4.0 * reference.Width * reference.Height);
    }

    /// <summary>The fraction of pixels drawn on.</summary>
    private static double Coverage(SKBitmap bitmap)
    {
        long drawn = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                    drawn++;
            }
        }

        return (double)drawn / (bitmap.Width * bitmap.Height);
    }

    /// <summary>The mean absolute change between horizontal neighbours: high for crisp edges, low for blurred ones.</summary>
    private static double Detail(SKBitmap bitmap)
    {
        double total = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 1; x < bitmap.Width; x++)
            {
                SKColor a = bitmap.GetPixel(x - 1, y), b = bitmap.GetPixel(x, y);
                total += Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue) + Math.Abs(a.Alpha - b.Alpha);
            }
        }

        return total / (bitmap.Width * bitmap.Height);
    }
}
