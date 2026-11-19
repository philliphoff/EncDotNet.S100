using System.Net;
using System.Text;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Cli.Web;
using EncDotNet.S100.Cli.Web.Panels;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the <c>tiles serve</c> web page's edge states and the built tile
/// set (issue #1584): the static 404 for unknown paths and a wrong token (D34),
/// the API's 404s left as they were, the Tile set panel's data (D35), and no
/// datasets left (D32).
/// </summary>
public sealed class ServePageEdgeStateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "serve-edge-" + Guid.NewGuid().ToString("N"));

    public ServePageEdgeStateTests() => Directory.CreateDirectory(_root);

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

    private static readonly TileSetMetadata Metadata = new()
    {
        Name = "Solent approaches",
        Description = "Tiles for tests",
        Format = TileImageFormat.Png,
        MinZoom = 13,
        MaxZoom = 16,
        Bounds = (-1.457, 50.567, -0.885, 50.883),
        TilePixelSize = 256,
        Settings = new Dictionary<string, string> { ["generator"] = "s100 9.9.9+abc123", ["palette"] = "day" },
    };

    private string WritePmTiles()
    {
        var path = Path.Combine(_root, "solent-approaches.pmtiles");
        using ITileSink sink = new PmTilesTileSink(path);
        sink.Write(13, 4044, 2738, Encoding.ASCII.GetBytes("tile"));
        sink.Complete(Metadata);
        return path;
    }

    [Fact]
    public async Task Unknown_paths_and_a_wrong_token_get_the_static_404_page()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = TileSource.Open(WritePmTiles());
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "secret-token", cancellationToken: ct);
        using var http = new HttpClient();
        var root = $"http://127.0.0.1:{server.Port}/";

        // No token, a wrong token, and an unknown path under the right one (D34).
        foreach (var url in new[] { root, root + "wrong-token/", root + "wrong-token/tiles.json", root + "secret-token/nothing/here" })
        {
            using var response = await http.GetAsync(new Uri(url), ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
            var html = await response.Content.ReadAsStringAsync(ct);
            Assert.Contains("<h1>Nothing here</h1>", html, StringComparison.Ordinal);
            Assert.Contains("Links to a server started with a token include it in the path.", html, StringComparison.Ordinal);

            // Nothing about what's served, and no live connection.
            Assert.DoesNotContain("Solent", html, StringComparison.Ordinal);
            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("secret-token", html, StringComparison.Ordinal);
        }

        // A HEAD gets the status without the body.
        using (var head = await http.SendAsync(new HttpRequestMessage(HttpMethod.Head, new Uri(root + "wrong-token/")), ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, head.StatusCode);
            Assert.Empty(await head.Content.ReadAsByteArrayAsync(ct));
        }

        // The health check still answers outside the token.
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri(root + "healthz"), ct)).StatusCode);
    }

    [Fact]
    public async Task The_apis_own_404s_stay_empty()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = TileSource.Open(WritePmTiles());
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        // A tile outside the grid, a tile in another format, a malformed row:
        // the tile endpoints answer, so their 404s are as they were.
        foreach (var path in new[] { "13/99999/0.png", "13/4044/2738.jpg", "13/x/0.png" })
        {
            using var response = await http.GetAsync(new Uri(server.BaseUri, path), ct);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync(ct));
        }

        // Without the page there's no 404 page either.
        await using var api = await TileServer.StartAsync(TileSource.Open(WritePmTiles()), IPAddress.Loopback, 0, token: null, viewer: false, cancellationToken: ct);
        using (var response = await http.GetAsync(new Uri(api.BaseUri, "nothing/here"), ct))
        {
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(await response.Content.ReadAsByteArrayAsync(ct));
        }
    }

    [Fact]
    public void A_built_tile_sets_panel_data_comes_from_its_tilejson()
    {
        var data = new TileSourcePageData(TileSource.Open(WritePmTiles()), hasToken: false);

        var built = Assert.IsType<BuiltTileSet>(data.Built);
        Assert.Equal("solent-approaches.pmtiles", built.File);
        Assert.Equal("PMTiles", built.Container);
        Assert.Equal(256, built.TileSize);
        Assert.Equal("s100 9.9.9+abc123", built.Generator);
        Assert.Equal("day", built.Palette);
        Assert.Null(built.Time);

        var tileSet = data.TileSet;
        Assert.Equal("Solent approaches · PMTiles", ServeSummary.Describe(data));
        Assert.Equal(["PMTiles", "PNG"], ServeTileSetInfo.Tags(tileSet, built));
        Assert.Equal("PNG, 256 px tiles", ServeTileSetInfo.Format(tileSet, built));
        Assert.Equal("No", ServeTileSetInfo.HighDpi(tileSet));
        Assert.Equal("13 – 16", ServeTileSetInfo.Zoom(tileSet));
        Assert.Equal(("50°34.0′N – 50°53.0′N", "001°27.4′W – 000°53.1′W"), ServeTileSetInfo.Bounds(tileSet));
        Assert.Equal("50°43.5′N 001°10.3′W", ServeTileSetInfo.Centre(tileSet));
        Assert.Equal("s100 9.9.9 · day palette", ServeTileSetInfo.MadeBy(built));

        // Another tool's tile set has no s100 block, so no "Made by".
        Assert.Null(ServeTileSetInfo.MadeBy(built with { Generator = null }));
        Assert.Equal("s100 1.0 · night palette · 2021-04-01 10:00 UTC",
            ServeTileSetInfo.MadeBy(new BuiltTileSet("x", "MBTiles", 512, "s100 1.0", "night", new DateTime(2021, 4, 1, 10, 0, 0, DateTimeKind.Utc))));
    }

    [Fact]
    public async Task The_page_for_a_built_tile_set_has_the_tile_set_panel()
    {
        var ct = TestContext.Current.CancellationToken;
        var source = TileSource.Open(WritePmTiles());
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        var html = WebUtility.HtmlDecode(await http.GetStringAsync(server.BaseUri, ct));

        Assert.Contains("<title>Solent approaches · PMTiles — s100 serve</title>", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"rail-label\">Tile set</span>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span class=\"rail-label\">Datasets</span>", html, StringComparison.Ordinal);
        Assert.Contains("<section class=\"panel\" aria-label=\"Tile set\">", html, StringComparison.Ordinal);
        Assert.Contains("solent-approaches.pmtiles", html, StringComparison.Ordinal);
        Assert.Contains("<dt>Made by</dt>", html, StringComparison.Ordinal);
        Assert.Contains("s100 9.9.9 · day palette", html, StringComparison.Ordinal);
        Assert.Contains("nothing to list or pick", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_datasets_left_says_how_to_add_one()
    {
        var data = new NoDatasets();
        Assert.Equal("No datasets", ServeSummary.Describe(data));

        var html = WebUtility.HtmlDecode(await RenderAsync<DatasetsPanel>(data, "http://192.168.1.20:8080/tok3n/"));

        Assert.Contains("<h3>No datasets are being served</h3>", html, StringComparison.Ordinal);
        Assert.Contains("--manage-datasets", html, StringComparison.Ordinal);
        Assert.Contains("curl -X POST http://192.168.1.20:8080/tok3n/datasets", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Search datasets", html, StringComparison.Ordinal);
    }

    private static async Task<string> RenderAsync<TComponent>(IServePageData data, string baseUri)
        where TComponent : IComponent
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(data);
        services.AddSingleton<NavigationManager>(new FixedNavigationManager(baseUri));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<TComponent>()).ToHtmlString());
    }

    private sealed class FixedNavigationManager : NavigationManager
    {
        public FixedNavigationManager(string baseUri) => Initialize(baseUri, baseUri);
    }

    private sealed class NoDatasets : IServePageData
    {
        public ServedTileSet TileSet { get; } = new("datasets", "png", 0, 14, null, null, "day", HighDpi: false);

        public IReadOnlyList<string> Palettes => [];

        public IReadOnlyList<DateTime> Times => [];

        public bool HasDatasets => true;

        public IReadOnlyList<ServedDataset> Datasets => [];

        public IReadOnlyList<string> Layers => [];

        public string? Revision => "0";

        public Task<FeatureQueryResult?> QueryFeaturesAsync(double latitude, double longitude, double radiusMeters, int maxResults, DateTime? time, CancellationToken cancellationToken) =>
            Task.FromResult<FeatureQueryResult?>(null);
    }
}
