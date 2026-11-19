using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Infrastructure.Tiles;
using EncDotNet.S100.Cli.Web;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Tests for the <c>tiles serve</c> web page's shell (issue #1585): the
/// rendered header, rail and map, and what the page works out for the
/// summary, the theme, the start view, the zoom limits, the URL hash and the
/// coverage outlines.
/// </summary>
public sealed class ServePageShellTests
{
    // Off Nantucket.
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    [Fact]
    public async Task The_page_has_the_header_rail_and_map()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var settings = new TilesServeCommand.Settings { Layers = [S57], MinZoom = 11, MaxZoom = 14, Metatile = 1, Parallel = 2 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        using var _ = source!;
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source!, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        var html = WebUtility.HtmlDecode(await http.GetStringAsync(server.BaseUri, ct));

        // The browser tab and the header (D3, Q1).
        Assert.Contains("<title>1 dataset · S-57 — s100 serve</title>", html, StringComparison.Ordinal);
        Assert.Contains("class=\"serve-header\"", html, StringComparison.Ordinal);
        Assert.Contains(">s100 serve<", html, StringComparison.Ordinal);
        Assert.Contains(">1 dataset · S-57<", html, StringComparison.Ordinal);
        Assert.Contains("Not for navigation", html, StringComparison.Ordinal);
        Assert.Contains("data-state=\"live\"", html, StringComparison.Ordinal);
        Assert.Contains("Copy a link to this view", html, StringComparison.Ordinal);
        Assert.Contains(">Use in GIS<", html, StringComparison.Ordinal);

        // The rail, with Datasets open beside it (D1), and the phone sheet's frame (D2).
        Assert.Contains("<nav class=\"rail\" aria-label=\"Panels\">", html, StringComparison.Ordinal);
        foreach (var label in new[] { "Datasets", "Display", "GIS" })
            Assert.Contains($"<span class=\"rail-label\">{label}</span>", html, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"true\" title=\"Datasets\"", html, StringComparison.Ordinal);
        Assert.Contains("<section class=\"panel\" aria-label=\"Datasets\">", html, StringComparison.Ordinal);
        Assert.Contains("data-height=\"lowered\"", html, StringComparison.Ordinal);
        Assert.Contains("US5MA1BO", html, StringComparison.Ordinal);

        // The map and its furniture (D5, D6).
        Assert.Contains("<div id=\"map\" class=\"map\"></div>", html, StringComparison.Ordinal);
        foreach (var button in new[] { "Zoom in", "Zoom out", "Zoom to the data", "North up" })
            Assert.Contains($"aria-label=\"{button}\"", html, StringComparison.Ordinal);
        Assert.Contains("data-scale-label", html, StringComparison.Ordinal);
        Assert.Contains("data-readout-zoom", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_built_tile_set_is_named_by_its_tile_json()
    {
        var folder = Path.Combine(Path.GetTempPath(), "serve-shell-" + Guid.NewGuid().ToString("N"), "harbour");
        Directory.CreateDirectory(Path.Combine(folder, "3", "4"));
        try
        {
            var ct = TestContext.Current.CancellationToken;
            await File.WriteAllBytesAsync(Path.Combine(folder, "3", "4", "2.png"), [0x89, (byte)'P', (byte)'N', (byte)'G'], ct);
            var source = TileSource.Open(folder);
            await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: "t0ken", cancellationToken: ct);
            using var http = new HttpClient();

            var html = WebUtility.HtmlDecode(await http.GetStringAsync(server.BaseUri, ct));

            Assert.Contains("<title>harbour — s100 serve</title>", html, StringComparison.Ordinal);
            Assert.Contains("Not for navigation", html, StringComparison.Ordinal);
            Assert.Contains("A built tile set", html, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, recursive: true);
        }
    }

    [Fact]
    public void The_summary_counts_datasets_and_names_their_products()
    {
        Assert.Equal("3 datasets · S-101", ServeSummary.Describe(Data(Dataset("a", "S-101"), Dataset("b", "S-101"), Dataset("c", "S-101"))));
        Assert.Equal("2 datasets · S-101, S-111", ServeSummary.Describe(Data(Dataset("b", "S-111"), Dataset("a", "S-101"))));
        Assert.Equal("1 dataset · S-57", ServeSummary.Describe(Data(Dataset("a", "S-57"))));
        Assert.Equal("No datasets", ServeSummary.Describe(Data()));
        Assert.Equal("Solent approaches", ServeSummary.Describe(new FakeData(TileSet() with { Name = "Solent approaches" }, hasDatasets: false, [])));
        Assert.Equal("3 datasets · S-101 — s100 serve", ServeSummary.Title(Data(Dataset("a", "S-101"), Dataset("b", "S-101"), Dataset("c", "S-101"))));
    }

    [Theory]
    [InlineData("day", PageAppearance.Auto, "auto")]
    [InlineData("day", PageAppearance.Light, "light")]
    [InlineData("day", PageAppearance.Dark, "dark")]
    [InlineData(null, PageAppearance.Auto, "auto")]
    [InlineData("dusk", PageAppearance.Auto, "dark")]
    [InlineData("dusk", PageAppearance.Dark, "dark")]
    [InlineData("dusk", PageAppearance.Light, "light")]
    [InlineData("night", PageAppearance.Auto, "night")]
    [InlineData("night", PageAppearance.Light, "night")]
    [InlineData("night", PageAppearance.Dark, "night")]
    public void Dusk_forces_dark_and_night_forces_night(string? palette, PageAppearance page, string theme) =>
        Assert.Equal(theme, ServeTheme.Resolve(palette, page));

    [Fact]
    public void The_start_view_frames_the_data_where_the_chart_draws()
    {
        // The Solent cells: framed in a desktop map below zoom 13, so clamped
        // to 13 and centred on the TileJSON centre (D12).
        var solent = TileSet() with { Bounds = [-1.40, 50.65, -0.95, 50.85], Center = [-1.235, 50.775, 13], MinZoom = 13, MaxZoom = 16 };
        var start = ServeMapView.Start(solent, 900, 740);
        Assert.Equal(13, start.Zoom);
        Assert.Equal(-1.235, start.Longitude, 6);
        Assert.Equal(50.775, start.Latitude, 6);
        Assert.Equal(ZoomLimit.Within, ServeMapView.Limit(start.Zoom, 13, 16));

        // With a minimum zoom the fit reaches, the bounds' centre at the fitted zoom.
        var wide = solent with { MinZoom = 8 };
        var fitted = ServeMapView.Start(wide, 900, 740);
        Assert.InRange(fitted.Zoom, 10.5, 12);
        Assert.Equal(-1.175, fitted.Longitude, 6);
        Assert.InRange(fitted.Latitude, 50.74, 50.76);

        // The fitted bounds fill the width or height, less the padding.
        var span = 360 / Math.Pow(2, fitted.Zoom) * (900 - 2 * ServeMapView.Padding) / 256;
        Assert.True(span >= 0.45 - 1e-9);

        // A tiny area is clamped to the maximum zoom.
        var buoy = solent with { Bounds = [-1.2, 50.77, -1.1999, 50.7701], MinZoom = 8 };
        Assert.Equal(16, ServeMapView.Start(buoy, 900, 740).Zoom);

        // No bounds: the TileJSON centre; nothing at all: the world at the minimum zoom.
        Assert.Equal(new MapView(-1.235, 50.775, 13), ServeMapView.Start(solent with { Bounds = null }, 900, 740));
        Assert.Equal(new MapView(0, 0, 2), ServeMapView.Start(TileSet() with { Bounds = null, Center = null, MinZoom = 2 }, 900, 740));
    }

    [Fact]
    public void The_start_view_crosses_the_antimeridian()
    {
        var fiji = TileSet() with { Bounds = [177, -19, -179, -16], Center = null, MinZoom = 0, MaxZoom = 14 };
        var start = ServeMapView.Start(fiji, 1000, 800);
        Assert.Equal(179, start.Longitude, 6);
        Assert.InRange(start.Zoom, 7.5, 9);
    }

    [Theory]
    [InlineData(10, ZoomLimit.BelowMinimum)]
    [InlineData(12.4, ZoomLimit.BelowMinimum)]
    [InlineData(12.5, ZoomLimit.Within)]
    [InlineData(13, ZoomLimit.Within)]
    [InlineData(16.5, ZoomLimit.Within)]
    [InlineData(16.6, ZoomLimit.PastMaximum)]
    [InlineData(19, ZoomLimit.PastMaximum)]
    public void The_chart_draws_from_half_a_zoom_below_the_minimum_to_half_past_the_maximum(double zoom, ZoomLimit limit) =>
        Assert.Equal(limit, ServeMapView.Limit(zoom, 13, 16));

    [Fact]
    public void The_hash_holds_the_view_and_the_page_parameters()
    {
        var (view, parameters) = ServeHash.Parse("#13/50.775/-1.235&palette=night&t=2021-04-01T10:00Z&dataset=101GB0040242B&pick=50.7705,-1.1980");
        Assert.Equal(new MapView(-1.235, 50.775, 13), view);
        Assert.Equal("night", parameters["palette"]);
        Assert.Equal("2021-04-01T10:00Z", parameters["t"]);
        Assert.Equal("101GB0040242B", parameters["dataset"]);
        Assert.Equal("50.7705,-1.1980", parameters["pick"]);

        Assert.Equal(
            "dataset=101GB0040242B&palette=night&pick=50.7705,-1.1980&t=2021-04-01T10:00Z",
            ServeHash.FormatParameters(parameters));

        Assert.Equal(new MapView(4.5, -33.25, 9.5, 30), ServeHash.Parse("9.5/-33.25/4.5/30").View);
        Assert.Equal("a b", ServeHash.Parse("#layer=a%20b").Parameters["layer"]);
        Assert.Equal("layer=a%20b", ServeHash.FormatParameters([new("layer", "a b")]));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#13/50.775")]
    [InlineData("#13/95/1")]
    [InlineData("#x/50/1")]
    [InlineData("#13/50/1/0/0/0")]
    public void A_hash_without_a_view_is_ignored(string? hash)
    {
        var (view, parameters) = ServeHash.Parse(hash);
        Assert.Null(view);
        Assert.Empty(parameters);
    }

    [Fact]
    public void Outlines_are_the_coverage_or_else_the_bounds()
    {
        var coverage = new JsonObject
        {
            ["type"] = "MultiPolygon",
            ["coordinates"] = JsonNode.Parse("[[[[0,0],[1,0],[1,1],[0,0]]]]"),
        };
        var datasets = new[]
        {
            Dataset("covered", "S-101") with { Coverage = coverage },
            Dataset("bounded", "S-57") with { Bounds = [1, 2, 3, 4] },
            Dataset("nowhere", "S-124"),
        };

        var collection = ServeOutlines.FeatureCollection(datasets);

        Assert.Equal("FeatureCollection", (string?)collection["type"]);
        var features = collection["features"]!.AsArray();
        Assert.Equal(2, features.Count);
        Assert.Equal("covered", (string?)features[0]!["properties"]!["id"]);
        Assert.Equal("S-101", (string?)features[0]!["properties"]!["product"]);
        Assert.Equal("MultiPolygon", (string?)features[0]!["geometry"]!["type"]);
        Assert.NotSame(coverage, features[0]!["geometry"]);
        Assert.Equal("Polygon", (string?)features[1]!["geometry"]!["type"]);
        Assert.Equal("[[[1,2],[3,2],[3,4],[1,4],[1,2]]]", features[1]!["geometry"]!["coordinates"]!.ToJsonString());
    }

    private static ServedTileSet TileSet() => new("test", "png", 0, 14, [-1, -1, 1, 1], [0, 0, 7], "day", HighDpi: false);

    private static ServedDataset Dataset(string id, string product) =>
        new(id, id + ".000", product, null, null, null, null, [], null, null, null);

    private static FakeData Data(params ServedDataset[] datasets) => new(TileSet(), hasDatasets: true, datasets);

    private sealed class FakeData(ServedTileSet tileSet, bool hasDatasets, IReadOnlyList<ServedDataset> datasets) : IServePageData
    {
        public ServedTileSet TileSet => tileSet;

        public IReadOnlyList<string> Palettes => [];

        public IReadOnlyList<DateTime> Times => [];

        public bool HasDatasets => hasDatasets;

        public IReadOnlyList<ServedDataset> Datasets => datasets;

        public IReadOnlyList<string> Layers => [];

        public string? Revision => null;

        public Task<FeatureQueryResult?> QueryFeaturesAsync(double latitude, double longitude, double radiusMeters, int maxResults, DateTime? time, CancellationToken cancellationToken) =>
            Task.FromResult<FeatureQueryResult?>(null);
    }
}
