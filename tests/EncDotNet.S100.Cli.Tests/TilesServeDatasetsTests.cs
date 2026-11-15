using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the <c>tiles serve</c> <c>/datasets</c> document (issue #1504):
/// each dataset's file, product, producer, bounds, coverage, time steps and
/// data dynamicity, so a GIS client needn't open the datasets itself.
/// </summary>
public sealed class TilesServeDatasetsTests
{
    private static string S111 => Path.Combine(AppContext.BaseDirectory, "TestData", "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    [Theory]
    [InlineData("S-111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5", "US00")]
    [InlineData("S-104", "104US004SC1CO_20251217T12Z.h5", "US00")]
    [InlineData("S-102", "102ca00abc.h5", "CA00")]
    [InlineData("S-57", "US5MA1BO.000", "US")]
    [InlineData("S-57", "gb40162a.000", "GB")]
    [InlineData("S-104", "111US00_x.h5", null)]
    [InlineData("S-124", "navwarn_surface.gml", null)]
    [InlineData("S-57", "chart.000", null)]
    public void The_producer_comes_from_the_file_name(string product, string file, string? producer) =>
        Assert.Equal(producer, DatasetsDocument.Producer(product, file));

    [Theory]
    [InlineData(DataDynamicity.Observation, 1, "observation", "Observed")]
    [InlineData(DataDynamicity.AstronomicalPrediction, 2, "astronomicalPrediction", "Predicted")]
    [InlineData(DataDynamicity.AnalysisOrHybrid, 3, "analysisOrHybrid", "Analysis")]
    [InlineData(DataDynamicity.HydrodynamicHindcast, 4, "hydrodynamicHindcast", "Hindcast")]
    [InlineData(DataDynamicity.HydrodynamicForecast, 5, "hydrodynamicForecast", "Forecast")]
    public void Data_dynamicity_has_its_code_name_and_label(DataDynamicity kind, int code, string value, string label)
    {
        var json = DatasetsDocument.Dynamicity(kind);
        Assert.Equal(code, (int)json["code"]!);
        Assert.Equal(value, (string?)json["value"]);
        Assert.Equal(label, (string?)json["label"]);
    }

    [Fact]
    public void Bounds_across_the_antimeridian_make_two_polygons()
    {
        var plain = DatasetsDocument.Coverage(new BoundingBox(10, 20, 11, 21));
        Assert.Equal("MultiPolygon", (string?)plain["type"]);
        var ring = Assert.Single(Assert.Single(plain["coordinates"]!.AsArray())!.AsArray())!.AsArray();
        Assert.Equal(5, ring.Count);
        Assert.Equal(20.0, (double)ring[0]![0]!);
        Assert.Equal(10.0, (double)ring[0]![1]!);
        Assert.Equal(ring[0]!.ToJsonString(), ring[4]!.ToJsonString());

        var crossing = DatasetsDocument.Coverage(new BoundingBox(-10, 170, 10, -170));
        Assert.Equal(2, crossing["coordinates"]!.AsArray().Count);
    }

    [Fact]
    public void Coverage_keeps_holes_and_closes_rings()
    {
        var area = new CoverageArea
        {
            ExteriorRing = [new(0, 0), new(0, 2), new(2, 2), new(2, 0)],
            InteriorRings = [[new(0.5, 0.5), new(0.5, 1), new(1, 1), new(0.5, 0.5)]],
        };

        var polygon = Assert.Single(DatasetsDocument.Coverage([area])["coordinates"]!.AsArray())!.AsArray();
        Assert.Equal(2, polygon.Count);
        Assert.Equal(5, polygon[0]!.AsArray().Count);
        Assert.Equal(4, polygon[1]!.AsArray().Count);
    }

    [Fact]
    public async Task Datasets_lists_each_dataset_with_its_own_bounds()
    {
        Assert.SkipUnless(File.Exists(S111) && File.Exists(S57), "S-57 or S-111 fixture not present.");
        var settings = new TilesServeCommand.Settings { Layers = [S57, S111], Parallel = 2 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var opened));
        using var source = opened!;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, "secret-token", cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        // tiles.json points at the document.
        var tileJson = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        var url = (string?)tileJson["s100"]!["datasetsUrl"];
        Assert.Equal($"http://127.0.0.1:{server.Port}/secret-token/datasets", url);

        using var response = await http.GetAsync(url, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("*", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
        Assert.True((bool)json["notForNavigation"]!);
        var datasets = json["datasets"]!.AsArray();
        Assert.Equal(2, datasets.Count);

        var chart = datasets[0]!;
        Assert.Equal("US5MA1BO", (string?)chart["id"]);
        Assert.Equal("US5MA1BO.000", (string?)chart["file"]);
        Assert.Equal("S-57", (string?)chart["product"]);
        Assert.Equal("US", (string?)chart["producer"]);
        Assert.Equal("dataCoverage", (string?)chart["coverageSource"]);
        Assert.Equal("MultiPolygon", (string?)chart["coverage"]!["type"]);
        Assert.Empty(chart["times"]!.AsArray());
        Assert.Null(chart["dataDynamicity"]);
        Assert.True((bool)chart["notForNavigation"]!);

        var currents = datasets[1]!;
        Assert.Equal("111US00_DBOFS_20260320T18Z_US4DE1BB", (string?)currents["id"]);
        Assert.Equal("S-111", (string?)currents["product"]);
        Assert.NotNull((string?)currents["edition"]);
        Assert.Equal("US00", (string?)currents["producer"]);
        Assert.NotNull(currents["coverage"]);
        var times = currents["times"]!.AsArray();
        Assert.Equal(48, times.Count);
        Assert.Equal("2026-03-20T19:00:00Z", (string?)times[0]);

        // Per dataset, not the union: the two don't share bounds, and each
        // lies within the tile set's.
        var chartBounds = Bounds(chart);
        var currentBounds = Bounds(currents);
        Assert.NotEqual(chartBounds, currentBounds);
        var union = Bounds(tileJson);
        foreach (var b in new[] { chartBounds, currentBounds })
        {
            Assert.True(b[0] < b[2] && b[1] < b[3]);
            Assert.True(b[0] >= union[0] - 1e-6 && b[1] >= union[1] - 1e-6 && b[2] <= union[2] + 1e-6 && b[3] <= union[3] + 1e-6);
        }
    }

    [Fact]
    public async Task A_built_tile_set_has_no_datasets()
    {
        var source = new BuiltTileSet();
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: TestContext.Current.CancellationToken);
        using var http = new HttpClient();
        var ct = TestContext.Current.CancellationToken;

        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync(new Uri(server.BaseUri, "datasets"), ct)).StatusCode);
        var tileJson = JsonNode.Parse(await http.GetStringAsync(new Uri(server.BaseUri, "tiles.json"), ct))!;
        Assert.Null(tileJson["s100"]?["datasetsUrl"]);
    }

    private static double[] Bounds(JsonNode node) =>
        [.. node["bounds"]!.AsArray().Select(v => (double)v!)];

    private sealed class BuiltTileSet : ITileSource
    {
        public string Path => "built";

        public TileImageFormat Format => TileImageFormat.Png;

        public ValueTask<byte[]?> ReadAsync(int zoom, int x, int y, string? palette, DateTime? time, CancellationToken cancellationToken) =>
            ValueTask.FromResult<byte[]?>(null);

        public JsonObject ToTileJson(string? palette, DateTime? time = null) => new()
        {
            ["tilejson"] = "3.0.0",
            ["s100"] = new JsonObject(),
        };
    }
}
