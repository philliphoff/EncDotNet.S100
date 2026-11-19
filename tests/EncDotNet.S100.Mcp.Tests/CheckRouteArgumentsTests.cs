using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Navigation;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>
/// The hosts' <c>check_route</c> takes every <see cref="CheckRouteRequest"/>
/// parameter: its schema lists each one, and each value sent reaches the
/// request <see cref="CheckRouteTool"/> builds its settings from.
/// </summary>
public class CheckRouteArgumentsTests
{
    private static readonly ParameterInfo[] RequestParameters = Parameters(typeof(CheckRouteRequest));

    // The argument groups (vessel, window, weather, limits, samples and
    // weather.cyclones): records whose own parameters are arguments too.
    private static bool IsGroup(Type type) =>
        type.IsClass && type != typeof(string) && type != typeof(GeoPolyline) && !typeof(IEnumerable).IsAssignableFrom(type);

    private static ParameterInfo[] Parameters(Type type) =>
        type.GetConstructors().Single(c => c.GetParameters().Length > 1).GetParameters();

    // Every leaf argument, by its path of parameters from the request.
    private static IEnumerable<ParameterInfo[]> Leaves(Type type, ParameterInfo[] prefix)
    {
        foreach (var p in Parameters(type))
        {
            var path = prefix.Append(p).ToArray();
            var inner = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
            if (IsGroup(inner))
            {
                foreach (var leaf in Leaves(inner, path))
                {
                    yield return leaf;
                }
            }
            else
            {
                yield return path;
            }
        }
    }

    private static readonly ParameterInfo[][] LeafArguments = Leaves(typeof(CheckRouteRequest), []).ToArray();

    private static string WireName(ParameterInfo p) => JsonNamingPolicy.CamelCase.ConvertName(p.Name!);

    [Fact]
    public void Schema_lists_every_request_parameter_with_its_description()
    {
        var tool = S100McpTools.Create(McpTestHelpers.NewCatalog()).Single(t => t.ProtocolTool.Name == CheckRouteTool.Name);
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");

        var names = properties.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(RequestParameters.Select(WireName).Order(StringComparer.Ordinal).ToArray(), names);
        foreach (var leaf in LeafArguments)
        {
            var node = properties;
            foreach (var (p, i) in leaf.Select((p, i) => (p, i)))
            {
                Assert.True(node.TryGetProperty(WireName(p), out var property), $"check_route's schema has no {string.Join('.', leaf.Select(WireName))}.");
                Assert.True(property.TryGetProperty("description", out var description) && description.GetString() is { Length: > 0 },
                    $"check_route's {string.Join('.', leaf.Take(i + 1).Select(WireName))} has no description.");
                if (i < leaf.Length - 1)
                {
                    node = property.GetProperty("properties");
                }
            }
        }

        Assert.Equal("string", properties.GetProperty("polyline").GetProperty("type").GetString());
        Assert.False(tool.ProtocolTool.InputSchema.TryGetProperty("required", out _));
    }

    [Fact]
    public void Every_argument_reaches_the_request()
    {
        // A value unlike each parameter's default, so a dropped argument shows.
        var arguments = new Dictionary<string, object?>();
        var expected = new List<(ParameterInfo[] Path, object Value)>();
        var groups = new Dictionary<string, JsonObject>();
        foreach (var leaf in LeafArguments)
        {
            var p = leaf[^1];
            var (wire, value) = SampleFor(p);
            Assert.NotEqual(p.DefaultValue, value);
            expected.Add((leaf, value));
            if (leaf.Length == 1)
            {
                arguments[WireName(p)] = JsonSerializer.SerializeToElement(wire);
                continue;
            }

            // Build the group objects, sent as objects on the wire.
            var top = WireName(leaf[0]);
            if (!groups.TryGetValue(top, out var group))
            {
                groups[top] = group = new JsonObject();
            }

            var node = group;
            foreach (var step in leaf[1..^1])
            {
                node = (node[WireName(step)] ??= new JsonObject()).AsObject();
            }

            node[WireName(p)] = JsonSerializer.SerializeToNode(wire);
        }

        foreach (var (name, group) in groups)
        {
            arguments[name] = JsonSerializer.SerializeToElement(group);
        }

        var request = S100McpServerToolFactory.ReadCheckRouteRequest(arguments);

        foreach (var (path, value) in expected)
        {
            object? actual = request;
            foreach (var p in path)
            {
                Assert.NotNull(actual);
                actual = actual.GetType().GetProperty(p.Name!)!.GetValue(actual);
            }

            switch (value)
            {
                case GeoPolyline polyline:
                    var read = Assert.IsType<GeoPolyline>(actual);
                    Assert.Equal(polyline.Vertices, read.Vertices);
                    Assert.Equal(polyline.CorridorWidthMeters, read.CorridorWidthMeters);
                    break;
                case IEnumerable list and not string:
                    Assert.Equal(list.Cast<object>(), Assert.IsAssignableFrom<IEnumerable>(actual).Cast<object>());
                    break;
                default:
                    Assert.Equal(value, actual);
                    break;
            }
        }
    }

