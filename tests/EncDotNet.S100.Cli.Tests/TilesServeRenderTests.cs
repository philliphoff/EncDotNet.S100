using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>s100 tiles serve</c> rendering datasets on demand (issue #865):
/// block selection matching <c>tiles export</c>, tiles identical to an export,
/// palettes, one render per block under concurrent requests, the cache, and the
/// palette routes over HTTP.
/// </summary>
public sealed class TilesServeRenderTests : IDisposable
{
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-serve-render-" + Guid.NewGuid().ToString("N"));

    public TilesServeRenderTests() => Directory.CreateDirectory(_root);

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

    private static TileArea Area(double west, double south, double east, double north)
    {
        var (minX, minY) = WebMercator.FromLonLat(west, south);
        var (maxX, maxY) = WebMercator.FromLonLat(east, north);
        return new TileArea(minX, minY, maxX, maxY);
    }

    public static TheoryData<double, double, double, double, int, int> Areas => new()
    {
        // A harbour, at a few zooms and block sizes.
        { -70.35, 41.25, -70.275, 41.325, 13, 4 },
        { -70.35, 41.25, -70.275, 41.325, 15, 4 },
        { -70.35, 41.25, -70.275, 41.325, 15, 3 },
        // Across the antimeridian, kept east of +180°.
        { 179.2, -17.5, 181.1, -15.9, 8, 4 },
        // Wider than the world at a low zoom.
        { -200, -60, 200, 60, 2, 4 },
    };

    [Theory]
    [MemberData(nameof(Areas))]
    public void Each_tile_renders_in_the_block_tiles_export_plans_for_it(
        double west, double south, double east, double north, int zoom, int blockSize)
    {
        var area = Area(west, south, east, north);
        var columns = XyzTileGrid.Columns(zoom, area.MinX, area.MaxX);
        var (firstRow, lastRow) = XyzTileGrid.Rows(zoom, area.MinY, area.MaxY);
        var blocks = HeadlessTileRenderer.PlanBlocks(zoom, columns, firstRow, lastRow, blockSize);

        foreach (var block in blocks)
        {
            for (int x = block.X; x < block.X + block.Columns; x++)
            {
                for (int y = block.Y; y < block.Y + block.Rows; y++)
                    Assert.Equal(block, RenderedTileSource.BlockFor(area, blockSize, zoom, x, y));
            }
        }

        // A tile outside the area has no block.
        int n = XyzTileGrid.TilesPerAxis(zoom);
        if (lastRow < n - 1)
            Assert.Null(RenderedTileSource.BlockFor(area, blockSize, zoom, columns[0], lastRow + 1));
        if (columns.Count < n)
        {
            int outside = Enumerable.Range(0, n).First(c => !columns.Contains(c));
            Assert.Null(RenderedTileSource.BlockFor(area, blockSize, zoom, outside, firstRow));
        }
    }

    private RenderedTileSource OpenRendered(int minZoom, int maxZoom, int parallel = 2)
    {
        var settings = new TilesServeCommand.Settings
        {
            Input = S57,
            MinZoom = minZoom,
            MaxZoom = maxZoom,
            Parallel = parallel,
        };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    [Fact]
    public async Task Rendered_tiles_match_an_export_of_the_same_datasets()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        var exported = Path.Combine(_root, "export");
        Assert.Equal(0, CliApp.Build().Run(
            ["tiles", "export", S57, "-o", exported, "--min-zoom", "13", "--max-zoom", "14", "--skip-empty"]));
        var files = Directory.GetFiles(exported, "*.png", SearchOption.AllDirectories);
        Assert.NotEmpty(files);

        using var source = OpenRendered(13, 14);
        Assert.Equal((13, 14), (source.Layout.MinZoom, source.Layout.MaxZoom));

        // Every exported tile is served byte for byte…
        foreach (var file in files)
        {
            var parts = Path.GetRelativePath(exported, file).Split(Path.DirectorySeparatorChar);
            int zoom = int.Parse(parts[0], CultureInfo.InvariantCulture);
            int x = int.Parse(parts[1], CultureInfo.InvariantCulture);
            int y = int.Parse(Path.GetFileNameWithoutExtension(parts[2]), CultureInfo.InvariantCulture);
            Assert.Equal(File.ReadAllBytes(file), await source.ReadAsync(zoom, x, y, null, TestContext.Current.CancellationToken));
        }

        // …and nothing else: the rest of the area is empty.
        int served = 0;
        for (int zoom = 13; zoom <= 14; zoom++)
        {
            var area = source.Layout.Area;
            var (firstRow, lastRow) = XyzTileGrid.Rows(zoom, area.MinY, area.MaxY);
            foreach (int x in XyzTileGrid.Columns(zoom, area.MinX, area.MaxX))
            {
                for (int y = firstRow; y <= lastRow; y++)
                {
                    if (await source.ReadAsync(zoom, x, y, null, TestContext.Current.CancellationToken) is not null)
                        served++;
                }
            }
        }

        Assert.Equal(files.Length, served);

        // Outside the zoom range there is nothing.
        var (cx, cy) = FirstTile(files, exported);
        Assert.Null(await source.ReadAsync(15, cx * 2, cy * 2, null, TestContext.Current.CancellationToken));
        Assert.Null(await source.ReadAsync(12, cx / 4, cy / 4, null, TestContext.Current.CancellationToken));
    }

    private static (int X, int Y) FirstTile(string[] files, string root)
    {
        var parts = Path.GetRelativePath(root, files.First(f => Path.GetRelativePath(root, f).StartsWith("14", StringComparison.Ordinal)))
            .Split(Path.DirectorySeparatorChar);
        return (int.Parse(parts[1], CultureInfo.InvariantCulture), int.Parse(Path.GetFileNameWithoutExtension(parts[2]), CultureInfo.InvariantCulture));
    }

    /// <summary>A tile of the test cell that draws something at zoom 14.</summary>
    private static async Task<(int X, int Y)> DrawnTileAsync(RenderedTileSource source)
    {
        var area = source.Layout.Area;
        var (firstRow, lastRow) = XyzTileGrid.Rows(14, area.MinY, area.MaxY);
        foreach (int x in XyzTileGrid.Columns(14, area.MinX, area.MaxX))
        {
            for (int y = firstRow; y <= lastRow; y++)
            {
                if (await source.ReadAsync(14, x, y, null, TestContext.Current.CancellationToken) is not null)
                    return (x, y);
            }
        }

        throw new InvalidOperationException("No tile drew anything.");
    }

    [Fact]
    public async Task Palettes_render_differently()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        using var source = OpenRendered(14, 14);
        var (x, y) = await DrawnTileAsync(source);

        var day = await source.ReadAsync(14, x, y, "day", TestContext.Current.CancellationToken);
        var night = await source.ReadAsync(14, x, y, "night", TestContext.Current.CancellationToken);

        Assert.Equal(day, await source.ReadAsync(14, x, y, null, TestContext.Current.CancellationToken));
        Assert.NotNull(night);
        Assert.NotEqual(day, night);
        Assert.Null(await source.ReadAsync(14, x, y, "sepia", TestContext.Current.CancellationToken));
        Assert.Equal("night", (string?)source.ToTileJson("night")["s100"]!["palette"]);
    }

    [Fact]
    public async Task Concurrent_requests_for_one_block_render_it_once()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        using var source = OpenRendered(14, 14, parallel: 4);
        var block = RenderedTileSource.BlockFor(source.Layout.Area, 4, 14,
            XyzTileGrid.Columns(14, source.Layout.Area.MinX, source.Layout.Area.MaxX)[0],
            XyzTileGrid.Rows(14, source.Layout.Area.MinY, source.Layout.Area.MaxY).First)!.Value;

        var reads = new List<Task<byte[]?>>();
        for (int x = block.X; x < block.X + block.Columns; x++)
        {
            for (int y = block.Y; y < block.Y + block.Rows; y++)
            {
                var (tx, ty) = (x, y);
                reads.Add(Task.Run(async () => await source.ReadAsync(14, tx, ty, "dusk", TestContext.Current.CancellationToken)));
            }
        }

        await Task.WhenAll(reads);

        Assert.Equal(1, source.BlocksRendered);
    }

    [Fact]
    public void The_cache_evicts_the_least_recently_used_tiles()
    {
        var cache = new TileCache(capacityBytes: 3 * (100 + 64));
        TileKey Key(int i) => new("day", 1, i, 0);
        for (int i = 0; i < 3; i++)
            cache.Add(Key(i), new byte[100]);
        Assert.True(cache.TryGet(Key(0), out _));

        cache.Add(Key(3), new byte[100]);

        Assert.True(cache.TryGet(Key(0), out _));
        Assert.False(cache.TryGet(Key(1), out _));
        Assert.True(cache.TryGet(Key(2), out _));
        Assert.True(cache.TryGet(Key(3), out _));
        Assert.True(cache.Size <= 3 * (100 + 64));

        // A tile larger than the whole cache isn't kept.
        cache.Add(Key(4), new byte[1000]);
        Assert.False(cache.TryGet(Key(4), out _));
    }

    [Fact]
    public async Task Rendered_tiles_are_served_in_each_palette_over_http()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        using var source = OpenRendered(14, 14);
        var (x, y) = await DrawnTileAsync(source);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "t0ken", cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        var json = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "night/tiles.json"), ct))!;
        Assert.Equal($"http://127.0.0.1:{server.Port}/t0ken/night/{{z}}/{{x}}/{{y}}.png", (string?)json["tiles"]![0]);
        Assert.Equal(["day", "dusk", "night"], json["s100"]!["palettes"]!.AsArray().Select(p => (string)p!));

        var root = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Equal($"http://127.0.0.1:{server.Port}/t0ken/{{z}}/{{x}}/{{y}}.png", (string?)root["tiles"]![0]);
        Assert.Equal("day", (string?)root["s100"]!["palette"]);

        using var night = await http.GetAsync(new Uri(server.BaseUri, $"night/14/{x}/{y}.png"), ct);
        night.EnsureSuccessStatusCode();
        Assert.Equal("image/png", night.Content.Headers.ContentType?.MediaType);
        Assert.Equal(await source.ReadAsync(14, x, y, "night", ct), await night.Content.ReadAsByteArrayAsync(ct));

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, $"sepia/14/{x}/{y}.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "sepia/tiles.json"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync(new Uri(server.BaseUri, "14/0/0.png"), ct)).StatusCode);
    }

    [Fact]
    public void Datasets_and_tile_sets_are_told_apart()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");

        Assert.False(new TilesServeCommand.Settings { Input = S57 }.ServesTileSet);
        Assert.True(new TilesServeCommand.Settings { Input = _root }.ServesTileSet);
        Assert.False(new TilesServeCommand.Settings { Layers = [S57] }.ServesTileSet);

        var result = new TilesServeCommand.Settings { Input = S57, CacheMegabytes = 0 }.Validate();
        Assert.False(result.Successful);
        Assert.Contains("--cache-mb", result.Message);
    }
}
