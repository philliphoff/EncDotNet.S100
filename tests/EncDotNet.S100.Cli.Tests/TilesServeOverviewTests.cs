using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using SkiaSharp;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for <c>tiles serve --overview</c> (issue #1503): below the datasets'
/// minimum zoom, XYZ and WMTS tiles and WMS GetMap draw each dataset's
/// coverage outline and name instead of nothing, and the WMS and WMTS
/// capabilities advertise the layer down to zoom 0.
/// </summary>
public sealed class TilesServeOverviewTests
{
    private static readonly XNamespace Wms = "http://www.opengis.net/wms";
    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";

    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    // Aids to navigation in Chesapeake Bay, far from the S-57 cell (Boston).
    private static string S125 => Path.Combine(AppContext.BaseDirectory, "TestData", "S125", "aton_point.gml");

    [Fact]
    public void Every_tile_source_member_is_forwarded()
    {
        // A member ITileSource implements by default would otherwise answer
        // for the chart source behind the overview (Revision, HasDatasets, …).
        var map = typeof(OverviewTileSource).GetInterfaceMap(typeof(ITileSource));
        var missing = map.TargetMethods
            .Where(m => m.DeclaringType != typeof(OverviewTileSource))
            .Select(m => m.Name)
            .ToArray();
        Assert.Empty(missing);
        Assert.Equal(typeof(ITileSource).GetMethods(BindingFlags.Instance | BindingFlags.Public).Length, map.TargetMethods.Length);
    }

    [Fact]
    public async Task Below_the_minimum_zoom_tiles_draw_the_coverage()
    {
        var chart = new ChartSource();
        await using var overview = new OverviewTileSource(chart);
        await using var server = await TileServer.StartAsync(overview, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        await using var plain = await TileServer.StartAsync(new ChartSource(), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        var (x, y) = TileAt(3, 11, 50.5);
        var path = $"3/{x}/{y}.png";

        // Without --overview: nothing, as today.
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync(new Uri(plain.BaseUri, path), ct)).StatusCode);

        using (var response = await http.GetAsync(new Uri(server.BaseUri, path), ct))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
            using var tile = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync(ct));
            Assert.Equal(256, tile.Width);
            Assert.True(CountOutline(tile) > 0, "The coverage outline is drawn.");
        }

        // Night is dimmer than day.
        using (var night = SKBitmap.Decode(await http.GetByteArrayAsync(new Uri(server.BaseUri, "night/" + path), ct)))
            Assert.Equal(0, CountOutline(night));

        // Far from any dataset: still nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await http.GetAsync(new Uri(server.BaseUri, "3/0/7.png"), ct)).StatusCode);

        // A dataset too small to outline is still marked.
        var (tx, ty) = TileAt(2, -69.995, 41.005);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(new Uri(server.BaseUri, $"2/{tx}/{ty}.png"), ct)).StatusCode);

        // At the chart's zoom levels, the chart.
        var (cx, cy) = TileAt(10, 11, 50.5);
        Assert.Equal(ChartSource.Tile, await http.GetByteArrayAsync(new Uri(server.BaseUri, $"10/{cx}/{cy}.png"), ct));
        Assert.Equal(1, chart.Reads);
    }

    [Fact]
    public async Task TileJson_and_capabilities_reach_zoom_zero()
    {
        await using var overview = new OverviewTileSource(new ChartSource());
        await using var server = await TileServer.StartAsync(overview, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        await using var plain = await TileServer.StartAsync(new ChartSource(), IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        var tileJson = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Equal(0, (int)tileJson["minzoom"]!);
        Assert.Equal(12, (int)tileJson["maxzoom"]!);
        Assert.Equal(10, (int)tileJson["s100"]!["chartMinzoom"]!);
        Assert.Equal(0, (int)tileJson["s100"]!["overview"]!["minzoom"]!);
        Assert.Equal(9, (int)tileJson["s100"]!["overview"]!["maxzoom"]!);

        double MaxScale(XDocument document) =>
            double.Parse(document.Descendants(Wms + "MaxScaleDenominator").Single().Value, CultureInfo.InvariantCulture);
        var wms = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "wms?SERVICE=WMS&REQUEST=GetCapabilities"), ct));
        var plainWms = XDocument.Parse(await http.GetStringAsync(new Uri(plain.BaseUri, "wms?SERVICE=WMS&REQUEST=GetCapabilities"), ct));
        Assert.True(MaxScale(wms) > 500_000_000, $"MaxScaleDenominator {MaxScale(wms)} reaches zoom 0.");
        Assert.True(MaxScale(plainWms) < 1_000_000);

        static IEnumerable<string> Limits(XDocument document) =>
            document.Descendants(Wmts + "TileMatrixLimits").Select(l => l.Element(Wmts + "TileMatrix")!.Value);
        var wmts = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
        Assert.Equal([.. Enumerable.Range(0, 13).Select(z => z.ToString(CultureInfo.InvariantCulture))], Limits(wmts));
        Assert.Equal("10", Limits(XDocument.Parse(await http.GetStringAsync(new Uri(plain.BaseUri, WmtsService.CapabilitiesPath), ct))).First());

        // A WMTS tile below the chart's zoom levels draws the overview too.
        var (x, y) = TileAt(4, 11, 50.5);
        using var response = await http.GetAsync(new Uri(server.BaseUri, $"wmts/1.0.0/s100/day/WebMercatorQuad/4/{y}/{x}.png"), ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("&DPI=192")]
    public async Task Wms_GetMap_draws_the_overview_when_zoomed_out(string dpi)
    {
        await using var overview = new OverviewTileSource(new ChartSource());
        await using var server = await TileServer.StartAsync(overview, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        // Europe, about zoom 4.
        using var response = await http.GetAsync(new Uri(server.BaseUri,
            $"wms?SERVICE=WMS&VERSION=1.3.0&REQUEST=GetMap&LAYERS=s100&STYLES=&CRS=CRS:84&BBOX=-10,35,30,60&WIDTH=400&HEIGHT=300&FORMAT=image/png&TRANSPARENT=TRUE{dpi}"), ct);
        var body = await response.Content.ReadAsByteArrayAsync(ct);
        Assert.True(response.IsSuccessStatusCode, System.Text.Encoding.UTF8.GetString(body));
        using var map = SKBitmap.Decode(body);
        Assert.True(CountOutline(map) > 0, "The coverage outline is drawn.");
    }

    [Fact]
    public void Names_that_would_overlap_are_left_out()
    {
        var data = OverviewData.Build(new ChartSource(), "r");
        Assert.Equal(10, data.ChartMinZoom);
        Assert.Equal("day", data.DefaultPalette);
        Assert.Equal(["AREA1", "AREA1-COPY", "TINY"], data.Datasets.Select(d => d.Name));

        // AREA1-COPY lies on AREA1, so only the first is named.
        var labels = data.LabelsAt(3);
        Assert.Equal(["AREA1", "TINY"], labels.Select(l => l.Text));
        Assert.Same(labels, data.LabelsAt(3));
    }

    [Fact]
    public void Overview_applies_only_to_datasets()
    {
        var folder = Directory.CreateTempSubdirectory("s100-overview-");
        try
        {
            File.WriteAllText(Path.Combine(folder.FullName, XyzDirectoryTileSink.TileJsonFileName), "{}");
            var result = new TilesServeCommand.Settings { Input = folder.FullName, Overview = true }.Validate();
            Assert.False(result.Successful);
            Assert.Contains("--overview", result.Message, StringComparison.Ordinal);
        }
        finally
        {
            folder.Delete(recursive: true);
        }

        Assert.Throws<ArgumentException>(() => new OverviewTileSource(new ChartSource(datasets: false)));
    }

    [Fact]
    public async Task A_chart_cell_is_outlined_below_its_minimum_zoom()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var settings = new TilesServeCommand.Settings { Input = S57, Overview = true, MinZoom = 12, MaxZoom = 13, Parallel = 2 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var rendered));
        await using var overview = new OverviewTileSource(rendered!);
        Assert.Equal(12, overview.ChartMinZoom);

        var (west, south, east, north) = rendered!.Layout.Bounds;
        double longitude = (west + east) / 2, latitude = (south + north) / 2;
        foreach (int zoom in new[] { 5, 9, 11 })
        {
            var (x, y) = TileAt(zoom, longitude, latitude);
            var data = await overview.ReadAsync(zoom, x, y, palette: null, time: null, TestContext.Current.CancellationToken);
            Assert.NotNull(data);
            using var tile = SKBitmap.Decode(data);
            Assert.True(CountOutline(tile) > 0, $"Zoom {zoom} shows the cell.");
        }

        Assert.Null(await rendered.ReadAsync(11, TileAt(11, longitude, latitude).X, TileAt(11, longitude, latitude).Y, null, null, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_overview_follows_a_change_to_the_datasets()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var root = Directory.CreateTempSubdirectory("s100-overview-manage-");
        try
        {
            var cell = System.IO.Path.Combine(root.FullName, System.IO.Path.GetFileName(S57));
            var atons = System.IO.Path.Combine(root.FullName, System.IO.Path.GetFileName(S125));
            File.Copy(S57, cell);
            File.Copy(S125, atons);
            var settings = new TilesServeCommand.Settings { Input = cell, MinZoom = 14, MaxZoom = 14, Parallel = 2, ManageDatasets = true, Overview = true };
            Assert.True(settings.Validate().Successful);

            // As tiles serve --manage-datasets --refresh 0 --overview sets it up.
            var managed = new ManagedDatasetTiles(64L * 1024 * 1024);
            var styles = new MarinerStyleRegistry();
            ServedDatasets? datasets = null;
            DatasetChanges Changes() => datasets?.Changes ?? DatasetChanges.None;
            var fingerprint = TileRenderSession.Fingerprint(settings);
            Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var initial, styles, managed: managed));
            var refreshing = new RefreshingTileSource(
                initial!,
                fingerprint,
                () => TileRenderSession.Fingerprint(settings, Changes()),
                () => TilesServeCommand.OpenRendered(settings, out var reopened, styles, Changes(), managed) == 0 ? reopened : null,
                Timeout.InfiniteTimeSpan,
                _ => { });
            datasets = new ServedDatasets(
                refreshing,
                changes => TilesServeCommand.OpenRendered(settings, out var changed, styles, changes, managed) == 0 ? changed : null,
                changes => TileRenderSession.Fingerprint(settings, changes));
            await using var overview = new OverviewTileSource(refreshing);
            var ct = TestContext.Current.CancellationToken;
            await using var server = await TileServer.StartAsync(overview, IPAddress.Loopback, 0, "tok", cancellationToken: ct, datasets: datasets);
            using var http = new HttpClient();

            var (cellX, cellY) = TileAt(6, -70.31, 41.29);
            var (atonX, atonY) = TileAt(6, -76.2, 36.98);
            async Task<HttpStatusCode> StatusAsync(int x, int y)
            {
                using var response = await http.GetAsync(new Uri(server.BaseUri, $"6/{x}/{y}.png"), ct);
                return response.StatusCode;
            }

            Assert.Equal(HttpStatusCode.OK, await StatusAsync(cellX, cellY));
            Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(atonX, atonY));

            using (var add = await http.PostAsJsonAsync(new Uri(server.BaseUri, ServedDatasets.Path), new { paths = new[] { atons } }, ct))
                Assert.Equal(HttpStatusCode.OK, add.StatusCode);
            Assert.Equal(HttpStatusCode.OK, await StatusAsync(atonX, atonY));
            Assert.Equal(HttpStatusCode.OK, await StatusAsync(cellX, cellY));

            Assert.True((await datasets.RemoveAsync([cell])).Succeeded);
            Assert.Equal(HttpStatusCode.NoContent, await StatusAsync(cellX, cellY));
            Assert.Equal(HttpStatusCode.OK, await StatusAsync(atonX, atonY));
        }
        finally
        {
            try
            {
                root.Delete(recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>The XYZ tile holding a point.</summary>
    private static (int X, int Y) TileAt(int zoom, double longitude, double latitude)
    {
        int n = 1 << zoom;
        double phi = latitude * Math.PI / 180;
        int x = (int)Math.Floor((longitude + 180) / 360 * n);
        int y = (int)Math.Floor((1 - (Math.Log(Math.Tan(phi) + (1 / Math.Cos(phi))) / Math.PI)) / 2 * n);
        return (x, y);
    }

    /// <summary>The pixels in the day palette's outline colour.</summary>
    private static int CountOutline(SKBitmap bitmap)
    {
        int count = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            for (int x = 0; x < bitmap.Width; x++)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Alpha > 200 && c.Red > 140 && c.Blue > 140 && c.Green < 40)
                    count++;
            }
        }

        return count;
    }

    /// <summary>A chart from zoom 10 to 12 over three datasets, its tiles all one sentinel.</summary>
    private sealed class ChartSource(bool datasets = true) : ITileSource
    {
        public static readonly byte[] Tile = [1, 2, 3, 4];

        private int _reads;

        public int Reads => Volatile.Read(ref _reads);

        public string Path => "chart";

        public TileImageFormat Format => TileImageFormat.Png;

        public IReadOnlyList<string> Palettes => RenderedTileSource.AllPalettes;

        public bool HasDatasets => datasets;

        public string? Revision => "r1";

        public bool CanRenderViews => true;

        public JsonObject? ToDatasetsJson() => new()
        {
            ["datasets"] = new JsonArray(
                Dataset("AREA1", 10, 50, 12, 51),
                Dataset("AREA1-COPY", 10.2, 50.2, 11.8, 50.8),
                Dataset("TINY", -70, 41, -69.99, 41.01)),
        };

        public ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken)
        {
            if (zoom < 10)
                return ValueTask.FromResult<byte[]?>(null);
            Interlocked.Increment(ref _reads);
            return ValueTask.FromResult<byte[]?>(Tile);
        }

        public ValueTask<SKBitmap?> RenderViewAsync(
            int zoom, double minX, double minY, double maxX, double maxY, int width, int height,
            string? palette, DateTime? time, CancellationToken cancellationToken) =>
            ValueTask.FromResult<SKBitmap?>(zoom < 10 ? null : new SKBitmap(width, height));

        public JsonObject ToTileJson(string? palette, DateTime? time = null) => new()
        {
            ["tilejson"] = "3.0.0",
            ["name"] = "chart",
            ["minzoom"] = 10,
            ["maxzoom"] = 12,
            ["bounds"] = new JsonArray(-70.0, 41.0, 12.0, 51.0),
            ["s100"] = new JsonObject { ["palette"] = "day" },
        };

        private static JsonObject Dataset(string id, double west, double south, double east, double north) => new()
        {
            ["id"] = id,
            ["file"] = id + ".000",
            ["coverage"] = new JsonObject
            {
                ["type"] = "MultiPolygon",
                ["coordinates"] = new JsonArray(new JsonArray(new JsonArray(
                    new JsonArray(west, south), new JsonArray(east, south), new JsonArray(east, north), new JsonArray(west, north), new JsonArray(west, south)))),
            },
        };
    }
}
