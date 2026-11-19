using System.Net;
using System.Text.Json.Nodes;
using EncDotNet.S100.Cli.Commands;
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
/// Tests for the <c>tiles serve</c> web page's Datasets panel (issue #1578):
/// the editions and display scales the server reports, the purpose bands,
/// grouping, search and filters, the row and details text, and the rendered list.
/// </summary>
public sealed class ServeDatasetsPanelTests
{
    // Off Nantucket: a harbour cell (purpose 5).
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    private static string S101Folder => Path.Combine(AppContext.BaseDirectory, "TestData", "S101");

    // Surface currents in Delaware Bay.
    private static string S111 => Path.Combine(AppContext.BaseDirectory, "TestData", "S111", "111US00_DBOFS_20260320T18Z_US4DE1BB.h5");

    [Fact]
    public void Chart_cells_report_their_edition_and_update()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S111), "S-57 or S-111 fixture not present.");
        using var source = Open(S57, S111);
        var data = new TileSourcePageData(source, hasToken: false);

        var chart = Assert.Single(data.Datasets, d => d.Product == "S-57");
        Assert.NotNull(chart.EditionNumber);
        Assert.NotNull(chart.UpdateNumber);
        Assert.NotNull(chart.IssueDate);
        Assert.True(chart.HasDataCoverage || chart.Coverage is not null);
        Assert.Null(chart.MaximumDisplayScale); // S-57 purpose comes from the cell name.
        Assert.Equal(5, ServeDatasetInfo.Purpose(chart));

        // Other products have no cell edition of their own, and the document is as before for them.
        var currents = Assert.Single(data.Datasets, d => d.Product == "S-111");
        Assert.Null(currents.EditionNumber);
        Assert.Null(currents.UpdateNumber);
        var json = source.ToDatasetsJson()!["datasets"]!.AsArray().OfType<JsonObject>().Single(d => (string?)d["product"] == "S-111");
        Assert.False(json.ContainsKey("editionNumber"));
        Assert.False(json.ContainsKey("displayScale"));
    }

    [Fact]
    public void S101_cells_report_their_display_scale_range()
    {
        var cells = Directory.Exists(S101Folder) ? Directory.GetFiles(S101Folder, "*.000") : [];
        Assert.SkipWhen(cells.Length == 0, "S-101 fixtures not present.");
        using var source = Open(cells);
        var data = new TileSourcePageData(source, hasToken: false);

        Assert.All(data.Datasets, d => Assert.NotNull(d.EditionNumber));
        var scaled = data.Datasets.Where(d => d.MaximumDisplayScale is not null).ToList();
        Assert.NotEmpty(scaled);
        Assert.All(scaled, d =>
        {
            Assert.True(d.MinimumDisplayScale is null || d.MinimumDisplayScale >= d.MaximumDisplayScale);
            Assert.Equal(ServeDatasetInfo.Band(d.MaximumDisplayScale!.Value), ServeDatasetInfo.Purpose(d));
        });
    }

    [Theory]
    [InlineData("US1EEZ1M.000", 1)]
    [InlineData("US2WC01M.000", 2)]
    [InlineData("US3WA01M.000", 3)]
    [InlineData("US4WA1CD.000", 4)]
    [InlineData("us5ma1bo.000", 5)]
    [InlineData("GB6XXXXX", 6)]
    [InlineData("US7WA1CD.000", null)]
    [InlineData("chart.000", null)]
    public void S57_purpose_comes_from_the_cell_name(string file, int? purpose) =>
        Assert.Equal(purpose, ServeDatasetInfo.Purpose(Dataset("x", "S-57") with { File = file }));

    [Theory]
    [InlineData(3_000_000, 1)]
    [InlineData(1_500_000, 1)]
    [InlineData(1_499_999, 2)]
    [InlineData(350_000, 2)]
    [InlineData(90_000, 3)]
    [InlineData(45_000, 4)]
    [InlineData(22_000, 4)]
    [InlineData(21_999, 5)]
    [InlineData(4_000, 5)]
    [InlineData(2_500, 6)]
    public void S101_purpose_comes_from_the_maximum_display_scale_in_the_S57_bands(int maximum, int purpose)
    {
        Assert.Equal(purpose, ServeDatasetInfo.Band(maximum));
        Assert.Equal(purpose, ServeDatasetInfo.Purpose(Dataset("x", "S-101") with { MaximumDisplayScale = maximum }));
    }

    [Fact]
    public void A_cell_without_a_band_is_Other_and_other_products_have_no_purpose()
    {
        Assert.Equal("Other", ServeDatasetInfo.PurposeGroup(Dataset("a", "S-101")));
        Assert.Equal("Other", ServeDatasetInfo.PurposeGroup(Dataset("b", "S-57") with { File = "chart.000" }));
        Assert.Null(ServeDatasetInfo.PurposeGroup(Dataset("c", "S-111")));
    }

    [Fact]
    public void Rows_and_details_read_like_the_Library()
    {
        var cell = Dataset("US5WA22M", "S-57") with
        {
            Producer = "US",
            EditionNumber = 44,
            UpdateNumber = 2,
            IssueDate = new DateOnly(2024, 12, 18),
            Bounds = [-122.5519865, 47.2309017, -122.3535228, 47.3253997],
            Coverage = new JsonObject { ["type"] = "MultiPolygon", ["coordinates"] = new JsonArray(new JsonArray(), new JsonArray()) },
            HasDataCoverage = true,
        };
        Assert.Equal("Ed 44 · Upd 2 · US", ServeDatasetInfo.RowMeta(cell));
        Assert.Equal("Ed 44 · US", ServeDatasetInfo.RowMeta(cell with { UpdateNumber = 0 }));
        Assert.Equal("Edition 44, update 2, 18 Dec 2024", ServeDatasetInfo.EditionLong(cell));
        Assert.Equal(("47°13.9′N – 47°19.5′N", "122°33.1′W – 122°21.2′W"), ServeDatasetInfo.Bounds(cell));
        Assert.Equal("Data coverage, 2 areas", ServeDatasetInfo.CoverageText(cell));
        Assert.Equal("Bounds only", ServeDatasetInfo.CoverageText(cell with { HasDataCoverage = false }));
        Assert.Null(ServeDatasetInfo.TimeLine(cell));

        var s101 = Dataset("101GB0040242B", "S-101") with { MinimumDisplayScale = 45000, MaximumDisplayScale = 22000 };
        Assert.Equal("1:45 000 – 1:22 000", ServeDatasetInfo.DisplayScale(s101));
        Assert.Null(ServeDatasetInfo.RowMeta(Dataset("x", "S-111")).NullIfEmpty());

        var start = new DateTime(2021, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var currents = Dataset("111UK", "S-111") with
        {
            Dynamicity = "Forecast",
            Times = [.. Enumerable.Range(0, 288).Select(i => start.AddMinutes(15 * i))],
        };
        Assert.Equal("Forecast · 1 Apr 2021 00:00 – 3 Apr 23:45 UTC · 288 steps", ServeDatasetInfo.TimeLine(currents));
    }

    [Fact]
    public void A_dataset_with_no_coverage_or_bounds_has_no_place_and_is_a_problem()
    {
        var lost = Dataset("lost", "S-101");
        Assert.False(ServeDatasetInfo.HasPlace(lost));
        Assert.True(ServeDatasetInfo.IsProblem(lost));
        Assert.Null(ServeDatasetInfo.CoverageText(lost));
        Assert.True(ServeDatasetInfo.HasPlace(lost with { Bounds = [0, 0, 1, 1] }));
    }

    [Fact]
    public void Groups_are_by_layer_then_product_then_purpose()
    {
        ServedDataset[] datasets =
        [
            Dataset("US5A", "S-57") with { File = "US5WA01A.000" },
            Dataset("C111", "S-111"),
            Dataset("US3A", "S-57") with { File = "US3WA01A.000" },
            Dataset("GB", "S-101") with { MaximumDisplayScale = 22000 },
            Dataset("GBX", "S-101"),
            Dataset("US3B", "S-57") with { File = "US3WA01B.000" },
        ];

        var groups = ServeDatasetList.Group(datasets, []);
        Assert.Equal(["S-101 · Approach", "S-101 · Other", "S-111", "S-57 · Coastal", "S-57 · Harbour"], groups.Select(g => g.Label));
        Assert.Equal(["US3A", "US3B"], groups[3].Datasets.Select(d => d.Id));

        // With layers, by layer first, in the layers' order.
        var layered = datasets.Select(d => d with { Layer = d.Product == "S-111" ? "currents" : "charts" }).ToArray();
        var byLayer = ServeDatasetList.Group(layered, ["currents", "charts"]);
        Assert.Equal("currents", byLayer[0].Layer);
        Assert.All(byLayer.Skip(1), g => Assert.Equal("charts", g.Layer));
        var items = ServeDatasetList.Items(layered, byLayer, byLayer.Select(g => g.Key).ToHashSet(), DatasetFilter.None, layerHeadings: true);
        Assert.Equal(["currents", "charts"], items.Select(i => i.LayerHeading).OfType<string>());
    }

    [Fact]
    public void Groups_start_collapsed_above_30_datasets_except_the_first()
    {
        var few = Enumerable.Range(0, 30).Select(i => Dataset("US5A" + i, "S-57") with { File = $"US{(i % 2 == 0 ? 3 : 5)}WA{i:000}.000" }).ToArray();
        var groups = ServeDatasetList.Group(few, []);
        Assert.Equal(groups.Count, ServeDatasetList.InitiallyExpanded(groups, few.Length).Count);

        var many = few.Append(Dataset("extra", "S-111")).ToArray();
        var manyGroups = ServeDatasetList.Group(many, []);
        var expanded = ServeDatasetList.InitiallyExpanded(manyGroups, many.Length);
        Assert.Equal([manyGroups[0].Key], expanded);

        // Collapsed groups list their header only.
        var items = ServeDatasetList.Items(many, manyGroups, expanded, DatasetFilter.None, layerHeadings: false);
        Assert.Equal(manyGroups.Count + manyGroups[0].Datasets.Count, items.Count);
    }

    [Fact]
    public void A_search_or_filter_makes_a_flat_list_of_the_matches()
    {
        ServedDataset[] datasets =
        [
            Dataset("US5WA22M", "S-57") with { File = "US5WA22M.000", Bounds = [0, 0, 1, 1] },
            Dataset("US3WA01M", "S-57") with { File = "US3WA01M.000", Bounds = [0, 0, 1, 1] },
            Dataset("101GB0040242B", "S-101") with { MaximumDisplayScale = 22000, Bounds = [0, 0, 1, 1] },
            Dataset("lost", "S-111") with { File = "lost.h5" },
        ];
        var groups = ServeDatasetList.Group(datasets, []);
        HashSet<string> none = [];

        Ids(new DatasetFilter("wa2", none, none, false), ["US5WA22M"]);
        Ids(new DatasetFilter(" .000 ", none, none, false), ["US5WA22M", "US3WA01M", "101GB0040242B"]);
        Ids(new DatasetFilter("", Set("S-57"), none, false), ["US5WA22M", "US3WA01M"]);
        Ids(new DatasetFilter("", none, Set("Approach"), false), ["101GB0040242B"]);
        Ids(new DatasetFilter("", none, Set("Harbour", "Coastal"), false), ["US5WA22M", "US3WA01M"]);
        Ids(new DatasetFilter("", none, none, true), ["lost"]);
        Ids(new DatasetFilter("US3", Set("S-57"), Set("Harbour"), false), []);
        Assert.False(DatasetFilter.None.IsActive);
        Assert.False(new DatasetFilter("  ", none, none, false).IsActive);

        void Ids(DatasetFilter filter, string[] expected)
        {
            Assert.True(filter.IsActive);
            var items = ServeDatasetList.Items(datasets, groups, new HashSet<string>(), filter, layerHeadings: false);
            Assert.All(items, i => Assert.Null(i.Group));
            Assert.Equal(expected, items.Select(i => i.Dataset!.Id));
        }
    }

    [Fact]
    public async Task The_page_lists_the_datasets_in_groups_with_search_and_filters()
    {
        Assert.SkipUnless(File.Exists(S57) && File.Exists(S111), "S-57 or S-111 fixture not present.");
        using var source = Open(S57, S111);
        var ct = TestContext.Current.CancellationToken;
        await using var server = await TileServer.StartAsync(source, IPAddress.Loopback, 0, token: null, cancellationToken: ct);
        using var http = new HttpClient();

        var html = WebUtility.HtmlDecode(await http.GetStringAsync(server.BaseUri, ct));

        Assert.Contains("placeholder=\"Search datasets\"", html, StringComparison.Ordinal);
        Assert.Contains(">Product", html, StringComparison.Ordinal);
        Assert.Contains(">Purpose", html, StringComparison.Ordinal);
        Assert.Contains("Problems", html, StringComparison.Ordinal);
        Assert.Contains(">S-57 · Harbour<", html, StringComparison.Ordinal);
        Assert.Contains(">S-111<", html, StringComparison.Ordinal);
        Assert.Contains("data-dataset-id=\"US5MA1BO\"", html, StringComparison.Ordinal);
        Assert.Contains(">US5MA1BO.000<", html, StringComparison.Ordinal);
        Assert.Matches(@">Ed \d+( · Upd \d+)? · US<", html);
        Assert.Matches(@">Forecast · .+ UTC · \d+ steps<", html);
    }

    [Fact]
    public void Outlines_carry_each_datasets_layer_so_the_map_can_outline_one_layer()
    {
        var outlines = ServeOutlines.FeatureCollection(Layered);
        var features = outlines["features"]!.AsArray().OfType<JsonObject>().ToList();

        // Every dataset with a place, with its layer; the one with no place is left out.
        Assert.Equal(["GB1", "GB2", "C111", "loose"], features.Select(f => (string?)f["properties"]!["id"]));
        Assert.Equal(["chart", "chart", "S111"], features.Take(3).Select(f => (string?)f["properties"]!["layer"]));
        Assert.False(features[3]["properties"]!.AsObject().ContainsKey("layer"));
        Assert.Equal(["C111"], features.Where(f => (string?)f["properties"]!["layer"] == "S111").Select(f => (string?)f["properties"]!["id"]));
    }

    [Fact]
    public void A_layer_chosen_scopes_the_list_to_its_datasets()
    {
        Assert.Same(Layered, ServeDatasetList.InLayer(Layered, null));
        Assert.Equal(["C111", "C111b"], ServeDatasetList.InLayer(Layered, "S111").Select(d => d.Id));
        Assert.Equal(["GB1", "GB2"], ServeDatasetList.InLayer(Layered, "chart").Select(d => d.Id));
        Assert.Empty(ServeDatasetList.InLayer(Layered, "nothing"));

        // Grouped and searched within the layer only.
        var scoped = ServeDatasetList.InLayer(Layered, "S111");
        var groups = ServeDatasetList.Group(scoped, ["chart", "S111"]);
        Assert.All(groups, g => Assert.Equal("S111", g.Layer));
        var items = ServeDatasetList.Items(scoped, groups, groups.Select(g => g.Key).ToHashSet(), DatasetFilter.None, layerHeadings: false);
        Assert.Equal(["C111", "C111b"], items.Select(i => i.Dataset?.Id).OfType<string>());
        var found = ServeDatasetList.Items(scoped, groups, new HashSet<string>(), new DatasetFilter("b", Set(), Set(), false), layerHeadings: false);
        Assert.Equal(["C111b"], found.Select(i => i.Dataset!.Id));
    }

    [Fact]
    public async Task The_list_says_its_scope_with_a_layer_chosen()
    {
        var all = await RenderPanelAsync(null, null);
        Assert.Contains(">GB1<", all, StringComparison.Ordinal);
        Assert.Contains(">C111<", all, StringComparison.Ordinal);
        Assert.Contains("Layer <span class=\"strong\">S111</span>", all, StringComparison.Ordinal);

        var s111 = await RenderPanelAsync("S111", null);
        Assert.Contains(">C111<", s111, StringComparison.Ordinal);
        Assert.Contains(">C111b<", s111, StringComparison.Ordinal);
        Assert.DoesNotContain(">GB1<", s111, StringComparison.Ordinal);
        Assert.DoesNotContain(">loose<", s111, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"layer-heading\"", s111, StringComparison.Ordinal);
        Assert.Matches("<span class=\"strong\">2</span> of 6 · layer <span class=\"strong\">S111</span>", s111);
        Assert.Contains(">All layers</button>", s111, StringComparison.Ordinal);

        // A dataset outside the layer, from the hash or a link, still opens, and says so.
        var outside = await RenderPanelAsync("S111", "GB1");
        Assert.Contains(">GB1</h3>", outside, StringComparison.Ordinal);
        Assert.Contains("The dataset is in layer chart; the map shows layer S111, so it isn't drawn.", outside, StringComparison.Ordinal);
        Assert.Contains(">Show layer chart</button>", outside, StringComparison.Ordinal);
        var inside = await RenderPanelAsync("S111", "C111");
        Assert.DoesNotContain("isn't drawn", inside, StringComparison.Ordinal);
    }

    // Two layers, a dataset in neither, and one with no place.
    private static readonly ServedDataset[] Layered =
    [
        Dataset("GB1", "S-101") with { Layer = "chart", Bounds = [0, 50, 1, 51] },
        Dataset("GB2", "S-101") with { Layer = "chart", Bounds = [1, 50, 2, 51] },
        Dataset("C111", "S-111") with { Layer = "S111", File = "C111.h5", Bounds = [0, 50, 2, 51] },
        Dataset("C111b", "S-111") with { Layer = "S111", File = "C111b.h5" },
        Dataset("loose", "S-102") with { Bounds = [3, 50, 4, 51] },
        Dataset("lost", "S-102") with { Layer = "bathy" },
    ];

    private static async Task<string> RenderPanelAsync(string? layer, string? selected)
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton<IServePageData>(new LayeredData());
        services.AddSingleton<NavigationManager>(new FixedNavigationManager("http://localhost:8080/"));
        await using var provider = services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(provider, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<DatasetsPanel>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(DatasetsPanel.Layer)] = layer,
                [nameof(DatasetsPanel.Selected)] = selected,
            }))).ToHtmlString());
        return WebUtility.HtmlDecode(html);
    }

    private sealed class FixedNavigationManager : NavigationManager
    {
        public FixedNavigationManager(string baseUri) => Initialize(baseUri, baseUri);
    }

    private sealed class LayeredData : IServePageData
    {
        public ServedTileSet TileSet { get; } = new("datasets", "png", 0, 14, null, null, "day", HighDpi: false);

        public IReadOnlyList<string> Palettes => [];

        public IReadOnlyList<DateTime> Times => [];

        public bool HasDatasets => true;

        public IReadOnlyList<ServedDataset> Datasets => Layered;

        public IReadOnlyList<string> Layers => ["chart", "S111", "bathy"];

        public string? Revision => "0";

        public Task<FeatureQueryResult?> QueryFeaturesAsync(double latitude, double longitude, double radiusMeters, int maxResults, DateTime? time, CancellationToken cancellationToken) =>
            Task.FromResult<FeatureQueryResult?>(null);
    }

    private static RenderedTileSource Open(params string[] layers)
    {
        var settings = new TilesServeCommand.Settings { Layers = layers, MinZoom = 11, MaxZoom = 14, Metatile = 1, Parallel = 2 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var source));
        return source!;
    }

    private static HashSet<string> Set(params string[] values) => [.. values];

    private static ServedDataset Dataset(string id, string product) =>
        new(id, id + ".000", product, null, null, null, null, [], null, null, null);
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string value) => value.Length == 0 ? null : value;
}
