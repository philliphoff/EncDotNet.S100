using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Infrastructure.Tiles;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the OGC WMTS 1.0.0 service of <c>s100 tiles serve</c>: the
/// capabilities document (layer, styles from palettes, the Time dimension,
/// the WebMercatorQuad matrices and their limits), GetTile in the KVP and
/// RESTful encodings, and OWS exception reports.
/// </summary>
public sealed class TilesServeWmtsTests
{
    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";
    private static readonly XNamespace Ows = "http://www.opengis.net/ows/1.1";

    private static readonly DateTime[] Steps =
    [
        new(2026, 3, 20, 18, 0, 0, DateTimeKind.Utc),
        new(2026, 3, 20, 19, 0, 0, DateTimeKind.Utc),
        new(2026, 3, 20, 20, 0, 0, DateTimeKind.Utc),
    ];

    /// <summary>
    /// A rendered-like source with palettes and time steps whose tiles name
    /// what was asked for, so a wrong palette, time, row or column shows.
    /// </summary>
    private sealed class FakeSource(bool timed, int tileSize = 256) : ITileSource
    {
        public string Path => "fake";

        public TileImageFormat Format => TileImageFormat.Png;

        public IReadOnlyList<string> Palettes => ["day", "dusk", "night"];

        public IReadOnlyList<DateTime> Times => timed ? Steps : [];

        public ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte[]?>(zoom < 1 || (zoom, x, y) == (2, 0, 0)
                ? null
                : Encoding.ASCII.GetBytes($"{zoom}/{x}/{y} {palette ?? "-"} {(time is { } t ? TileRenderSession.FormatTime(t) : "-")}"));

        public JsonObject ToTileJson(string? palette, DateTime? time = null) => new()
        {
            ["name"] = "Fake chart",
            ["description"] = "For tests",
            ["minzoom"] = 1,
            ["maxzoom"] = 3,
            ["bounds"] = new JsonArray(-10.0, -5.0, 10.0, 5.0),
            ["tileSize"] = tileSize,
            ["s100"] = new JsonObject { ["palette"] = palette ?? "dusk", ["timeStep"] = "1" },
        };
    }

