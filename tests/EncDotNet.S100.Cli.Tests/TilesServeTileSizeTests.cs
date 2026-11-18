using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for high-DPI tiles from <c>tiles serve</c> (issue #1563): with
/// <c>--tile-size 512</c>, rendered datasets are served as 512-pixel tiles of
/// the 256-pixel grid, drawn at twice the pixel density, the same as
/// <c>tiles export --tile-size 512</c> writes them; TileJSON and WMTS describe
/// them; the disk cache keeps them apart from 256-pixel tiles; and the
/// overview draws at the same size. At any tile size, <c>{y}@2x</c> URLs and
/// <c>tiles.json?scale=2</c> serve the 512-pixel tiles from the same server.
/// </summary>
public sealed class TilesServeTileSizeTests : IDisposable
{
    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";
    private static readonly XNamespace Ows = "http://www.opengis.net/ows/1.1";

    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-serve-size-" + Guid.NewGuid().ToString("N"));

    public TilesServeTileSizeTests() => Directory.CreateDirectory(_root);

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

    private static RenderedTileSource OpenRendered(TilesServeCommand.Settings settings)
    {
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    [Fact]
    public async Task Served_512_pixel_tiles_match_an_export_at_512()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;

        var exported = Path.Combine(_root, "export");
        Assert.Equal(0, CliApp.Build().Run(
            ["tiles", "export", S57, "-o", exported, "--min-zoom", "14", "--max-zoom", "14", "--tile-size", "512", "--skip-empty"], ct));
        var files = Directory.GetFiles(exported, "*.png", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        using var source = OpenRendered(new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14, TileSize = 512, Parallel = 2 });
        Assert.Equal(512, (int?)source.ToTileJson(palette: null)["tileSize"]);

        foreach (var file in files)
        {
            var parts = Path.GetRelativePath(exported, file).Split(Path.DirectorySeparatorChar);
            int x = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int y = int.Parse(Path.GetFileNameWithoutExtension(parts[2]), CultureInfo.InvariantCulture);
            var served = await source.ReadAsync(14, x, y, null, null, ct);
            Assert.Equal(File.ReadAllBytes(file), served);
            using var bitmap = SKBitmap.Decode(served);
            Assert.Equal((512, 512), (bitmap.Width, bitmap.Height));
        }
    }

    [Fact]
    public async Task A_512_pixel_tile_draws_the_256_pixel_tile_at_twice_the_density()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;

        using var standard = OpenRendered(new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14, Parallel = 2 });
        using var dense = OpenRendered(new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14, TileSize = 512, Parallel = 2 });

        var (x, y) = await DrawnTileAsync(standard, ct);
        using var small = SKBitmap.Decode(await standard.ReadAsync(14, x, y, null, null, ct));
        using var large = SKBitmap.Decode(await dense.ReadAsync(14, x, y, null, null, ct));
        Assert.Equal(256, small.Width);
        Assert.Equal(512, large.Width);

        // Not a resampled 256 tile: the large tile has detail a 2x upscale
        // can't, so scaled back down it is close to, but not the same as, the
        // small tile.
        using var halved = large.Resize(new SKImageInfo(256, 256), new SKSamplingOptions(SKCubicResampler.Mitchell));
        Assert.True(MeanDifference(small, halved) < 24, "The 512 tile shows the same place as the 256 tile.");
        using var upscaled = small.Resize(new SKImageInfo(512, 512), new SKSamplingOptions(SKFilterMode.Nearest));
        Assert.NotEqual(upscaled.Bytes, large.Bytes);
    }

    [Fact]
    public async Task Wmts_describes_512_pixel_matrices_for_every_layer()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;

        using var source = OpenRendered(new TilesServeCommand.Settings
        {
            Input = S57,
            MinZoom = 13,
            MaxZoom = 14,
            TileSize = 512,
            LayersBy = "product",
            Parallel = 2,
        });
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        var capabilities = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
        var contents = capabilities.Root!.Element(Wmts + "Contents")!;
        var sets = contents.Elements(Wmts + "TileMatrixSet").ToList();
        Assert.NotEmpty(sets);
        Assert.All(sets.SelectMany(s => s.Elements(Wmts + "TileMatrix")), matrix =>
        {
            Assert.Equal("512", matrix.Element(Wmts + "TileWidth")!.Value);
            Assert.Equal("512", matrix.Element(Wmts + "TileHeight")!.Value);
        });

        // The layer of every dataset and the chart layer both link to a 512 set.
        var setIds = sets.Select(s => s.Element(Ows + "Identifier")!.Value).ToHashSet();
        var layers = contents.Elements(Wmts + "Layer").ToList();
        Assert.True(layers.Count >= 2);
        Assert.All(layers, layer => Assert.Contains(layer.Element(Wmts + "TileMatrixSetLink")!.Element(Wmts + "TileMatrixSet")!.Value, setIds));

        // A tile through WMTS is the XYZ tile at the same zoom.
        var (x, y) = await DrawnTileAsync(source, ct);
        var setId = layers[0].Element(Wmts + "TileMatrixSetLink")!.Element(Wmts + "TileMatrixSet")!.Value;
        var wmtsTile = await http.GetByteArrayAsync(new Uri(server.BaseUri, $"wmts/1.0.0/s100/default/{setId}/14/{y}/{x}.png"), ct);
        Assert.Equal(await http.GetByteArrayAsync(new Uri(server.BaseUri, $"14/{x}/{y}.png"), ct), wmtsTile);
        using var bitmap = SKBitmap.Decode(wmtsTile);
        Assert.Equal(512, bitmap.Width);
    }

    [Fact]
    public void The_disk_cache_keeps_512_pixel_tiles_apart()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        Assert.NotEqual(
            FingerprintOf(new TilesServeCommand.Settings { Input = S57 }),
            FingerprintOf(new TilesServeCommand.Settings { Input = S57, TileSize = 512 }));
    }

    [Fact]
    public async Task The_overview_draws_512_pixel_tiles()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;

        var rendered = OpenRendered(new TilesServeCommand.Settings { Input = S57, Overview = true, MinZoom = 12, MaxZoom = 13, TileSize = 512, Parallel = 2 });
        await using var overview = new OverviewTileSource(rendered);
        var (x, y) = OverviewTile(rendered, 9);

        var data = await overview.ReadAsync(9, x, y, palette: null, time: null, ct);
        Assert.NotNull(data);
        using var tile = SKBitmap.Decode(data);
        Assert.Equal(512, tile.Width);
    }

    [Fact]
    public async Task An_at_2x_tile_is_the_tile_a_512_pixel_server_draws()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;

        using var standard = OpenRendered(new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14, Parallel = 2 });
        using var dense = OpenRendered(new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14, TileSize = 512, Parallel = 2 });
        Assert.True(standard.CanReadPixelRatio(2));
        Assert.False(standard.CanReadPixelRatio(3));

        var (x, y) = await DrawnTileAsync(standard, ct);
        var plain = await standard.ReadAsync(14, x, y, null, null, ct);
        var highDpi = await standard.ReadAsync(14, x, y, null, null, pixelRatio: 2, ct);
        Assert.Equal(await dense.ReadAsync(14, x, y, null, null, ct), highDpi);
        using (var bitmap = SKBitmap.Decode(highDpi))
            Assert.Equal(512, bitmap.Width);

        // Each size is cached apart: the plain tile is still the 256 one.
        Assert.Equal(plain, await standard.ReadAsync(14, x, y, null, null, ct));
        Assert.Equal(plain, await standard.ReadAsync(14, x, y, null, null, pixelRatio: 1, ct));

        // A 512-pixel server's @2x tiles are its plain tiles.
        Assert.Equal(await dense.ReadAsync(14, x, y, null, null, ct), await dense.ReadAsync(14, x, y, null, null, pixelRatio: 2, ct));
        using (var small = SKBitmap.Decode(await dense.ReadAsync(14, x, y, null, null, pixelRatio: 1, ct)))
            Assert.Equal(256, small.Width);

        // Other palettes and layers at 2x too.
        using var night = SKBitmap.Decode(await standard.ReadAsync(14, x, y, "night", null, pixelRatio: 2, ct));
        Assert.Equal(512, night.Width);
    }

    [Fact]
    public async Task The_server_answers_at_2x_urls_and_a_scaled_tilejson()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;

        using var source = OpenRendered(new TilesServeCommand.Settings
        {
            Input = S57,
            MinZoom = 14,
            MaxZoom = 14,
            LayersBy = "product",
            Parallel = 2,
        });
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: ct);
        using var http = new HttpClient();
        var (x, y) = await DrawnTileAsync(source, ct);

        foreach (var path in new[] { $"14/{x}/{y}@2x.png", $"dusk/14/{x}/{y}@2x.png", $"layers/{source.Layers[0]}/14/{x}/{y}@2x.png" })
        {
            using var response = await http.GetAsync(new Uri(server.BaseUri, path), ct);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var bitmap = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync(ct));
            Assert.Equal(512, bitmap.Width);
        }

        using (var plain = SKBitmap.Decode(await http.GetByteArrayAsync(new Uri(server.BaseUri, $"14/{x}/{y}.png"), ct)))
            Assert.Equal(256, plain.Width);

        // The TileJSON lists the pixel ratios, and with scale=2 points at the @2x tiles.
        var json = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Equal(256, (int?)json["tileSize"]);
        Assert.Equal([1, 2], json["s100"]!["pixelRatios"]!.AsArray().Select(r => (int)r!));
        Assert.EndsWith("/tok/{z}/{x}/{y}.png", (string?)json["tiles"]![0]);

        var scaled = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "night/tiles.json?scale=2&t=2026-01-01T00:00Z"), ct))!;
        Assert.Equal(512, (int?)scaled["tileSize"]);
        Assert.EndsWith("/tok/night/{z}/{x}/{y}@2x.png", (string?)scaled["tiles"]![0]);
        var layer = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, $"layers/{source.Layers[0]}/tiles.json?scale=2"), ct))!;
        Assert.EndsWith($"/layers/{source.Layers[0]}/{{z}}/{{x}}/{{y}}@2x.png", (string?)layer["tiles"]![0]);

        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(new Uri(server.BaseUri, "tiles.json?scale=3"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, $"14/{x}/{y}@3x.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, $"14/{x}/@2x.png"), ct)).StatusCode);
    }

    [Fact]
    public async Task A_built_tile_set_has_no_at_2x_tiles()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;

        var exported = Path.Combine(_root, "built");
        Assert.Equal(0, CliApp.Build().Run(["tiles", "export", S57, "-o", exported, "--min-zoom", "13", "--max-zoom", "13", "--skip-empty"], ct));
        var file = Directory.GetFiles(exported, "*.png", SearchOption.AllDirectories).First();
        var parts = Path.GetRelativePath(exported, file).Split(Path.DirectorySeparatorChar);
        var row = Path.GetFileNameWithoutExtension(parts[2]);

        var source = TileSource.Open(exported);
        Assert.False(source.CanReadPixelRatio(2));
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri(server.BaseUri, $"13/{parts[1]}/{row}.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, $"13/{parts[1]}/{row}@2x.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "tiles.json?scale=2"), ct)).StatusCode);
        var json = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Null(json["s100"]?["pixelRatios"]);
    }

    [Fact]
    public async Task At_2x_tiles_are_kept_on_disk_apart_and_reused()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;
        var settings = new TilesServeCommand.Settings { Input = S57, MinZoom = 14, MaxZoom = 14, Parallel = 2, CacheDirectory = Path.Combine(_root, "cache") };

        byte[]? plain, highDpi;
        int x, y;
        string fingerprint;
        using (var first = OpenRendered(settings))
        {
            (x, y) = await DrawnTileAsync(first, ct);
            plain = await first.ReadAsync(14, x, y, null, null, ct);
            highDpi = await first.ReadAsync(14, x, y, null, null, pixelRatio: 2, ct);
            Assert.True(first.WaitForDiskWrites(TimeSpan.FromSeconds(30)));
            fingerprint = first.Revision;
        }

        var folder = Path.Combine(_root, "cache", DiskTileCache.KindName, fingerprint);
        string palette = settings.Palette.ToLowerInvariant();
        Assert.Equal(highDpi, File.ReadAllBytes(Path.Combine(folder, palette, "2x", "14", x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture) + ".png")));
        Assert.Equal(plain, File.ReadAllBytes(Path.Combine(folder, palette, "14", x.ToString(CultureInfo.InvariantCulture), y.ToString(CultureInfo.InvariantCulture) + ".png")));

        using var second = OpenRendered(settings);
        Assert.Equal(highDpi, await second.ReadAsync(14, x, y, null, null, pixelRatio: 2, ct));
        Assert.Equal(plain, await second.ReadAsync(14, x, y, null, null, ct));
        Assert.Equal(0, second.BlocksRendered);
    }

    [Fact]
    public async Task The_overview_draws_at_2x_tiles()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var ct = TestContext.Current.CancellationToken;

        var rendered = OpenRendered(new TilesServeCommand.Settings { Input = S57, Overview = true, MinZoom = 12, MaxZoom = 13, Parallel = 2 });
        await using var overview = new OverviewTileSource(rendered);
        Assert.True(overview.CanReadPixelRatio(2));
        var (x, y) = OverviewTile(rendered, 9);

        using var plain = SKBitmap.Decode(await overview.ReadAsync(9, x, y, palette: null, time: null, ct));
        using var highDpi = SKBitmap.Decode(await overview.ReadAsync(9, x, y, palette: null, time: null, pixelRatio: 2, ct));
        Assert.Equal(256, plain.Width);
        Assert.Equal(512, highDpi.Width);
    }

    /// <summary>The tile at <paramref name="zoom"/> over the middle of the datasets.</summary>
    private static (int X, int Y) OverviewTile(RenderedTileSource rendered, int zoom)
    {
        var (west, south, east, north) = rendered.Layout.Bounds;
        var (mx, my) = WebMercator.FromLonLat((west + east) / 2, (south + north) / 2);
        int n = 1 << zoom;
        return ((int)((mx + XyzTileGrid.Extent) / (2 * XyzTileGrid.Extent) * n), (int)((XyzTileGrid.Extent - my) / (2 * XyzTileGrid.Extent) * n));
    }

    private static string FingerprintOf(TilesServeCommand.Settings settings)
    {
        Assert.Equal(0, TileRenderSession.TryOpen(settings, out var session));
        using (session)
            return session!.Fingerprint();
    }

    private static async Task<(int X, int Y)> DrawnTileAsync(RenderedTileSource source, CancellationToken ct)
    {
        var area = source.Layout.Area;
        var (firstRow, lastRow) = XyzTileGrid.Rows(14, area.MinY, area.MaxY);
        foreach (int x in XyzTileGrid.Columns(14, area.MinX, area.MaxX))
        {
            for (int y = firstRow; y <= lastRow; y++)
            {
                if (await source.ReadAsync(14, x, y, null, null, ct) is not null)
                    return (x, y);
            }
        }

        throw new InvalidOperationException("No tile drew anything.");
    }

    private static double MeanDifference(SKBitmap a, SKBitmap b)
    {
        long total = 0;
        for (int y = 0; y < a.Height; y++)
        {
            for (int x = 0; x < a.Width; x++)
            {
                var p = a.GetPixel(x, y);
                var q = b.GetPixel(x, y);
                total += Math.Abs(p.Red - q.Red) + Math.Abs(p.Green - q.Green) + Math.Abs(p.Blue - q.Blue) + Math.Abs(p.Alpha - q.Alpha);
            }
        }

        return total / (4.0 * a.Width * a.Height);
    }
}
