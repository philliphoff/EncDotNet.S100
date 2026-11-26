using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;
using static EncDotNet.S100.Viewer.Tests.SplitMapMcpTestKit;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// The split-map MCP tools (#1735, dual-map design D28), called through a
/// running MCP server: the new tools (list_maps, set_split_map,
/// set_active_map, set_map_link, set_map_name), the map tools' map argument,
/// shownIn on picks, and per-map settle, idle and render stats.
/// </summary>
public sealed class SplitMapMcpToolsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SplitMapMcpFixture> StartAsync(bool split = true, IDatasetCatalog? catalog = null)
    {
        var f = new SplitMapMcpFixture(catalog);
        if (split)
            f.Split.OpenSplit();
        await f.StartAsync(Ct);
        return f;
    }

    private static Dictionary<string, object?> Args(params (string Name, object? Value)[] args) =>
        args.ToDictionary(a => a.Name, a => a.Value);

    private static string[] Ids(JsonNode list) =>
        [.. list["maps"]!.AsArray().Select(m => m!["id"]!.GetValue<string>())];

    [Fact]
    public async Task List_maps_reports_each_map_with_its_label_active_and_viewport()
    {
        await using var f = await StartAsync();
        f.Map("map-1").Notifier.Publish(Viewport(47.6, -122.4));

        var list = Json(await f.CallAsync("list_maps", null, Ct));

        Assert.True(list["split"]!.GetValue<bool>());
        Assert.False(list["linked"]!.GetValue<bool>());
        Assert.Equal("map-2", list["active"]!.GetValue<string>());
        Assert.Equal(["map-1", "map-2"], Ids(list));
        var first = list["maps"]![0]!;
        Assert.Equal("Map 1", first["label"]!.GetValue<string>());
        Assert.False(first["active"]!.GetValue<bool>());
        Assert.False(first["folded"]!.GetValue<bool>());
        Assert.Equal(47.6, first["viewport"]!["centerLatitude"]!.GetValue<double>(), 6);
        Assert.Equal(-122.4, first["viewport"]!["centerLongitude"]!.GetValue<double>(), 6);
        Assert.Null(list["maps"]![1]!["viewport"]);
    }

    [Fact]
    public async Task Set_split_map_opens_a_second_active_map_and_closes_back_to_it()
    {
        await using var f = await StartAsync(split: false);

        var opened = Json(await f.CallAsync("set_split_map", Args(("open", true)), Ct));
        Assert.True(opened["split"]!.GetValue<bool>());
        Assert.Equal(["map-1", "map-2"], Ids(opened));
        Assert.Equal("map-2", opened["active"]!.GetValue<string>());

        // Opening a split map changes nothing.
        Assert.Equal(["map-1", "map-2"], Ids(Json(await f.CallAsync("set_split_map", Args(("open", true)), Ct))));

        var closed = Json(await f.CallAsync("set_split_map", Args(("open", false)), Ct));
        Assert.False(closed["split"]!.GetValue<bool>());
        Assert.Equal(["map-2"], Ids(closed));
        Assert.Equal("map-2", closed["active"]!.GetValue<string>());
    }

    [Fact]
    public async Task Set_active_map_activates_a_map_and_refuses_an_unknown_one()
    {
        await using var f = await StartAsync();

        var list = Json(await f.CallAsync("set_active_map", Args(("map", "map-1")), Ct));
        Assert.Equal("map-1", list["active"]!.GetValue<string>());
        Assert.Same(f.Map("map-1"), f.Views.Active);

        Assert.Equal("map_not_found", ErrorCode(await f.CallAsync("set_active_map", Args(("map", "map-9")), Ct)));
        Assert.Same(f.Map("map-1"), f.Views.Active);
    }

    [Fact]
    public async Task Set_map_link_links_a_split_and_refuses_one_map()
    {
        await using var f = await StartAsync(split: false);
        Assert.Equal("invalid_argument", ErrorCode(await f.CallAsync("set_map_link", Args(("linked", true)), Ct)));

        await f.CallAsync("set_split_map", Args(("open", true)), Ct);
        var linked = Json(await f.CallAsync("set_map_link", Args(("linked", true)), Ct));
        Assert.True(linked["linked"]!.GetValue<bool>());
        Assert.All(linked["maps"]!.AsArray(), m => Assert.True(m!["linked"]!.GetValue<bool>()));
        Assert.True(f.Split.IsLinked);

        var unlinked = Json(await f.CallAsync("set_map_link", Args(("linked", false)), Ct));
        Assert.False(unlinked["linked"]!.GetValue<bool>());
        Assert.False(f.Split.IsLinked);
    }

    [Fact]
    public async Task Set_map_name_names_a_pane_and_a_blank_name_clears_it()
    {
        await using var f = await StartAsync();

        var named = Json(await f.CallAsync("set_map_name", Args(("map", "map-1"), ("name", "  Low water ")), Ct));
        Assert.Equal("Low water", named["maps"]![0]!["name"]!.GetValue<string>());
        Assert.Equal("Map 1", named["maps"]![0]!["label"]!.GetValue<string>());
        var pane = f.Split.Panes.Single(p => p.Id.Value == "map-1");
        Assert.Equal("Low water", pane.Title);

        var cleared = Json(await f.CallAsync("set_map_name", Args(("map", "map-1"), ("name", "")), Ct));
        Assert.Null(cleared["maps"]![0]!["name"]);
        Assert.Equal("Map 1", pane.Title);

        Assert.Equal("map_not_found", ErrorCode(await f.CallAsync("set_map_name", Args(("map", "map-7"), ("name", "x")), Ct)));
        Assert.Equal("invalid_argument", ErrorCode(await f.CallAsync("set_map_name", Args(("map", "map-1"), ("name", new string('x', 41))), Ct)));
    }

    [Fact]
    public async Task Set_viewport_moves_the_named_map_without_changing_the_active_map()
    {
        await using var f = await StartAsync();

        Json(await f.CallAsync("set_viewport", Args(("centerLatitude", 47.6), ("centerLongitude", -122.4), ("zoom", 12.0), ("map", "map-1")), Ct));

        Assert.Contains("SetViewportToCenterAndResolution", f.Map("map-1").Host.Calls);
        Assert.Empty(f.Map("map-2").Host.Calls);
        Assert.Same(f.Map("map-2"), f.Views.Active);

        // No map: the active map.
        Json(await f.CallAsync("set_viewport", Args(("centerLatitude", 47.6), ("centerLongitude", -122.4), ("zoom", 12.0)), Ct));
        Assert.Contains("SetViewportToCenterAndResolution", f.Map("map-2").Host.Calls);

        Assert.Equal("map_not_found", ErrorCode(await f.CallAsync(
            "set_viewport", Args(("centerLatitude", 1.0), ("centerLongitude", 1.0), ("zoom", 3.0), ("map", "map-3")), Ct)));
    }

    [Fact]
    public async Task Fling_flings_the_named_map()
    {
        await using var f = await StartAsync();

        var fling = Json(await f.CallAsync("fling", Args(("velocityX", 1500.0), ("velocityY", 0.0), ("map", "map-1")), Ct));

        Assert.True(fling["started"]!.GetValue<bool>());
        Assert.Contains("Fling(1500,0)", f.Map("map-1").Host.Calls);
        Assert.Empty(f.Map("map-2").Host.Calls);
        Assert.Same(f.Map("map-2"), f.Views.Active);
    }

    [Fact]
    public async Task Render_to_image_renders_the_named_map()
    {
        await using var f = await StartAsync();

        var result = await f.CallAsync("render_to_image", Args(("width", 200), ("height", 100), ("map", "map-1")), Ct);

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(["Render(200x100)"], f.Map("map-1").Host.Calls);
        Assert.Empty(f.Map("map-2").Host.Calls);
        Assert.Equal("map_not_found", ErrorCode(await f.CallAsync("render_to_image", Args(("map", "map-5")), Ct)));
    }

    [Fact]
    public async Task Identify_features_takes_a_map_and_refuses_an_unknown_one()
    {
        await using var f = await StartAsync(catalog: WarningCatalog());

        var found = Json(await f.CallAsync("identify_features", Args(("latitude", 0.0), ("longitude", 0.0), ("map", "map-1")), Ct));
        Assert.Equal("W1", found["features"]![0]!["featureId"]!.GetValue<string>());

        Assert.Equal("map_not_found", ErrorCode(await f.CallAsync(
            "identify_features", Args(("latitude", 0.0), ("longitude", 0.0), ("map", "map-4")), Ct)));
    }

    [Fact]
    public async Task Pick_features_reports_shownIn_for_each_map()
    {
        await using var f = await StartAsync(catalog: WarningCatalog());
        // Map 1 draws the warning's dataset with the point in view; Map 2
        // draws it but looks elsewhere.
        DrawWarnings(f.Map("map-1"));
        DrawWarnings(f.Map("map-2"));
        f.Map("map-1").Notifier.Publish(Viewport(0, 0));
        f.Map("map-2").Notifier.Publish(Viewport(50, 10));

        var pick = Json(await f.CallAsync("pick_features", Args(("latitude", 0.0), ("longitude", 0.0)), Ct));

        Assert.Equal("W1", pick["features"]![0]!["featureId"]!.GetValue<string>());
        var shownIn = pick["shownIn"]!.AsArray();
        Assert.Equal(["map-1", "map-2"], shownIn.Select(s => s!["map"]!.GetValue<string>()));
        Assert.True(shownIn[0]!["shown"]!.GetValue<bool>());
        Assert.Null(shownIn[0]!["reason"]);
        Assert.False(shownIn[1]!["shown"]!.GetValue<bool>());
        Assert.Equal("out_of_view", shownIn[1]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public async Task Pick_features_in_a_named_map_uses_its_pixels_and_says_where_the_dataset_is_hidden()
    {
        await using var f = await StartAsync(catalog: WarningCatalog());
        DrawWarnings(f.Map("map-1"));
        f.Map("map-1").Notifier.Publish(Viewport(0, 0));
        f.Map("map-2").Notifier.Publish(Viewport(0, 0));
        f.Map("map-1").Host.ScreenPoint = new GeoPosition(0, 0);
        f.Map("map-2").Host.ScreenPoint = new GeoPosition(30, 30);

        var pick = Json(await f.CallAsync("pick_features", Args(("x", 400.0), ("y", 300.0), ("map", "map-1")), Ct));

        Assert.Equal(0.0, pick["latitude"]!.GetValue<double>());
        Assert.Equal("W1", pick["features"]![0]!["featureId"]!.GetValue<string>());
        var shownIn = pick["shownIn"]!.AsArray();
        Assert.True(shownIn[0]!["shown"]!.GetValue<bool>());
        Assert.Equal("dataset_hidden", shownIn[1]!["reason"]!.GetValue<string>());
        Assert.Same(f.Map("map-2"), f.Views.Active);
    }

    [Fact]
    public async Task Await_settled_waits_for_the_named_map_only()
    {
        await using var f = await StartAsync();
        f.Map("map-1").Settle = TestMap.SettledNow;

        var one = Json(await f.CallAsync("await_settled", Args(("map", "map-1"), ("confirmMs", 0), ("timeoutMs", 2000)), Ct));
        Assert.True(one["settled"]!.GetValue<bool>());
        Assert.Equal(1, f.Map("map-1").Redraws);

        // Map 2 (the active map) never paints, so it times out.
        var two = Json(await f.CallAsync("await_settled", Args(("map", "map-2"), ("confirmMs", 0), ("timeoutMs", 100)), Ct));
        Assert.False(two["settled"]!.GetValue<bool>());
        Assert.True(two["timedOut"]!.GetValue<bool>());

        Assert.Equal("map_not_found", ErrorCode(await f.CallAsync("await_settled", Args(("map", "map-8")), Ct)));
    }

    [Fact]
    public async Task Await_render_idle_waits_on_the_named_map_or_every_map()
    {
        await using var f = await StartAsync();

        var one = Json(await f.CallAsync("await_render_idle", Args(("map", "map-1"), ("quietPeriodMs", 0)), Ct));
        Assert.Equal("map-1", one["map"]!.GetValue<string>());
        Assert.Equal(1, f.Map("map-1").Activity.Waits);
        Assert.Equal(0, f.Map("map-2").Activity.Waits);
        Assert.Equal(0, f.WindowActivity.Waits);

        var all = Json(await f.CallAsync("await_render_idle", Args(("quietPeriodMs", 0)), Ct));
        Assert.Null(all["map"]);
        Assert.Equal(1, f.WindowActivity.Waits);
    }

    [Fact]
    public async Task Get_render_stats_reports_the_named_map()
    {
        await using var f = await StartAsync();
        f.Map("map-2").Activity.LatestStats = new RenderStatsSnapshot(12.5, null, 7, [], 3, DateTimeOffset.UnixEpoch);

        var two = Json(await f.CallAsync("get_render_stats", Args(("map", "map-2")), Ct));
        Assert.True(two["hasData"]!.GetValue<bool>());
        Assert.Equal("map-2", two["map"]!.GetValue<string>());
        Assert.Equal(12.5, two["frameDurationMs"]!.GetValue<double>());

        var all = Json(await f.CallAsync("get_render_stats", null, Ct));
        Assert.False(all["hasData"]!.GetValue<bool>());
        Assert.Null(all["map"]);
    }

    private static void DrawWarnings(TestMap map) =>
        map.Session.SetDataset(
            new MapDataset(new MapDatasetId("warnings"), "warnings", new DatasetMetadata { Spec = WarningSpec }),
            minimumDisplayScale: null,
            maximumDisplayScale: null);

    private static readonly SpecRef WarningSpec = new("S-124", new SpecVersion(2, 0, 0));

    private static IDatasetCatalog WarningCatalog()
    {
        var dataset = new LoadedDataset(
            new DatasetId("warnings"),
            WarningSpec,
            new BoundingBox(-1, -1, 1, 1),
            TimeRange: null,
            new S124DatasetData(new S124Dataset
            {
                Features =
                [
                    new S124Feature
                    {
                        Id = "W1",
                        FeatureType = "NavwarnPart",
                        GeometryType = S100GeometryType.Point,
                        Points = [new GeoPosition(0.0, 0.0)],
                        Attributes = ReadOnlyDictionary<string, string>.Empty,
                        ComplexAttributes = [],
                    },
                ],
                InformationTypes = [],
            }));
        return new SingleDatasetCatalog(dataset);
    }

    private sealed class SingleDatasetCatalog(LoadedDataset dataset) : IDatasetCatalog
    {
        public IReadOnlyList<LoadedDataset> Datasets => [dataset];

        public event EventHandler<DatasetCatalogChangedEventArgs>? Changed { add { } remove { } }
    }
}
