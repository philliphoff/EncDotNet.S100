using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Cli.Web;
using EncDotNet.S100.Rendering.Scene;
using Microsoft.AspNetCore.Http;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the <c>tiles serve</c> web page's Use in GIS panel and sharing
/// links (issue #1582): the links it builds for each layer and palette, the
/// masked token, the toasts, and that the links resolve on a running server,
/// with a token and without.
/// </summary>
public sealed class ServePageGisTests
{
    // Off Nantucket.
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    // Surface currents in Delaware Bay.
    private static string S111 => Path.Combine(AppContext.BaseDirectory, "TestData", "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

    [Fact]
    public void The_links_follow_the_layer_and_palette()
    {
        const string root = "http://192.168.1.20:8080/7Hq2xR9cLm/";

        var all = ServeLinks.Build(root, hasToken: true, showToken: true, "png", layer: null, palette: "day", defaultPalette: "day");
        Assert.Equal(
            [
                root + "{z}/{x}/{y}.png",
                root + "tiles.json",
                root + "wmts/1.0.0/WMTSCapabilities.xml",
                root + "wms?SERVICE=WMS&REQUEST=GetCapabilities",
            ],
            all.Select(l => l.Url));
        Assert.Equal([ServeLinkKind.Xyz, ServeLinkKind.TileJson, ServeLinkKind.Wmts, ServeLinkKind.Wms], all.Select(l => l.Kind));
        Assert.Equal("Layer s100, style day", all[2].Hint);

        var night = ServeLinks.Build(root, hasToken: true, showToken: true, "png", layer: "S111", palette: "night", defaultPalette: "day");
        Assert.Equal(root + "layers/S111/night/{z}/{x}/{y}.png", night[0].Url);
        Assert.Equal(root + "layers/S111/night/tiles.json", night[1].Url);
        // WMTS has a layer per group; WMS one layer of every dataset.
        Assert.Equal("Layer S111, style night", night[2].Hint);
        Assert.Equal("Layer s100, style night", night[3].Hint);

        // A built tile set: no palettes, one style.
        var built = ServeLinks.Build("http://127.0.0.1:8200/", hasToken: false, showToken: false, "webp", layer: null, palette: null, defaultPalette: null);
        Assert.Equal("http://127.0.0.1:8200/{z}/{x}/{y}.webp", built[0].Url);
        Assert.Equal("Layer s100, style default", built[2].Hint);
    }

    [Fact]
    public void The_token_is_masked_unless_shown_and_copied_in_full()
    {
        const string root = "http://192.168.1.20:8080/7Hq2xR9cLm/";

        var masked = ServeLinks.Build(root, hasToken: true, showToken: false, "png", layer: null, palette: null, defaultPalette: null);
        Assert.Equal("…/••••••••/tiles.json", masked[1].Shown);
        Assert.Equal("…/••••••••/{z}/{x}/{y}.png", masked[0].Shown);
        Assert.All(masked, l => Assert.DoesNotContain("7Hq2xR9cLm", l.Shown, StringComparison.Ordinal));
        Assert.All(masked, l => Assert.StartsWith(root, l.Url, StringComparison.Ordinal));

        var shown = ServeLinks.Build(root, hasToken: true, showToken: true, "png", layer: null, palette: null, defaultPalette: null);
        Assert.All(shown, l => Assert.Equal(l.Url, l.Shown));

        // Without a token there's nothing to mask.
        var open = ServeLinks.Build("http://127.0.0.1:8200/", hasToken: false, showToken: false, "png", layer: null, palette: null, defaultPalette: null);
        Assert.All(open, l => Assert.Equal(l.Url, l.Shown));
    }

    [Fact]
    public void The_toast_says_when_the_token_went_with_the_link()
    {
        Assert.Equal(new CopiedToast("Link copied, with the token", "Share it only with people who should see these charts.", "lock"), CopiedToast.For(copied: true, hasToken: true));
        Assert.Equal(new CopiedToast("Link copied", null, "check"), CopiedToast.For(copied: true, hasToken: false));
        Assert.Equal("Couldn't copy the link", CopiedToast.For(copied: false, hasToken: true).Title);
    }

    [Fact]
    public void The_public_base_url_is_the_requests_scheme_host_and_path_base()
    {
        // As the host's forwarded-headers handling leaves the request, so the
        // page and the TileJSON agree.
        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("charts.example");
        context.Request.PathBase = "/proxy/7Hq2xR9cLm";
        context.Request.Path = "/tiles.json";

        Assert.Equal("https://charts.example/proxy/7Hq2xR9cLm/", TileServer.RequestBaseUri(context.Request).AbsoluteUri);

        var folder = Path.Combine(Path.GetTempPath(), "serve-gis-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(folder, "3", "4"));
        File.WriteAllBytes(Path.Combine(folder, "3", "4", "2.png"), [0x89, (byte)'P', (byte)'N', (byte)'G']);
        try
        {
            IServePageData data = new TileSourcePageData(TileSource.Open(folder), hasToken: true);
            Assert.True(data.HasToken);
            Assert.Equal(TileServer.RequestBaseUri(context.Request), data.PublicBaseUri(context.Request));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("t0ken")]
    public async Task The_links_resolve_on_a_running_server(string? token)
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S111), "S-57 or S-111 fixture not present.");
        var settings = new TilesServeCommand.Settings { Layers = [S57, S111], LayersBy = TileLayers.Product, MinZoom = 11, MaxZoom = 14, Metatile = 1, Parallel = 2 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        using var _ = source!;
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source!, IPAddress.Loopback, 0, token, cancellationToken: ct);
        using var http = new HttpClient();
        var root = server.BaseUri.AbsoluteUri;
        var data = new TileSourcePageData(source!, hasToken: token is not null);
        var tileSet = data.TileSet;

        foreach (var layer in new string?[] { null }.Concat(data.Layers))
        {
            foreach (var palette in data.Palettes)
            {
                var links = ServeLinks.Build(root, data.HasToken, showToken: false, tileSet.Format, layer, palette, tileSet.DefaultPalette);

                // The TileJSON's tiles are the XYZ link (with the time step, for time-varying tiles).
                var tileJson = JsonNode.Parse(await http.GetStringAsync(links[1].Url, ct))!;
                var tiles = (string)tileJson["tiles"]![0]!;
                Assert.StartsWith(links[0].Url, tiles, StringComparison.Ordinal);

                // A tile of the XYZ link, at the centre: drawn or empty, never missing.
                var center = tileJson["center"]!.AsArray().Select(n => (double)n!).ToArray();
                var tile = TileUrl(links[0].Url, tileSet.MinZoom, center[0], center[1]);
                var response = await http.GetAsync(tile, ct);
                Assert.True(response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent, $"{tile}: {response.StatusCode}");

                foreach (var link in links.Where(l => l.Kind is ServeLinkKind.Wmts or ServeLinkKind.Wms))
                {
                    using var capabilities = await http.GetAsync(link.Url, ct);
                    Assert.Equal(HttpStatusCode.OK, capabilities.StatusCode);
                    Assert.Contains("xml", capabilities.Content.Headers.ContentType?.MediaType, StringComparison.Ordinal);
                }

                // Without the token, nothing.
                if (token is not null)
                {
                    using var bare = await http.GetAsync(links[1].Url.Replace("/" + token + "/", "/", StringComparison.Ordinal), ct);
                    Assert.Equal(HttpStatusCode.NotFound, bare.StatusCode);
                }
            }
        }

        // The header's link button warns that a copied link carries the token (D27).
        var html = WebUtility.HtmlDecode(await http.GetStringAsync(server.BaseUri, ct));
        Assert.Equal(token is not null, html.Contains("Copy a link to this view. It contains the access token.", StringComparison.Ordinal));
    }

    /// <summary>The XYZ template filled in for the tile of <paramref name="zoom"/> holding a point.</summary>
    private static string TileUrl(string template, int zoom, double longitude, double latitude)
    {
        var (mx, my) = WebMercator.FromLonLat(longitude, latitude);
        double size = XyzTileGrid.TileWorldSize(zoom);
        int x = (int)Math.Floor((mx + XyzTileGrid.Extent) / size);
        int y = (int)Math.Floor((XyzTileGrid.Extent - my) / size);
        return template
            .Replace("{z}", zoom.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{x}", x.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{y}", y.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }
}
