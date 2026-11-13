using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the OGC WMS 1.3.0 service of <c>s100 tiles serve</c>: the
/// capabilities document, GetMap drawn from the tiles in EPSG:3857, EPSG:4326
/// and CRS:84 (and 1.1.1's axis order), GetFeatureInfo answered from the
/// datasets, and service exception reports.
/// </summary>
public sealed class TilesServeWmsTests
{
    private static readonly XNamespace Wms = "http://www.opengis.net/wms";
    private static readonly XNamespace Ogc = "http://www.opengis.net/ogc";

    private static readonly DateTime[] Steps =
    [
        new(2026, 3, 20, 18, 0, 0, DateTimeKind.Utc),
        new(2026, 3, 20, 19, 0, 0, DateTimeKind.Utc),
    ];

    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    /// <summary>
    /// Tiles of one solid colour per palette, drawn only in the eastern
    /// hemisphere, so the side, the palette and the resampling show in a map.
    /// </summary>
    private sealed class SolidSource : ITileSource
    {
        public static readonly Dictionary<string, SKColor> Colours = new()
        {
            ["day"] = new SKColor(200, 0, 0),
            ["dusk"] = new SKColor(0, 160, 0),
            ["night"] = new SKColor(0, 0, 180),
        };

        private readonly Dictionary<string, byte[]> _tiles = Colours.ToDictionary(c => c.Key, c => Png(c.Value));

        public string Path => "solid";

        public TileImageFormat Format => TileImageFormat.Png;

        public IReadOnlyList<string> Palettes => ["day", "dusk", "night"];

        public IReadOnlyList<DateTime> Times => Steps;

        public ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken) =>
            ValueTask.FromResult(x >= (1 << zoom) / 2 ? _tiles[palette ?? "dusk"] : null);

        public JsonObject ToTileJson(string? palette, DateTime? time = null) => new()
        {
            ["name"] = "Solid",
            ["description"] = "For tests",
            ["minzoom"] = 0,
            ["maxzoom"] = 4,
            ["bounds"] = new JsonArray(-10.0, -5.0, 10.0, 5.0),
            ["tileSize"] = 256,
            ["s100"] = new JsonObject { ["palette"] = palette ?? "dusk", ["timeStep"] = "1" },
        };

