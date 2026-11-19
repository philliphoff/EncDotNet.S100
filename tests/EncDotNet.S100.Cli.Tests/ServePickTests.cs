using System.Net;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Cli.Web;
using EncDotNet.S100.Cli.Web.Pick;
using EncDotNet.S100.Datasets.Pipelines;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EncDotNet.S100.Cli.Tests;

/// <summary>
/// Features at a point on the <c>tiles serve</c> web page (issue #1581): the
/// decoded query result, the card's content (D23), the highlight geometry
/// (D24) and the hash's <c>pick=</c>.
/// </summary>
public sealed class ServePickTests
{
    // Off Nantucket.
    private static string S57 => Path.Combine(AppContext.BaseDirectory, "TestData", "US5MA1BO.000");

    [Fact]
    public async Task A_query_decodes_features_with_the_catalogue()
    {
        Assert.SkipUnless(File.Exists(S57), "S-57 fixture not present.");
        var settings = new TilesServeCommand.Settings { Layers = [S57], MinZoom = 13, MaxZoom = 13 };
        Assert.True(settings.Validate().Successful);
        Assert.Equal(0, TilesServeCommand.OpenRendered(settings, out var rendered));
        using var source = rendered!;
        var (west, south, east, north) = source.Layout.Bounds;

        var result = await source.FeatureQuery!.QueryAsync(
            (south + north) / 2, (west + east) / 2, 200, ServePick.MaxResults, time: null, TestContext.Current.CancellationToken);
        Assert.NotEmpty(result.Features);

        // Every feature is read again with its catalogue: S-57 decodes with S-101's.
        Assert.All(result.Features, f => Assert.NotNull(f.Information));
        var report = ServePick.Report(result, []);
        Assert.Equal(result.Features.Count, report.Features.Count);
        Assert.All(report.Features, f => Assert.Equal("S-57", f.Product));
        Assert.Contains(report.Features, f => f.Title.Contains(' ', StringComparison.Ordinal)); // "Depth Area", not "DepthArea"
        Assert.Contains(report.Features.SelectMany(f => f.Rows), r => r.Name.Contains(' ', StringComparison.Ordinal));

        // The meta types (data coverage, quality of bathymetric data…) fold behind "Show N more", last.
        Assert.Contains(report.Features, f => f.Minor);
        Assert.Equal(report.Features.OrderBy(f => f.Minor).Select(f => f.Index), report.Features.Select(f => f.Index));
        Assert.Equal(Enumerable.Range(0, report.Features.Count), report.Features.Select(f => f.Index));

        // One highlight geometry per feature, in the card's order (D24).
        var geometries = ServePick.Geometries(result);
        Assert.Equal(report.Features.Count, geometries.Count);
        Assert.Contains(geometries, g => (string?)g?["geometry"]?["type"] == "MultiPolygon");
        var first = report.Features[0];
        var expected = first.Geometry switch { "point" => "MultiPoint", "curve" => "MultiLineString", _ => "MultiPolygon" };
        Assert.Equal(expected, (string?)geometries[0]?["geometry"]?["type"]);
    }

    [Theory]
    [InlineData(50.7705, -1.198, "50°46.23′N 001°11.88′W")]
    [InlineData(-33.85, 151.2, "33°51.00′S 151°12.00′E")]
    [InlineData(10.99999999, 0, "11°00.00′N 000°00.00′E")]
    [InlineData(0, 181, "00°00.00′N 179°00.00′W")]
    public void The_position_is_in_degrees_and_minutes(double latitude, double longitude, string expected) =>
        Assert.Equal(expected, ServePick.Position(latitude, longitude));

    [Fact]
    public void The_pick_goes_in_the_hash()
    {
        var value = ServePick.FormatHash(50.7705, -1.198);
        Assert.Equal("50.7705,-1.198", value);
        Assert.Equal((50.7705, -1.198), ServePick.ParseHash(value));

        // It survives the hash's own formatting (commas kept).
        var hash = "#13/50.775/-1.235&" + ServeHash.FormatParameters([new("pick", value)]);
        Assert.Equal(value, ServeHash.Parse(hash).Parameters["pick"]);

        foreach (var bad in new[] { null, "", "50.7", "a,b", "91,0", "0,600", "1,2,3" })
            Assert.Null(ServePick.ParseHash(bad));
    }

    [Fact]
    public void A_name_is_the_identity_and_is_not_repeated()
    {
        PickAttribute[] buoy =
        [
            Leaf("categoryOfLateralMark", "Category of Lateral Mark", "1", "Port-Hand Lateral Mark"),
            new() { Code = "featureName", Name = "Feature Name", RawValue = "", Children = [Leaf("name", "Name", "North East Ryde Middle")] },
        ];

        Assert.Equal(("North East Ryde Middle", true), ServePick.Identity("BuoyLateral", buoy));
    }