    [Fact]
    public void A_group_may_also_be_json_text()
    {
        var request = S100McpServerToolFactory.ReadCheckRouteRequest(new Dictionary<string, object?>
        {
            ["vessel"] = JsonSerializer.SerializeToElement("""{"draftMeters":2.5}"""),
        });

        Assert.Equal(2.5, request.Vessel!.DraftMeters);
    }

    [Fact]
    public void Missing_arguments_keep_the_request_defaults()
    {
        var request = S100McpServerToolFactory.ReadCheckRouteRequest([]);

        Assert.Equal(new CheckRouteRequest(), request);
    }

    [Fact]
    public void Polyline_may_also_be_an_object()
    {
        var request = S100McpServerToolFactory.ReadCheckRouteRequest(new Dictionary<string, object?>
        {
            ["polyline"] = JsonSerializer.SerializeToElement(new { vertices = new[] { new[] { 1.0, 2.0 }, new[] { 1.5, 2.5 } } }),
        });

        Assert.Equal([new GeoPoint(1, 2), new GeoPoint(1.5, 2.5)], request.Polyline!.Vertices);
    }

    [Fact]
    public async Task Weather_settings_reach_the_headless_check()
    {
        // The GFS-Wave crop: about 20 kn and 2.54 m at file index 566 at f003.
        var catalog = FileDatasetCatalog.Build(
            [new FileDatasetInput(new DatasetId("gfswave"), "GRIB2", Fixture(WaveFilter))],
            new ProjNetCrsTransformFactory());
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);
        var (lat, lon) = CropPoint(566);
        var polyline = string.Create(CultureInfo.InvariantCulture, $$"""{"vertices":[[{{lat}},{{lon}}],[{{lat}},{{lon + 0.1}}]]}""");
        var departure = new DateTimeOffset(Run).AddHours(3);

        var without = await Report(client, new() { ["polyline"] = polyline, ["vessel"] = new Dictionary<string, object?> { ["speedKnots"] = 6.0 }, ["departure"] = departure });
        Assert.Equal(RouteCheckReasons.WeatherLimitsMissing, Check(without, "weather")["reason"]!.GetValue<string>());