        private static byte[] Png(SKColor colour)
        {
            using var bitmap = new SKBitmap(256, 256);
            bitmap.Erase(colour);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            return data.ToArray();
        }
    }

    private static async Task<SKBitmap> GetMapAsync(HttpClient http, Uri baseUri, string query, string mediaType = "image/png")
    {
        using var response = await http.GetAsync(new Uri(baseUri, "wms?" + query), TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, System.Text.Encoding.UTF8.GetString(body));
        Assert.Equal(mediaType, response.Content.Headers.ContentType?.MediaType);
        return SKBitmap.Decode(body);
    }

    private static void AssertNear(SKColor expected, SKColor actual)
    {
        Assert.InRange(Math.Abs(expected.Red - actual.Red), 0, 3);
        Assert.InRange(Math.Abs(expected.Green - actual.Green), 0, 3);
        Assert.InRange(Math.Abs(expected.Blue - actual.Blue), 0, 3);
        Assert.Equal(expected.Alpha, actual.Alpha);
    }

    [Fact]
    public async Task Capabilities_describe_the_layer_crss_styles_and_time()
    {
        await using var server = await TileServer.StartAsync(new SolidSource(), IPAddress.Loopback, 0, "tok", cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        using var response = await http.GetAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetCapabilities"), ct);
        response.EnsureSuccessStatusCode();
        Assert.Equal("text/xml", response.Content.Headers.ContentType?.MediaType);
        var capabilities = XDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        Assert.Equal("1.3.0", (string?)capabilities.Root!.Attribute("version"));

        var layer = capabilities.Descendants(Wms + "Layer").Single();
        Assert.Equal("s100", layer.Element(Wms + "Name")!.Value);
        Assert.Equal(["EPSG:3857", "EPSG:4326", "CRS:84"], layer.Elements(Wms + "CRS").Select(c => c.Value));

        // A tile set without datasets behind it can't be queried.
        Assert.Equal("0", (string?)layer.Attribute("queryable"));
        Assert.Empty(capabilities.Descendants(Wms + "GetFeatureInfo"));

        // The default palette is the first style, as WMS defaults to the first.
        Assert.Equal(["dusk", "day", "night"], layer.Elements(Wms + "Style").Select(s => s.Element(Wms + "Name")!.Value));

        // EPSG:4326 boxes are latitude first in 1.3.0.
        var box = layer.Elements(Wms + "BoundingBox").Single(b => (string?)b.Attribute("CRS") == "EPSG:4326");
        Assert.Equal(("-5", "-10", "5", "10"), ((string?)box.Attribute("minx"), (string?)box.Attribute("miny"), (string?)box.Attribute("maxx"), (string?)box.Attribute("maxy")));

        var dimension = layer.Element(Wms + "Dimension")!;
        Assert.Equal("time", (string?)dimension.Attribute("name"));
        Assert.Equal("2026-03-20T19:00:00Z", (string?)dimension.Attribute("default"));
        Assert.Equal("2026-03-20T18:00:00Z,2026-03-20T19:00:00Z", dimension.Value);

        // The endpoint is absolute, under the token.
        Assert.All(
            capabilities.Descendants(Wms + "Get").Select(g => g.Element(Wms + "OnlineResource")!),
            r => Assert.Equal($"http://127.0.0.1:{server.Port}/tok/wms?", (string?)r.Attribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href")));
    }

    [Fact]
    public async Task GetMap_draws_the_tiles_in_web_mercator()
    {
        await using var server = await TileServer.StartAsync(new SolidSource(), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        const string e = "20037508.342789244";

        // The world at 512 × 512 (zoom 1): west empty, east in the palette asked for.
        using var map = await GetMapAsync(http, server.BaseUri,
            $"SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=s100&STYLES=night&CRS=EPSG:3857&BBOX=-{e},-{e},{e},{e}&WIDTH=512&HEIGHT=512&FORMAT=image/png&TRANSPARENT=TRUE");
        Assert.Equal((512, 512), (map.Width, map.Height));
        Assert.Equal(0, map.GetPixel(100, 256).Alpha);
        AssertNear(SolidSource.Colours["night"], map.GetPixel(400, 256));

        // Opaque by default, on white or BGCOLOR; the default style is the server's palette.
        using var opaque = await GetMapAsync(http, server.BaseUri,
            $"SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=s100&STYLES=&CRS=EPSG:900913&BBOX=-{e},-{e},{e},{e}&WIDTH=512&HEIGHT=512&FORMAT=image/png&BGCOLOR=0x102030");
        AssertNear(new SKColor(0x10, 0x20, 0x30), opaque.GetPixel(100, 256));
        AssertNear(SolidSource.Colours["dusk"], opaque.GetPixel(400, 256));

        using var jpeg = await GetMapAsync(http, server.BaseUri,
            $"REQUEST=GetMap&LAYERS=s100&STYLES=day&CRS=EPSG:3857&BBOX=0,0,{e},{e}&WIDTH=256&HEIGHT=256&FORMAT=image/jpeg", "image/jpeg");
        Assert.InRange(jpeg.GetPixel(128, 128).Red, 180, 220);
    }

    [Theory]
    [InlineData("VERSION=1.3.0&CRS=EPSG:4326&BBOX=-60,-90,60,90")]
    [InlineData("VERSION=1.3.0&CRS=CRS:84&BBOX=-90,-60,90,60")]
    [InlineData("VERSION=1.1.1&SRS=EPSG:4326&BBOX=-90,-60,90,60")]
    public async Task GetMap_draws_plate_carree_in_each_axis_order(string crsAndBox)
    {
        await using var server = await TileServer.StartAsync(new SolidSource(), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();

        using var map = await GetMapAsync(http, server.BaseUri,
            $"SERVICE=WMS&REQUEST=GetMap&LAYERS=s100&STYLES=day&{crsAndBox}&WIDTH=200&HEIGHT=120&FORMAT=image/png&TRANSPARENT=TRUE");

        // Longitudes -90…90: the eastern half is drawn, top to bottom.
        Assert.Equal(0, map.GetPixel(50, 60).Alpha);
        foreach (int row in new[] { 1, 60, 118 })
            AssertNear(SolidSource.Colours["day"], map.GetPixel(150, row));
    }

    [Fact]
    public void The_nearest_zoom_is_drawn_and_none_below_the_minimum()
    {
        double zoom12 = 40075016.68557849 / 256 / 4096;

        Assert.Equal(12, WmsMapComposer.ZoomFor(zoom12, 256, 10, 16));
        Assert.Equal(12, WmsMapComposer.ZoomFor(zoom12 * 1.3, 256, 10, 16));
        Assert.Equal(11, WmsMapComposer.ZoomFor(zoom12 * 1.5, 256, 10, 16));
        Assert.Equal(16, WmsMapComposer.ZoomFor(zoom12 / 1000, 256, 10, 16));
        Assert.Null(WmsMapComposer.ZoomFor(zoom12 * 8, 256, 10, 16));

        // 512-pixel tiles cover the same ground at one zoom lower.
        Assert.Equal(11, WmsMapComposer.ZoomFor(zoom12, 512, 10, 16));
    }

    [Theory]
    [InlineData("SERVICE=WMS", null)]
    [InlineData("SERVICE=WFS&REQUEST=GetFeature", null)]
    [InlineData("SERVICE=WMS&REQUEST=DescribeLayer", "OperationNotSupported")]
    [InlineData("REQUEST=GetMap&LAYERS=other&CRS=EPSG:3857&BBOX=0,0,1,1&WIDTH=1&HEIGHT=1&FORMAT=image/png", "LayerNotDefined")]
    [InlineData("REQUEST=GetMap&LAYERS=s100&STYLES=sepia&CRS=EPSG:3857&BBOX=0,0,1,1&WIDTH=1&HEIGHT=1&FORMAT=image/png", "StyleNotDefined")]
    [InlineData("REQUEST=GetMap&LAYERS=s100&CRS=EPSG:27700&BBOX=0,0,1,1&WIDTH=1&HEIGHT=1&FORMAT=image/png", "InvalidCRS")]
    [InlineData("VERSION=1.1.1&REQUEST=GetMap&LAYERS=s100&SRS=EPSG:27700&BBOX=0,0,1,1&WIDTH=1&HEIGHT=1&FORMAT=image/png", "InvalidSRS")]
    [InlineData("REQUEST=GetMap&LAYERS=s100&CRS=EPSG:3857&BBOX=0,0,1,1&WIDTH=1&HEIGHT=1&FORMAT=image/tiff", "InvalidFormat")]
    [InlineData("REQUEST=GetMap&LAYERS=s100&CRS=EPSG:3857&BBOX=1,0,0,1&WIDTH=1&HEIGHT=1&FORMAT=image/png", null)]
    [InlineData("REQUEST=GetMap&LAYERS=s100&CRS=EPSG:3857&BBOX=0,0,1&WIDTH=1&HEIGHT=1&FORMAT=image/png", null)]
    [InlineData("REQUEST=GetMap&LAYERS=s100&CRS=EPSG:3857&BBOX=0,0,1,1&WIDTH=5000&HEIGHT=1&FORMAT=image/png", null)]
    [InlineData("REQUEST=GetMap&LAYERS=s100&CRS=EPSG:3857&BBOX=0,0,1,1&WIDTH=1&HEIGHT=1&FORMAT=image/png&TIME=soon", "InvalidDimensionValue")]
    [InlineData("REQUEST=GetFeatureInfo&LAYERS=s100&QUERY_LAYERS=s100&CRS=EPSG:3857&BBOX=0,0,1,1&WIDTH=1&HEIGHT=1&I=0&J=0&INFO_FORMAT=text/plain", "LayerNotQueryable")]
    public async Task Bad_requests_get_a_service_exception_report(string query, string? code)
    {
        await using var server = await TileServer.StartAsync(new SolidSource(), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();

        using var response = await http.GetAsync(new Uri(server.BaseUri, "wms?" + query), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var exception = XDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken))
            .Root!.Element(Ogc + "ServiceException")!;
        Assert.Equal(code, (string?)exception.Attribute("code"));
        Assert.NotEmpty(exception.Value);
    }

    [Fact]
    public async Task GetFeatureInfo_reports_the_features_at_the_clicked_pixel()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var settings = new TilesServeCommand.Settings { Input = S57, MinZoom = 13, MaxZoom = 13 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var rendered));
        using var source = rendered!;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        var capabilities = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetCapabilities"), ct));
        Assert.Equal("1", (string?)capabilities.Descendants(Wms + "Layer").Single().Attribute("queryable"));
        Assert.Equal(WmsService.InfoFormats, capabilities.Descendants(Wms + "GetFeatureInfo").Single().Elements(Wms + "Format").Select(f => f.Value));

        // A 101 × 101 map centred on the cell, clicked in its middle.
        var (west, south, east, north) = source.Layout.Bounds;
        double latitude = (south + north) / 2, longitude = (west + east) / 2, half = 0.01;
        var box = string.Create(CultureInfo.InvariantCulture, $"{latitude - half},{longitude - half},{latitude + half},{longitude + half}");
        var query = $"SERVICE=WMS&VERSION=1.3.0&REQUEST=GetFeatureInfo&LAYERS=s100&QUERY_LAYERS=s100&STYLES=&CRS=EPSG:4326&BBOX={box}&WIDTH=101&HEIGHT=101&I=50&J=50&FEATURE_COUNT=10";

        using (var response = await http.GetAsync(new Uri(server.BaseUri, "wms?" + query + "&INFO_FORMAT=application/json"), ct))
        {
            response.EnsureSuccessStatusCode();
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
            Assert.Equal("FeatureCollection", (string?)json["type"]);
            var features = json["features"]!.AsArray();
            Assert.NotEmpty(features);
            Assert.InRange(features.Count(f => (string?)f!["properties"]!["kind"] == "feature"), 1, 10);

            // Every feature sits at the clicked point: the centre of the middle pixel.
            var first = features[0]!;
            Assert.Equal("Point", (string?)first["geometry"]!["type"]);
            Assert.Equal(longitude, (double)first["geometry"]!["coordinates"]![0]!, 6);
            Assert.Equal(latitude, (double)first["geometry"]!["coordinates"]![1]!, 6);
            Assert.False(string.IsNullOrEmpty((string?)first["properties"]!["spec"]));
        }

        var text = await http.GetStringAsync(new Uri(server.BaseUri, "wms?" + query + "&INFO_FORMAT=text/plain"), ct);
        Assert.StartsWith("Pick lat", text, StringComparison.Ordinal);

        var html = await http.GetStringAsync(new Uri(server.BaseUri, "wms?" + query + "&INFO_FORMAT=text/html"), ct);
        Assert.Contains("<h3>", html, StringComparison.Ordinal);

        // A pixel off the map, and an unknown format, are refused.
        using var offMap = await http.GetAsync(new Uri(server.BaseUri, "wms?" + query.Replace("I=50", "I=101", StringComparison.Ordinal) + "&INFO_FORMAT=text/plain"), ct);
        Assert.Equal(HttpStatusCode.BadRequest, offMap.StatusCode);
        Assert.Contains("InvalidPoint", await offMap.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
        using var gml = await http.GetAsync(new Uri(server.BaseUri, "wms?" + query + "&INFO_FORMAT=application/vnd.ogc.gml"), ct);
        Assert.Contains("InvalidFormat", await gml.Content.ReadAsStringAsync(ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetFeatureInfo_finds_features_in_a_folder_of_datasets()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var folder = Directory.CreateTempSubdirectory("s100-wms-folder-");
        try
        {
            var nested = Directory.CreateDirectory(Path.Combine(folder.FullName, "ENC", "US5MA1BO"));
            File.Copy(S57, Path.Combine(nested.FullName, "US5MA1BO.000"));
            var settings = new TilesServeCommand.Settings { Input = folder.FullName, MinZoom = 13, MaxZoom = 13 };
            Assert.True(settings.Validate().Successful);
            Assert.True(settings.IsFolder);
            Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var rendered));
            using var source = rendered!;
            var (west, south, east, north) = source.Layout.Bounds;

            var result = await source.FeatureQuery!.QueryAsync(
                (south + north) / 2, (west + east) / 2, 50, 10, time: null, TestContext.Current.CancellationToken);

            Assert.NotEmpty(result.Features);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    [Fact]
    public void Described_attributes_show_as_name_and_value_rows()
    {
        using var described = JsonDocument.Parse("""
            {
              "recordHeader": { "recordName": 100 },
              "geometry": { "primitive": "Point", "depths": [12.5], "depthUnit": "m" },
              "attributes": [
                { "code": 58, "acronym": "surfaceCharacteristics", "index": 1, "value": "" },
                { "code": 59, "acronym": "natureOfSurface", "index": 1, "value": "4" }
              ]
            }
            """);

        Assert.Equal(
            [("surfaceCharacteristics", ""), ("natureOfSurface", "4"), ("depth", "12.5 m")],
            WmsService.AttributeRows(described.RootElement));

        using var other = JsonDocument.Parse("""{ "name": "x" }""");
        Assert.Null(WmsService.AttributeRows(other.RootElement));
    }
}