    [Fact]
    public void A_light_is_described_in_chart_abbreviations()
    {
        PickAttribute[] light =
        [
            Leaf("colour", "Colour", "3", "Red"),
            new()
            {
                Code = "rhythmOfLight", Name = "Rhythm of Light", RawValue = "",
                Children = [Leaf("lightCharacteristic", null, "2"), Leaf("signalGroup", null, "(2)"), Leaf("signalPeriod", null, "10")],
            },
        ];

        Assert.Equal(("Fl(2) R 10s", false), ServePick.Identity("LightAllAround", light));
        Assert.Equal("Q W", ServePick.LightDescription([Leaf("LITCHR", null, "4"), Leaf("COLOUR", null, "1"), Leaf("SIGGRP", null, "()")]));
        Assert.Null(ServePick.LightDescription([Leaf("COLOUR", null, "1")]));
    }

    [Fact]
    public void Depths_are_the_identity_of_depth_features()
    {
        Assert.Equal(("10 – 15 m", false), ServePick.Identity("DepthArea", [Leaf("depthRangeMinimumValue", null, "10.0"), Leaf("depthRangeMaximumValue", null, "15")]));
        Assert.Equal(("From 2 m", false), ServePick.Identity("DepthArea", [Leaf("DRVAL1", null, "2")]));
        Assert.Equal(("5.5 m", false), ServePick.Identity("DepthContour", [Leaf("valueOfDepthContour", null, "5.5")]));
        Assert.Equal((null, false), ServePick.Identity("Landmark", [Leaf("colour", null, "1")]));
    }

    [Fact]
    public async Task The_card_shows_coverage_first_one_feature_open_and_folds_the_minor_ones()
    {
        var report = new PickReport(
            50.7705, -1.198, "50°46.23′N 001°11.88′W",
            [new PickCoverage("S-111", "Surface current · Forecast", "0.3 kn", 110, "at 11:00 UTC")],
            [
                new PickFeature(0, "Lateral Buoy", "North East Ryde Middle", "S-101", "101GB0040242B", "point", [new PickRow("Colour", "Red", [])], false),
                new PickFeature(1, "Depth Area", "10 – 15 m", "S-101", "101GB0040242B", "surface", [new PickRow("Depth Range Minimum Value", "10 m", [])], false),
                new PickFeature(2, "Caution Area", null, "S-101", "101GB0040242B", "surface", [], true),
                new PickFeature(3, "Quality of Bathymetric Data", null, "S-101", "101GB0040242B", "surface", [], true),
            ],
            []);

        var html = await RenderAsync(report, expanded: 0, showMinor: false);

        Assert.Contains("Object information", html, StringComparison.Ordinal);
        Assert.Contains("50°46.23′N 001°11.88′W", html, StringComparison.Ordinal);
        Assert.Contains("4 features", html, StringComparison.Ordinal);
        Assert.True(html.IndexOf("Surface current", StringComparison.Ordinal) < html.IndexOf("Lateral Buoy", StringComparison.Ordinal));
        Assert.Contains("0.3 kn", html, StringComparison.Ordinal);
        Assert.Contains("110° T", html, StringComparison.Ordinal);
        Assert.Contains("rotate(110deg)", html, StringComparison.Ordinal);

        // Only the first feature is open: its rows and provenance show, the second's don't.
        Assert.Contains("data-pick-index=\"0\" data-expanded=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("data-pick-index=\"1\" data-expanded=\"false\"", html, StringComparison.Ordinal);
        Assert.Matches(@"<dt>Colour</dt>\s*<dd>Red</dd>", html);
        Assert.DoesNotContain("Depth Range Minimum Value", html, StringComparison.Ordinal);
        Assert.Matches(@"<span class=""tag"">S-101</span>\s*<span class=""mono"">101GB0040242B</span>\s*<span>· point</span>", html);

        // The minor ones are folded.
        Assert.Contains("Show 2 more: caution area, quality of bathymetric data", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-pick-index=\"2\"", html, StringComparison.Ordinal);

        var unfolded = await RenderAsync(report, expanded: -1, showMinor: true);
        Assert.Contains("data-pick-index=\"3\"", unfolded, StringComparison.Ordinal);
        Assert.DoesNotContain("Show 2 more", unfolded, StringComparison.Ordinal);
        Assert.DoesNotContain("data-expanded=\"true\"", unfolded, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_card_says_when_it_is_looking_and_when_nothing_is_there()
    {
        var looking = await RenderAsync(null, 0, false);
        Assert.Contains("Looking…", looking, StringComparison.Ordinal);

        var empty = await RenderAsync(new PickReport(0, 0, "00°00.00′N 000°00.00′E", [], [], []), -1, false);
        Assert.Contains("No features", empty, StringComparison.Ordinal);
        Assert.Contains("Nothing here.", empty, StringComparison.Ordinal);
    }

    private static PickAttribute Leaf(string code, string? name, string raw, string? display = null) =>
        new() { Code = code, Name = name, RawValue = raw, DisplayValue = display };

    private static async Task<string> RenderAsync(PickReport? report, int expanded, bool showMinor)
    {
        await using var services = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance).BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<ObjectInformation>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(ObjectInformation.Position)] = report?.Position ?? "50°46.23′N 001°11.88′W",
                [nameof(ObjectInformation.Report)] = report,
                [nameof(ObjectInformation.Expanded)] = expanded,
                [nameof(ObjectInformation.ShowMinor)] = showMinor,
            }));
            return output.ToHtmlString();
        });
        return WebUtility.HtmlDecode(html);
    }
}