        var with = await Report(client, new()
        {
            ["polyline"] = polyline,
            ["vessel"] = new Dictionary<string, object?> { ["speedKnots"] = 6.0 },
            ["departure"] = departure,
            ["weather"] = new Dictionary<string, object?> { ["maxWindKnots"] = 15.0, ["maxWaveHeightMeters"] = 2.0 },
        });
        Assert.Equal("ran", Check(with, "weather")["state"]!.GetValue<string>());
        Assert.Equal("above_limits", with["weather"]!["verdict"]!.GetValue<string>());
        Assert.Equal([15.0, 2.0], with["weather"]!["limits"]!.AsArray().Select(l => l!["value"]!.GetValue<double>()));
    }

    [Fact]
    public async Task Without_safety_settings_the_chart_check_says_depths_were_skipped()
    {
        await using var server = await McpTestHelpers.StartServerAsync(McpTestHelpers.NewCatalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var report = await Report(client, new() { ["polyline"] = Polyline });

        var chart = Check(report, "chart");
        Assert.Equal("partial", chart["state"]!.GetValue<string>());
        Assert.Equal(RouteCheckReasons.SafetySettingsMissing, chart["reason"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("limits", "trackClearanceMeters", -1.0)]
    [InlineData("limits", "nearbyDistanceMeters", -1.0)]
    [InlineData("limits", "berthRadiusMeters", -1.0)]
    [InlineData("limits", "areaKindsOff", new[] { "lagoon" })]
    [InlineData("weather", "maxWindKnots", 0.0)]
    [InlineData("weather", "maxWaveHeightMeters", 0.0)]
    [InlineData("weather.cyclones", "checkAgainstKnots", 40)]
    [InlineData("weather.cyclones", "mustReviewInside", "12")]
    [InlineData("vessel", "draftMeters", 0.0)]
    [InlineData("window", "stepMinutes", 0)]
    public async Task The_tool_validates_each_setting(string group, string name, object value)
    {
        await using var server = await McpTestHelpers.StartServerAsync(McpTestHelpers.NewCatalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        object? argument = new Dictionary<string, object?> { [name] = value };
        var steps = group.Split('.');
        foreach (var step in steps[1..].Reverse())
        {
            argument = new Dictionary<string, object?> { [step] = argument };
        }

        var result = await client.CallToolAsync(CheckRouteTool.Name, new Dictionary<string, object?> { ["polyline"] = Polyline, [steps[0]] = argument },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var error = JsonNode.Parse(Text(result))!;
        Assert.Equal("invalid_argument", error["code"]!.GetValue<string>());
        Assert.Equal($"{group}.{name}", error["details"]!["parameter"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_wrongly_typed_argument_is_invalid()
    {
        await using var server = await McpTestHelpers.StartServerAsync(McpTestHelpers.NewCatalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(CheckRouteTool.Name, new Dictionary<string, object?>
        {
            ["polyline"] = Polyline,
            ["vessel"] = new Dictionary<string, object?> { ["draftMeters"] = "deep" },
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        Assert.Equal("invalid_argument", JsonNode.Parse(Text(result))!["code"]!.GetValue<string>());
    }

    private const string Polyline = """{"vertices":[[0.005,0.005],[0.005,0.025]]}""";

    private static async Task<JsonObject> Report(McpClient client, Dictionary<string, object?> args)
    {
        var result = await client.CallToolAsync(CheckRouteTool.Name, args, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(result.IsError ?? false, Text(result));
        return JsonNode.Parse(Text(result))!.AsObject();
    }

    private static JsonObject Check(JsonObject report, string name) =>
        report["checks"]!.AsArray().Single(c => c!["name"]!.GetValue<string>() == name)!.AsObject();

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().Select(t => t.Text).FirstOrDefault() ?? "";

    // The value sent on the wire, and the value the request should hold.
    private static (object Wire, object Value) SampleFor(ParameterInfo p)
    {
        if (p.ParameterType == typeof(GeoPolyline))
        {
            var polyline = new GeoPolyline([new GeoPoint(47.6, -122.4), new GeoPoint(47.7, -122.5)], 120);
            return ("""{"vertices":[[47.6,-122.4],[47.7,-122.5]],"corridorWidthMeters":120}""", polyline);
        }

        // A distinct value per parameter, so two swapped arguments show too.
        var seed = Array.FindIndex(LeafArguments, leaf => leaf[^1] == p) + 1;
        var type = Nullable.GetUnderlyingType(p.ParameterType) ?? p.ParameterType;
        object value = type switch
        {
            _ when type == typeof(string) => p.Name switch
            {
                nameof(CycloneCheckArguments.MustReviewInside) => "64",
                _ => $"value-{seed}",
            },
            _ when type == typeof(double) => seed + 0.25,
            _ when type == typeof(int) => p.Name switch
            {
                nameof(CycloneCheckArguments.CheckAgainstKnots) => 64,
                _ => 1000 + seed,
            },
            _ when type == typeof(bool) => !(p.DefaultValue as bool? ?? false),
            _ when type == typeof(DateTimeOffset) => new DateTimeOffset(2026, 11, 1, seed, 0, 0, TimeSpan.Zero),
            _ when type == typeof(IReadOnlyList<string>) => new[] { "anchorage_area", "marine_farm" },
            _ => throw new InvalidOperationException(
                $"{p.Member.DeclaringType?.Name}.{p.Name} has a type ({p.ParameterType}) this test can't send yet; add it so the argument is checked."),
        };
        return (value, value);
    }
}