    [Fact]
    public async Task Capabilities_describe_the_layer_styles_time_and_matrices()
    {
        var source = new FakeSource(timed: true);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        using var response = await http.GetAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal("application/xml", response.Content.Headers.ContentType?.MediaType);
        var rest = XDocument.Parse(await response.Content.ReadAsStringAsync(ct));

        // The KVP GetCapabilities gives the same document; parameter names and values are case-insensitive.
        var kvp = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "wmts?service=wmts&request=getcapabilities"), ct));
        Assert.Equal(rest.ToString(), kvp.ToString());

        var layer = rest.Descendants(Wmts + "Layer").Single();
        Assert.Equal(WmtsService.LayerIdentifier, layer.Element(Ows + "Identifier")!.Value);
        Assert.Equal("Fake chart", layer.Element(Ows + "Title")!.Value);
        Assert.Equal("image/png", layer.Element(Wmts + "Format")!.Value);
        Assert.Equal("-10 -5", layer.Element(Ows + "WGS84BoundingBox")!.Element(Ows + "LowerCorner")!.Value);

        // Palettes are styles; the server's default palette is the default style.
        var styles = layer.Elements(Wmts + "Style").ToList();
        Assert.Equal(["day", "dusk", "night"], styles.Select(s => s.Element(Ows + "Identifier")!.Value));
        Assert.Equal("dusk", styles.Single(s => (string?)s.Attribute("isDefault") == "true").Element(Ows + "Identifier")!.Value);

        // The time steps are a Time dimension, defaulting to --time-step.
        var dimension = layer.Element(Wmts + "Dimension")!;
        Assert.Equal(WmtsService.TimeDimension, dimension.Element(Ows + "Identifier")!.Value);
        Assert.Equal("2026-03-20T19:00:00Z", dimension.Element(Wmts + "Default")!.Value);
        Assert.Equal(Steps.Select(TileRenderSession.FormatTime), dimension.Elements(Wmts + "Value").Select(v => v.Value));

        // The RESTful template and KVP endpoint are absolute, under the token.
        var baseUrl = $"http://127.0.0.1:{server.Port}/tok/";
        Assert.Equal(
            baseUrl + "wmts/1.0.0/s100/{Style}/{Time}/{TileMatrixSet}/{TileMatrix}/{TileRow}/{TileCol}.png",
            (string?)layer.Element(Wmts + "ResourceURL")!.Attribute("template"));
        Assert.All(
            rest.Descendants(Ows + "Get"),
            get => Assert.Equal(baseUrl + "wmts?", (string?)get.Attribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href")));

        // WebMercatorQuad from zoom 0 to the maximum, limited to the bounds from the minimum.
        var set = rest.Descendants(Wmts + "TileMatrixSet").Single(e => e.Parent!.Name == Wmts + "Contents");
        Assert.Equal("urn:ogc:def:wkss:OGC:1.0:GoogleMapsCompatible", set.Element(Wmts + "WellKnownScaleSet")!.Value);
        var matrices = set.Elements(Wmts + "TileMatrix").ToList();
        Assert.Equal(["0", "1", "2", "3"], matrices.Select(m => m.Element(Ows + "Identifier")!.Value));
        Assert.Equal(559082264.0287178, double.Parse(matrices[0].Element(Wmts + "ScaleDenominator")!.Value, System.Globalization.CultureInfo.InvariantCulture), 1e-3);
        Assert.Equal("8", matrices[3].Element(Wmts + "MatrixWidth")!.Value);

        var limits = layer.Descendants(Wmts + "TileMatrixLimits").ToList();
        Assert.Equal(["1", "2", "3"], limits.Select(l => l.Element(Wmts + "TileMatrix")!.Value));
        var three = limits[2];
        Assert.Equal(("3", "4", "3", "4"), (
            three.Element(Wmts + "MinTileRow")!.Value, three.Element(Wmts + "MaxTileRow")!.Value,
            three.Element(Wmts + "MinTileCol")!.Value, three.Element(Wmts + "MaxTileCol")!.Value));
    }

    [Fact]
    public void Tiles_of_another_pixel_size_scale_to_cover_the_same_ground()
    {
        var capabilities = XDocument.Parse(WmtsService.BuildCapabilities(new FakeSource(timed: false, tileSize: 512), new Uri("http://localhost/")));
        var set = capabilities.Descendants(Wmts + "TileMatrixSet").Single(e => e.Parent!.Name == Wmts + "Contents");

        Assert.Null(set.Element(Wmts + "WellKnownScaleSet"));
        var zero = set.Elements(Wmts + "TileMatrix").First();
        Assert.Equal("512", zero.Element(Wmts + "TileWidth")!.Value);
        Assert.Equal(559082264.0287178 / 2, double.Parse(zero.Element(Wmts + "ScaleDenominator")!.Value, System.Globalization.CultureInfo.InvariantCulture), 1e-3);

        // Without time steps there's no dimension, and the template has no {Time}.
        Assert.Empty(capabilities.Descendants(Wmts + "Dimension"));
        Assert.DoesNotContain("{Time}", (string?)capabilities.Descendants(Wmts + "ResourceURL").Single().Attribute("template"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetTile_serves_the_xyz_tile_in_both_encodings()
    {
        var source = new FakeSource(timed: true);
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        async Task<string> Get(string path)
        {
            using var response = await http.GetAsync(new Uri(server.BaseUri, path), ct);
            response.EnsureSuccessStatusCode();
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            return await response.Content.ReadAsStringAsync(ct);
        }

        // KVP: TILECOL is x and TILEROW is y, rows from the north as in XYZ.
        Assert.Equal("2/1/3 night 2026-03-20T20:00:00Z", await Get(
            "wmts?SERVICE=WMTS&REQUEST=GetTile&VERSION=1.0.0&LAYER=s100&STYLE=night&FORMAT=image/png"
            + "&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=3&TILECOL=1&Time=2026-03-20T20:00:00Z"));

        // The default style and no time are the server's defaults.
        Assert.Equal("2/1/3 - -", await Get(
            "wmts?SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&STYLE=default&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=3&TILECOL=1"));

        // RESTful, with and without the Time segment.
        Assert.Equal("2/1/3 dusk 2026-03-20T18:00:00Z", await Get("wmts/1.0.0/s100/dusk/2026-03-20T18:00:00Z/WebMercatorQuad/2/3/1.png"));
        Assert.Equal("2/1/3 day -", await Get("wmts/1.0.0/s100/day/WebMercatorQuad/2/3/1.png"));

        // An empty tile is 204, as on the XYZ URLs; another extension isn't found.
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync(new Uri(server.BaseUri, "wmts/1.0.0/s100/day/WebMercatorQuad/2/0/0.png"), ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "wmts/1.0.0/s100/day/WebMercatorQuad/2/3/1.jpg"), ct)).StatusCode);
    }

    [Theory]
    [InlineData("REQUEST=GetTile", HttpStatusCode.BadRequest, "MissingParameterValue", "SERVICE")]
    [InlineData("SERVICE=WMS&REQUEST=GetMap", HttpStatusCode.BadRequest, "InvalidParameterValue", "SERVICE")]
    [InlineData("SERVICE=WMTS", HttpStatusCode.BadRequest, "MissingParameterValue", "REQUEST")]
    [InlineData("SERVICE=WMTS&REQUEST=GetFeatureInfo", HttpStatusCode.NotImplemented, "OperationNotSupported", "REQUEST")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=0&TILECOL=0", HttpStatusCode.BadRequest, "MissingParameterValue", "LAYER")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&LAYER=other&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=0&TILECOL=0", HttpStatusCode.BadRequest, "InvalidParameterValue", "LAYER")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&TILEMATRIXSET=GoogleCRS84Quad&TILEMATRIX=2&TILEROW=0&TILECOL=0", HttpStatusCode.BadRequest, "InvalidParameterValue", "TILEMATRIXSET")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&STYLE=sepia&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=0&TILECOL=0", HttpStatusCode.BadRequest, "InvalidParameterValue", "STYLE")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&FORMAT=image/jpeg&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=0&TILECOL=0", HttpStatusCode.BadRequest, "InvalidParameterValue", "FORMAT")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&TILEMATRIXSET=WebMercatorQuad&TILEROW=0&TILECOL=0", HttpStatusCode.BadRequest, "MissingParameterValue", "TILEMATRIX")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=4&TILECOL=0", HttpStatusCode.BadRequest, "TileOutOfRange", "TILEROW")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=0&TILECOL=-1", HttpStatusCode.BadRequest, "TileOutOfRange", "TILECOL")]
    [InlineData("SERVICE=WMTS&REQUEST=GetTile&LAYER=s100&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX=2&TILEROW=0&TILECOL=0&TIME=soon", HttpStatusCode.BadRequest, "InvalidParameterValue", "Time")]
    public async Task Bad_requests_get_an_ows_exception_report(string query, HttpStatusCode status, string code, string locator)
    {
        await using var server = await TileServer.StartAsync(new FakeSource(timed: true), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();

        using var response = await http.GetAsync(new Uri(server.BaseUri, "wmts?" + query), TestContext.Current.CancellationToken);

        Assert.Equal(status, response.StatusCode);
        var exception = XDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Root!.Element(Ows + "Exception")!;
        Assert.Equal(code, (string?)exception.Attribute("exceptionCode"));
        Assert.Equal(locator, (string?)exception.Attribute("locator"));
    }

    [Fact]
    public async Task A_built_tile_set_has_one_default_style()
    {
        var root = Path.Combine(Path.GetTempPath(), "tiles-wmts-" + Guid.NewGuid().ToString("N"));
        try
        {
            using (var sink = new XyzDirectoryTileSink(root, TileImageFormat.Png))
            {
                sink.Write(1, 1, 0, Encoding.ASCII.GetBytes("tile"));
                sink.Complete(new TileSetMetadata
                {
                    Name = "Built",
                    Description = "Built set",
                    Format = TileImageFormat.Png,
                    MinZoom = 1,
                    MaxZoom = 1,
                    Bounds = (0, 0, 10, 10),
                    TilePixelSize = 256,
                    Settings = new Dictionary<string, string> { ["palette"] = "day" },
                });
            }

            await using var server = await TileServer.StartAsync(TileSource.Open(root), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
            using var http = new HttpClient();
            var ct = TestContext.Current.CancellationToken;

            var capabilities = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
            var style = Assert.Single(capabilities.Descendants(Wmts + "Style"));
            Assert.Equal(WmtsService.DefaultStyle, style.Element(Ows + "Identifier")!.Value);

            Assert.Equal("tile", await http.GetStringAsync(new Uri(server.BaseUri, "wmts/1.0.0/s100/default/WebMercatorQuad/1/0/1.png"), ct));
            Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(new Uri(server.BaseUri, "wmts/1.0.0/s100/night/WebMercatorQuad/1/0/1.png"), ct)).StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
