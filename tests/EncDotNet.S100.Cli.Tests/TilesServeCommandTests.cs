using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using Microsoft.Data.Sqlite;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>s100 tiles serve</c> (issue #865): reading each container
/// <c>tiles export</c> writes, and the XYZ server end to end on a free local
/// port — TileJSON, tiles, ETag revalidation, the token prefix, CORS and the
/// preview page.
/// </summary>
public sealed class TilesServeCommandTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-serve-" + Guid.NewGuid().ToString("N"));

    public TilesServeCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static readonly TileSetMetadata Metadata = new()
    {
        Name = "Test chart",
        Description = "Tiles for tests",
        Format = TileImageFormat.Png,
        MinZoom = 1,
        MaxZoom = 2,
        Bounds = (-10, -5, 10, 5),
        TilePixelSize = 256,
        Settings = new Dictionary<string, string> { ["palette"] = "dusk" },
    };

    /// <summary>Distinct stand-in content for each tile, so a flipped row or a wrong lookup shows.</summary>
    private static byte[] Content(int zoom, int x, int y, string version = "") =>
        Encoding.ASCII.GetBytes($"tile {zoom}/{x}/{y}{version}");

    private static readonly (int Zoom, int X, int Y)[] Tiles = [(1, 0, 0), (1, 1, 1), (2, 1, 0), (2, 1, 3), (2, 3, 2)];

    private string Write(TileContainer container, string version = "", string? path = null)
    {
        path ??= Path.Combine(_root, container switch
        {
            TileContainer.PmTiles => "set.pmtiles",
            TileContainer.MbTiles => "set.mbtiles",
            _ => "set",
        });

        using ITileSink sink = container switch
        {
            TileContainer.PmTiles => new PmTilesTileSink(path),
            TileContainer.MbTiles => new MbTilesTileSink(path),
            _ => new XyzDirectoryTileSink(path, TileImageFormat.Png),
        };
        foreach (var (zoom, x, y) in Tiles)
            sink.Write(zoom, x, y, Content(zoom, x, y, version));
        sink.Complete(Metadata);
        return path;
    }

    [Theory]
    [InlineData("Xyz")]
    [InlineData("PmTiles")]
    [InlineData("MbTiles")]
    public void Each_container_reads_back_its_tiles_and_metadata(string kind)
    {
        var container = Enum.Parse<TileContainer>(kind);
        var source = TileSource.Open(Write(container));

        Assert.Equal(container, source.Container);
        Assert.Equal(TileImageFormat.Png, source.Format);
        foreach (var (zoom, x, y) in Tiles)
            Assert.Equal(Content(zoom, x, y), source.Read(zoom, x, y));
        Assert.Null(source.Read(2, 0, 0));
        Assert.Null(source.Read(0, 0, 0));
        Assert.Null(source.Read(5, 3, 3));

        var json = source.ToTileJson();
        Assert.Equal("3.0.0", (string?)json["tilejson"]);
        Assert.Equal("xyz", (string?)json["scheme"]);
        Assert.Equal("png", (string?)json["format"]);
        Assert.Equal("Test chart", (string?)json["name"]);
        Assert.Equal(1, (int?)json["minzoom"]);
        Assert.Equal(2, (int?)json["maxzoom"]);
        Assert.Equal(-10, (double?)json["bounds"]![0]);
        Assert.Equal(5, (double?)json["bounds"]![3]);
        Assert.Equal(256, (int?)json["tileSize"]);
        Assert.Equal("dusk", (string?)json["s100"]!["palette"]);
        Assert.Null(json["tiles"]);
    }

    [Fact]
    public void A_pmtiles_archive_with_leaf_directories_reads_every_tile()
    {
        // Enough distinct tiles that the root directory can't hold them all.
        var path = Path.Combine(_root, "leaves.pmtiles");
        var tiles = Enumerable.Range(0, 20_000).Select(i => (Zoom: 8, X: i % 256, Y: i / 256)).ToArray();
        using (var sink = new PmTilesTileSink(path))
        {
            foreach (var (zoom, x, y) in tiles)
                sink.Write(zoom, x, y, Content(zoom, x, y));
            sink.Complete(Metadata with { MinZoom = 8, MaxZoom = 8 });
        }

        var source = TileSource.Open(path);

        foreach (var (zoom, x, y) in tiles.Where((_, i) => i % 97 == 0).Append(tiles[^1]))
            Assert.Equal(Content(zoom, x, y), source.Read(zoom, x, y));
        Assert.Null(source.Read(8, 255, 255));
    }

    [Fact]
    public void A_directory_without_tiles_json_is_read_from_its_files()
    {
        var path = Write(TileContainer.Xyz);
        File.Delete(Path.Combine(path, XyzDirectoryTileSink.TileJsonFileName));

        var source = TileSource.Open(path);

        Assert.Equal(TileImageFormat.Png, source.Format);
        Assert.Equal(Content(2, 1, 3), source.Read(2, 1, 3));
        var json = source.ToTileJson();
        Assert.Equal(1, (int?)json["minzoom"]);
        Assert.Equal(2, (int?)json["maxzoom"]);
    }

    [Fact]
    public void Paths_that_are_not_raster_tile_sets_are_refused()
    {
        var text = Path.Combine(_root, "notes.txt");
        File.WriteAllText(text, "not tiles");
        Assert.Throws<InvalidDataException>(() => TileSource.Open(text));

        var empty = Path.Combine(_root, "empty");
        Directory.CreateDirectory(empty);
        Assert.Throws<InvalidDataException>(() => TileSource.Open(empty));

        // An MBTiles database of vector tiles.
        var vector = Write(TileContainer.MbTiles);
        using (var connection = new SqliteConnection($"Data Source={vector};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE metadata SET value = 'pbf' WHERE name = 'format'";
            command.ExecuteNonQuery();
        }

        Assert.Throws<NotSupportedException>(() => TileSource.Open(vector));
        Assert.Equal(2, CliApp.Build().Run(["tiles", "serve", text]));
    }

    [Fact]
    public async Task Served_tiles_tilejson_and_preview_work_from_a_web_map()
    {
        var source = TileSource.Open(Write(TileContainer.PmTiles));
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "secret-token", cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal($"http://127.0.0.1:{server.Port}/secret-token/", server.BaseUri.AbsoluteUri);

        // TileJSON, with an absolute tile URL under the token, open to any origin.
        using (var response = await http.GetAsync(new Uri(server.BaseUri, "tiles.json"), ct))
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
            Assert.Equal($"http://127.0.0.1:{server.Port}/secret-token/{{z}}/{{x}}/{{y}}.png", (string?)json["tiles"]![0]);
            Assert.Equal("dusk", (string?)json["s100"]!["palette"]);
        }

        // A tile, with an ETag that revalidates.
        var tileUri = new Uri(server.BaseUri, "2/1/3.png");
        using (var response = await http.GetAsync(tileUri, ct))
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(Content(2, 1, 3), await response.Content.ReadAsByteArrayAsync(ct));

            using var conditional = new HttpRequestMessage(HttpMethod.Get, tileUri);
            conditional.Headers.IfNoneMatch.Add(response.Headers.ETag!);
            using var notModified = await http.SendAsync(conditional, ct);
            Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        }

        // A tile the set doesn't have is empty; malformed or foreign requests are not found.
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync(new Uri(server.BaseUri, "2/0/0.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "2/1/3.jpg"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "2/4/0.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "2/x/0.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await http.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/2/1/3.png"), ct)).StatusCode);

        // The preview page, reached with or without the trailing slash.
        using (var page = await http.GetAsync(new Uri($"http://127.0.0.1:{server.Port}/secret-token"), ct))
        {
            page.EnsureSuccessStatusCode();
            Assert.Equal(server.BaseUri, page.RequestMessage!.RequestUri);
            Assert.Equal("text/html", page.Content.Headers.ContentType?.MediaType);
            Assert.Contains("tiles.json", await page.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Without_the_viewer_the_root_is_not_found()
    {
        var source = TileSource.Open(Write(TileContainer.Xyz));
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, viewer: false, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(server.BaseUri, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri(server.BaseUri, "1/0/0.png"), TestContext.Current.CancellationToken)).StatusCode);
    }

    [Theory]
    [InlineData("Xyz")]
    [InlineData("PmTiles")]
    [InlineData("MbTiles")]
    public async Task A_tile_set_exported_again_while_served_is_picked_up(string kind)
    {
        var container = Enum.Parse<TileContainer>(kind);
        var path = Write(container);
        var source = TileSource.Open(path);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var tileUri = new Uri(server.BaseUri, "2/3/2.png");
        Assert.Equal(Content(2, 3, 2), await http.GetByteArrayAsync(tileUri, TestContext.Current.CancellationToken));

        // Written again in place, as `tiles export` does, with new content.
        SqliteConnection.ClearAllPools();
        Write(container, " v2", path);
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(1));

        Assert.Equal(Content(2, 3, 2, " v2"), await http.GetByteArrayAsync(tileUri, TestContext.Current.CancellationToken));
    }

    [Fact]
    public void An_exported_chart_is_served()
    {
        var s57 = Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");
        Assert.SkipUnless(File.Exists(s57), "S-57 fixture not present.");

        var path = Path.Combine(_root, "chart.mbtiles");
        Assert.Equal(0, CliApp.Build().Run(["tiles", "export", s57, "-o", path, "--min-zoom", "13", "--max-zoom", "13", "--skip-empty"]));

        var source = TileSource.Open(path);
        var json = source.ToTileJson();
        Assert.Equal(13, (int?)json["minzoom"]);
        var bounds = json["bounds"]!.AsArray().Select(b => (double)b!).ToArray();

        // Some tile across the set's bounds holds a PNG.
        var (x0, y0) = TileAt(bounds[0], bounds[3]);
        var (x1, y1) = TileAt(bounds[2], bounds[1]);
        var tiles = (from x in Enumerable.Range(x0, x1 - x0 + 1)
                     from y in Enumerable.Range(y0, y1 - y0 + 1)
                     select source.Read(13, x, y)).Where(t => t is not null).ToList();
        Assert.NotEmpty(tiles);
        Assert.All(tiles, t => Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, t![..4]));

        static (int X, int Y) TileAt(double longitude, double latitude)
        {
            int n = 1 << 13;
            double radians = latitude * Math.PI / 180;
            return ((int)((longitude + 180) / 360 * n),
                (int)((1 - (Math.Log(Math.Tan(radians) + (1 / Math.Cos(radians))) / Math.PI)) / 2 * n));
        }
    }

    [Theory]
    [InlineData("missing-folder", "127.0.0.1", null, "does not exist")]
    [InlineData(".", "localhost", null, "not an IP address")]
    [InlineData(".", "127.0.0.1", "bad/token", "--token may contain")]
    public void Settings_are_validated(string path, string host, string? token, string error)
    {
        var settings = new TilesServeCommand.Settings { Path = path, Host = host, Token = token };

        var result = settings.Validate();

        Assert.False(result.Successful);
        Assert.Contains(error, result.Message);
    }

    [Fact]
    public void Serving_beyond_this_machine_gets_a_token()
    {
        Assert.Null(ServeHost.ResolveToken(null, noToken: false, IPAddress.Loopback));
        Assert.Matches("^[A-Za-z0-9_-]{16}$", ServeHost.ResolveToken(null, noToken: false, IPAddress.Any));
        Assert.Null(ServeHost.ResolveToken(null, noToken: true, IPAddress.Any));
    }
}
