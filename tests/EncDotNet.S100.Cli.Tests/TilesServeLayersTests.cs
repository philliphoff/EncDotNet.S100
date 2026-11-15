using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for several WMTS layers in one <c>tiles serve</c> (issue #1495):
/// <c>--layers-by product</c>, layers named by clients with
/// <c>--manage-datasets</c>, each layer's time steps and extent in the
/// capabilities, and each layer's tiles drawing its datasets alone.
/// </summary>
public sealed class TilesServeLayersTests : IDisposable
{
    private static readonly XNamespace Wmts = "http://www.opengis.net/wmts/1.0";
    private static readonly XNamespace Ows = "http://www.opengis.net/ows/1.1";

    // Off Nantucket.
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    // Surface currents in Delaware Bay, far from the S-57 cell.
    private static string S111 => Path.Combine(AppContext.BaseDirectory, "TestData", "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

    // Aids to navigation in Chesapeake Bay.
    private static string S125 => Path.Combine(AppContext.BaseDirectory, "TestData", "S125", "aton_point.gml");

    // The chart draws at zoom 14; the currents at 11, in fewer tiles.
    private const int ChartZoom = 14;
    private const int Zoom = 11;

    private readonly string _root = Path.Combine(Path.GetTempPath(), "tiles-serve-layers-" + Guid.NewGuid().ToString("N"));

    public TilesServeLayersTests() => Directory.CreateDirectory(_root);

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

    private static TilesServeCommand.Settings ByProduct(string? layersBy = TileLayers.Product) => new()
    {
        Layers = [S57, S111],
        LayersBy = layersBy,
        MinZoom = Zoom,
        MaxZoom = ChartZoom,
        Metatile = 1,
        Parallel = 2,
    };

    private static RenderedTileSource OpenRendered(TilesServeCommand.Settings settings)
    {
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    /// <summary>The tile of <paramref name="zoom"/> holding a point.</summary>
    private static (int X, int Y) TileAt(int zoom, double longitude, double latitude)
    {
        var (x, y) = WebMercator.FromLonLat(longitude, latitude);
        double size = XyzTileGrid.TileWorldSize(zoom);
        return ((int)Math.Floor((x + XyzTileGrid.Extent) / size), (int)Math.Floor((XyzTileGrid.Extent - y) / size));
    }

    /// <summary>A tile of a layer that draws something at <paramref name="time"/>, searched for within the layer's bounds.</summary>
    private static async Task<(int X, int Y)> DrawnTileAsync(ITileSource layer, int zoom, DateTime? time)
    {
        var bounds = layer.ToTileJson(null)["bounds"]!.AsArray().Select(b => (double)b!).ToArray();
        var (west, north) = TileAt(zoom, bounds[0], bounds[3]);
        var (east, south) = TileAt(zoom, bounds[2], bounds[1]);
        for (int y = north; y <= south; y++)
        {
            for (int x = west; x <= east; x++)
            {
                if (await layer.ReadAsync(zoom, x, y, null, time, TestContext.Current.CancellationToken) is not null)
                    return (x, y);
            }
        }

        throw new InvalidOperationException("No tile of the layer drew anything.");
    }

    [Theory]
    [InlineData("S-57", "chart")]
    [InlineData("S-101", "chart")]
    [InlineData("S-102", "chart")]
    [InlineData("S-104", "S104")]
    [InlineData("S-111", "S111")]
    [InlineData("s-124", "S124")]
    public void Datasets_are_grouped_by_product(string spec, string layer) =>
        Assert.Equal(layer, TileLayers.ForProduct(spec));

    [Theory]
    [InlineData("chart", true)]
    [InlineData("Tides_2.v1", true)]
    [InlineData("s100", false)]
    [InlineData("S100", false)]
    [InlineData("", false)]
    [InlineData("-x", false)]
    [InlineData("a/b", false)]
    [InlineData("a b", false)]
    public void Layer_identifiers_are_url_safe_and_not_the_layer_of_every_dataset(string id, bool valid) =>
        Assert.Equal(valid, TileLayers.IsValidId(id));

    [Fact]
    public void Layers_named_by_clients_win_over_the_grouping_and_go_with_the_dataset()
    {
        var a = Path.GetFullPath(Path.Combine(_root, "a.000"));
        var b = Path.GetFullPath(Path.Combine(_root, "b.h5"));
        var changes = DatasetChanges.None.WithAdded(b).WithLayer(b, "tides");
        Assert.False(changes.IsEmpty);
        Assert.Equal("tides", changes.LayerOf(b));

        Assert.Equal<string?>(["chart", "tides"], TileLayers.Assign([a, b], ["S-57", "S-104"], TileLayers.Product, changes));
        Assert.Equal<string?>([null, "tides"], TileLayers.Assign([a, b], ["S-57", "S-104"], null, changes));
        Assert.Equal<string?>([null, null], TileLayers.Assign([a, b], ["S-57", "S-104"], TileLayers.None, null));

        // Removing the dataset forgets its layer; putting it back in the default layer undoes the change.
        Assert.True(changes.WithRemoved(b).IsEmpty);
        Assert.Same(changes, changes.WithLayer(b, "tides"));
        Assert.True(changes.WithLayer(b, null).WithRemoved(b).IsEmpty);
        Assert.True(changes.SameAs(DatasetChanges.None.WithAdded(b).WithLayer(b, "tides")));
        Assert.False(changes.SameAs(changes.WithLayer(b, "other")));
    }

    [Theory]
    [InlineData("{\"paths\": [\"/a\"]}", true, null)]
    [InlineData("{\"paths\": [\"/a\"], \"layer\": null}", true, null)]
    [InlineData("{\"paths\": [\"/a\"], \"layer\": \"tides\"}", true, "tides")]
    [InlineData("{\"paths\": [\"/a\"], \"layer\": 1}", false, null)]
    public void Request_bodies_may_name_a_layer(string body, bool parsed, string? layer)
    {
        Assert.Equal(parsed, ServedDatasets.TryParseLayer(Encoding.UTF8.GetBytes(body), out var named));
        Assert.Equal(layer, named);
    }

    [Fact]
    public void Without_layers_by_only_the_layer_of_every_dataset_is_served()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S111), "S-57 or S-111 fixture not present.");
        using var source = OpenRendered(ByProduct(layersBy: null));

        Assert.Empty(source.Layers);
        Assert.Null(source.Layer(TileLayers.Chart));
        var capabilities = XDocument.Parse(WmtsService.BuildCapabilities(source, new Uri("http://localhost/")));
        Assert.Equal([WmtsService.LayerIdentifier], capabilities.Descendants(Wmts + "Layer").Select(l => l.Element(Ows + "Identifier")!.Value));
        Assert.All(source.ToDatasetsJson()["datasets"]!.AsArray(), d => Assert.False(d!.AsObject().ContainsKey("layer")));
    }

    [Fact]
    public void Layers_by_is_checked()
    {
        Assert.False(ByProduct("colour").Validate().Successful);
        Assert.True(ByProduct("PRODUCT").Validate().Successful);
    }

    [Fact]
    public async Task Capabilities_list_each_layer_with_its_own_time_steps_and_extent()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S111), "S-57 or S-111 fixture not present.");
        using var source = OpenRendered(ByProduct());
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "tok", cancellationToken: ct);
        using var http = new HttpClient();

        var capabilities = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
        var layers = capabilities.Descendants(Wmts + "Layer").ToDictionary(l => l.Element(Ows + "Identifier")!.Value);
        Assert.Equal([WmtsService.LayerIdentifier, TileLayers.Chart, "S111"], layers.Keys);

        string[] Times(string layer) =>
            [.. layers[layer].Elements(Wmts + "Dimension").Elements(Wmts + "Value").Select(v => v.Value)];
        double[] Box(string layer) =>
        [
            .. layers[layer].Element(Ows + "WGS84BoundingBox")!.Element(Ows + "LowerCorner")!.Value.Split(' ').Select(double.Parse),
            .. layers[layer].Element(Ows + "WGS84BoundingBox")!.Element(Ows + "UpperCorner")!.Value.Split(' ').Select(double.Parse),
        ];

        // The chart doesn't vary with time; the currents have their own steps.
        var currents = source.Layer("S111")!.Times;
        Assert.NotEmpty(currents);
        Assert.Empty(Times(TileLayers.Chart));
        Assert.Equal(currents.Select(TileRenderSession.FormatTime), Times("S111"));
        Assert.Equal(source.Times.Select(TileRenderSession.FormatTime), Times(WmtsService.LayerIdentifier));

        // Each extent is its datasets': the cell off Nantucket, the currents in Delaware Bay.
        var all = Box(WmtsService.LayerIdentifier);
        var chart = Box(TileLayers.Chart);
        var s111 = Box("S111");
        Assert.InRange(chart[1], 41.0, 42.0);
        Assert.True(s111[3] < 41.0);
        Assert.True(chart[1] > s111[3]);
        // (The layer of every dataset's bounds are its drawn extent, a grid's cells included.)
        Assert.InRange(all[1], Math.Min(chart[1], s111[1]) - 0.01, Math.Min(chart[1], s111[1]) + 0.01);
        Assert.InRange(all[3], Math.Max(chart[3], s111[3]) - 0.01, Math.Max(chart[3], s111[3]) + 0.01);

        // Each layer has the same styles, each with the style's legend.
        string?[] Legends(string layer) =>
            [.. layers[layer].Elements(Wmts + "Style").Select(st => st.Element(Wmts + "LegendURL")?.Attribute(XName.Get("href", "http://www.w3.org/1999/xlink"))?.Value)];
        Assert.NotEmpty(Legends(WmtsService.LayerIdentifier));
        Assert.All(Legends(WmtsService.LayerIdentifier), Assert.NotNull);
        Assert.Equal(Legends(WmtsService.LayerIdentifier), Legends(TileLayers.Chart));
        Assert.Equal(Legends(WmtsService.LayerIdentifier), Legends("S111"));

        // Each layer's tile URLs name it.
        Assert.Contains("/wmts/1.0.0/S111/{Style}/{Time}/", layers["S111"].Element(Wmts + "ResourceURL")!.Attribute("template")!.Value, StringComparison.Ordinal);
        Assert.Contains("/wmts/1.0.0/chart/{Style}/{TileMatrixSet}/", layers[TileLayers.Chart].Element(Wmts + "ResourceURL")!.Attribute("template")!.Value, StringComparison.Ordinal);

        // TileJSON lists the layers, and each layer's TileJSON its own tiles.
        var tileJson = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        var listed = tileJson["s100"]!["layers"]!.AsArray();
        Assert.Equal([TileLayers.Chart, "S111"], listed.Select(l => (string?)l!["id"]));
        var s111Json = JsonNode.Parse(await http.GetStringAsync(new Uri((string)listed[1]!["tilejson"]!), ct))!;
        Assert.Equal("S111", (string?)s111Json["s100"]!["layer"]);
        Assert.StartsWith(server.BaseUri.AbsoluteUri + "layers/S111/{z}/{x}/{y}.png", (string?)s111Json["tiles"]![0], StringComparison.Ordinal);
        Assert.Equal(currents.Count, s111Json["s100"]!["times"]!.AsArray().Count);
        var chartJson = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "layers/chart/night/tiles.json"), ct))!;
        Assert.Null(chartJson["s100"]!["times"]);
        Assert.StartsWith(server.BaseUri.AbsoluteUri + "layers/chart/night/{z}/{x}/{y}.png", (string?)chartJson["tiles"]![0], StringComparison.Ordinal);
        using (var unknown = await http.GetAsync(new Uri(server.BaseUri, "layers/S104/tiles.json"), ct))
            Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        // The datasets document says which layer each dataset is in.
        var datasets = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, DatasetsDocument.Path), ct))!["datasets"]!.AsArray();
        Assert.Equal([TileLayers.Chart, "S111"], datasets.Select(d => (string?)d!["layer"]));
    }

    [Fact]
    public async Task Each_layers_tiles_draw_its_datasets_alone()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S111), "S-57 or S-111 fixture not present.");
        using var source = OpenRendered(ByProduct());
        var ct = TestContext.Current.CancellationToken;
        var chart = source.Layer(TileLayers.Chart)!;
        var currents = source.Layer("S111")!;
        var first = currents.Times[0];

        // A tile of the chart draws in the chart layer and in the layer of
        // every dataset, the same there, and not in the currents' layer…
        var (cx, cy) = await DrawnTileAsync(chart, ChartZoom, first);
        var chartTile = await chart.ReadAsync(ChartZoom, cx, cy, null, first, ct);
        Assert.Equal(chartTile, await source.ReadAsync(ChartZoom, cx, cy, null, first, ct));
        Assert.Null(await currents.ReadAsync(ChartZoom, cx, cy, null, first, ct));

        // …and a tile of the currents the other way round.
        var (sx, sy) = await DrawnTileAsync(currents, Zoom, first);
        Assert.NotNull(await source.ReadAsync(Zoom, sx, sy, null, first, ct));
        Assert.Null(await chart.ReadAsync(Zoom, sx, sy, null, first, ct));

        // Over WMTS, by layer, in both encodings.
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();
        var time = Uri.EscapeDataString(TileRenderSession.FormatTime(first));
        using (var rest = await http.GetAsync(new Uri(server.BaseUri, $"wmts/1.0.0/S111/default/{time}/WebMercatorQuad/{Zoom}/{sy}/{sx}.png"), ct))
            Assert.Equal(HttpStatusCode.OK, rest.StatusCode);
        using (var rest = await http.GetAsync(new Uri(server.BaseUri, $"wmts/1.0.0/S111/default/{time}/WebMercatorQuad/{ChartZoom}/{cy}/{cx}.png"), ct))
            Assert.Equal(HttpStatusCode.NoContent, rest.StatusCode);
        using (var kvp = await http.GetAsync(new Uri(server.BaseUri, $"wmts?SERVICE=WMTS&REQUEST=GetTile&LAYER=chart&STYLE=default&TILEMATRIXSET=WebMercatorQuad&TILEMATRIX={ChartZoom}&TILEROW={cy}&TILECOL={cx}&FORMAT=image/png"), ct))
        {
            Assert.Equal(HttpStatusCode.OK, kvp.StatusCode);
            Assert.Equal(chartTile, await kvp.Content.ReadAsByteArrayAsync(ct));
        }

        using (var xyz = await http.GetAsync(new Uri(server.BaseUri, $"layers/chart/{ChartZoom}/{cx}/{cy}.png"), ct))
            Assert.Equal(chartTile, await xyz.Content.ReadAsByteArrayAsync(ct));
        using (var unknown = await http.GetAsync(new Uri(server.BaseUri, $"wmts/1.0.0/S104/default/WebMercatorQuad/{Zoom}/{cy}/{cx}.png"), ct))
            Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
    }

    [Fact]
    public async Task Clients_put_datasets_they_add_in_layers_they_name()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S125), "Fixtures not present.");
        var cell = Path.Combine(_root, Path.GetFileName(S57));
        File.Copy(S57, cell);
        var atons = Path.Combine(_root, Path.GetFileName(S125));
        File.Copy(S125, atons);

        var settings = new TilesServeCommand.Settings
        {
            Input = cell,
            MinZoom = 14,
            MaxZoom = 14,
            Metatile = 1,
            Parallel = 2,
            ManageDatasets = true,
        };
        var managed = new ManagedDatasetTiles(64L * 1024 * 1024);
        var styles = new MarinerStyleRegistry();
        ServedDatasets? datasets = null;
        DatasetChanges Changes() => datasets?.Changes ?? DatasetChanges.None;
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var initial, styles, managed: managed));
        await using var source = new RefreshingTileSource(
            initial!,
            TileRenderSession.Fingerprint(settings),
            () => TileRenderSession.Fingerprint(settings, Changes()),
            () => TilesServeCommand.OpenRendered(settings, out var reopened, styles, Changes(), managed) == 0 ? reopened : null,
            Timeout.InfiniteTimeSpan,
            _ => { });
        datasets = new ServedDatasets(
            source,
            changes => TilesServeCommand.OpenRendered(settings, out var changed, styles, changes, managed) == 0 ? changed : null,
            changes => TileRenderSession.Fingerprint(settings, changes));
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct, datasets: datasets);
        using var http = new HttpClient();
        var uri = new Uri(server.BaseUri, ServedDatasets.Path);

        // No layers at first; a layer that can't be named is refused.
        Assert.Empty(source.Layers);
        var revision = source.Revision;
        using (var bad = await http.PostAsJsonAsync(uri, new { paths = new[] { atons }, layer = "s100" }, ct))
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Single(datasets.Datasets);

        using (var add = await http.PostAsJsonAsync(uri, new { paths = new[] { atons }, layer = "aids" }, ct))
        {
            Assert.Equal(HttpStatusCode.OK, add.StatusCode);
            var json = JsonNode.Parse(await add.Content.ReadAsStringAsync(ct))!;
            Assert.Equal([null, "aids"], json["datasets"]!.AsArray().Select(d => (string?)d!["layer"]));
        }

        Assert.Equal(["aids"], source.Layers);
        Assert.NotEqual(revision, source.Revision);
        var capabilities = XDocument.Parse(await http.GetStringAsync(new Uri(server.BaseUri, WmtsService.CapabilitiesPath), ct));
        Assert.Equal(
            [WmtsService.LayerIdentifier, "aids"],
            capabilities.Descendants(Wmts + "Layer").Select(l => l.Element(Ows + "Identifier")!.Value));

        // The aids layer is in Chesapeake Bay, not off Nantucket.
        var aids = source.Layer("aids")!;
        var bounds = aids.ToTileJson(null)["bounds"]!.AsArray().Select(b => (double)b!).ToArray();
        Assert.True(bounds[3] < 40);

        // The start-up cell can be put in a layer too.
        using (var move = await http.PostAsJsonAsync(uri, new { paths = new[] { cell }, layer = "aids" }, ct))
            Assert.Equal(HttpStatusCode.OK, move.StatusCode);
        Assert.Equal(["aids"], source.Layers);
        Assert.True(aids.ToTileJson(null)["bounds"]!.AsArray().Select(b => (double)b!).ToArray()[3] > 41);

        using (var remove = await http.SendAsync(new HttpRequestMessage(HttpMethod.Delete, uri) { Content = JsonContent.Create(new { paths = new[] { atons } }) }, ct))
            Assert.Equal(HttpStatusCode.OK, remove.StatusCode);
        Assert.Equal(["aids"], source.Layers);
        Assert.Equal(cell, Assert.Single(datasets.Changes.Layers).Key);
    }
}
