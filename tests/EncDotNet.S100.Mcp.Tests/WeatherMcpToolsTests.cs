using System.Text.Json.Nodes;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.TestSupport;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>
/// Issue #1076: over MCP, the weather formats are listed by <c>list_specs</c>
/// as not S-100, with their portrayal basis and the tools that work on them;
/// <c>list_time_steps</c> lists GRIB2, NetCDF and CAP steps; the NetCDF grid
/// answers <c>sample_coverage_along</c>; and CAP warnings, which have time
/// steps but no coverage, answer <c>identify_features</c> and
/// <c>describe_feature</c> rather than <c>sample_coverage</c>.
/// </summary>
public sealed class WeatherMcpToolsTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("weather-mcp-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private FileDatasetCatalog Catalog() => FileDatasetCatalog.Build(
        [
            new FileDatasetInput(new DatasetId("gfswave"), "GRIB2", GribFixtures.Fixture(GribFixtures.WaveFilter)),
            new FileDatasetInput(new DatasetId("waves"), "NetCDF", NetCdfFixtures.WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"))),
            new FileDatasetInput(new DatasetId("circle"), "CAP", CapFixtures.Fixture(CapFixtures.Circle)),
        ],
        new ProjNetCrsTransformFactory());

    [Fact]
    public async Task List_specs_lists_the_weather_formats_as_not_S100_with_their_tools()
    {
        var catalog = Catalog();
        Assert.Empty(catalog.Warnings);
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("list_specs", new Dictionary<string, object?>(), cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, Text(result));
        var specs = Json(result)["specs"]!.AsArray().ToDictionary(s => s!["name"]!.GetValue<string>());
        foreach (var (name, basis) in new[] { ("GRIB2", "GRIB2"), ("NetCDF", "NetCDF-CF"), ("CAP", "CAP 1.2") })
        {
            var spec = specs[name]!;
            Assert.False(spec["isS100"]!.GetValue<bool>(), name);
            Assert.Equal(1, spec["loadedDatasetCount"]!.GetValue<int>());
            Assert.Equal($"SoundCharts portrayal: {basis} defines no symbology", spec["portrayalNote"]!.GetValue<string>());
            Assert.True(spec["capabilities"]!["canListTimeSteps"]!.GetValue<bool>(), name);
        }

        Assert.True(specs["GRIB2"]!["capabilities"]!["canSampleCoverage"]!.GetValue<bool>());
        Assert.True(specs["NetCDF"]!["capabilities"]!["canSampleCoverage"]!.GetValue<bool>());
        Assert.False(specs["GRIB2"]!["capabilities"]!["canQueryFeatures"]!.GetValue<bool>());
        var cap = specs["CAP"]!["capabilities"]!;
        Assert.False(cap["canSampleCoverage"]!.GetValue<bool>());
        Assert.True(cap["canQueryFeatures"]!.GetValue<bool>());
        Assert.True(cap["canDescribeFeature"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("gfswave", "GRIB2", new[] { "2026-10-25T00:00:00", "2026-10-25T03:00:00", "2026-10-25T06:00:00" })]
    [InlineData("waves", "NetCDF", new[] { "2026-10-25T00:00:00", "2026-10-25T03:00:00", "2026-10-25T06:00:00" })]
    // The warnings start at 15:00 and end at 18:00: the steps are the start
    // and the tick after the end.
    [InlineData("circle", "CAP", new[] { "2026-08-28T15:00:00", "2026-08-28T18:00:00.0000001" })]
    public async Task List_time_steps_lists_a_weather_datasets_steps(string id, string spec, string[] starts)
    {
        await using var server = await McpTestHelpers.StartServerAsync(Catalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("list_time_steps", new Dictionary<string, object?>
        {
            ["datasetId"] = id,
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, Text(result));
        var payload = Json(result);
        Assert.Equal(spec, payload["spec"]!["name"]!.GetValue<string>());
        var times = payload["times"]!.AsArray().Select(t => t!.GetValue<string>()).ToArray();
        Assert.Equal(starts.Length, times.Length);
        for (int i = 0; i < starts.Length; i++)
            Assert.StartsWith(starts[i], times[i]);
        Assert.Equal(starts[0], payload["firstTime"]!.GetValue<string>()[..starts[0].Length]);
    }

    [Fact]
    public async Task Sample_coverage_along_reads_the_NetCDF_waves_at_each_vertex()
    {
        await using var server = await McpTestHelpers.StartServerAsync(Catalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);
        var grid = NetCdfFixtures.ChartGrid;
        double Lat(int row) => grid.South + row * grid.Spacing;
        double Lon(int column) => grid.West + column * grid.Spacing;

        var result = await client.CallToolAsync("sample_coverage_along", new Dictionary<string, object?>
        {
            ["spec"] = "NetCDF",
            ["polyline"] = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $$"""{"vertices":[[{{Lat(2)}},{{Lon(3)}}],[{{Lat(10)}},{{Lon(12)}}],[0.0,0.0]]}"""),
            ["time"] = "2026-10-25T03:00:00Z",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, Text(result));
        var samples = Json(result)["samples"]!.AsArray();
        Assert.Equal(3, samples.Count);
        Assert.Equal(NetCdfFixtures.WaveHeight(2, 3, 1), Height(samples[0]!), 3);
        Assert.Equal(NetCdfFixtures.WaveHeight(10, 12, 1), Height(samples[1]!), 3);
        Assert.Equal("out_of_bounds", samples[2]!["error"]!["code"]!.GetValue<string>());

        static double Height(JsonNode sample) => sample["result"]!["value"]!["fields"]!.AsArray()
            .Single(f => f!["name"]!.GetValue<string>() == "wave.hs")!["value"]!.GetValue<double>();
    }

    [Fact]
    public async Task Sample_coverage_on_CAP_says_the_spec_has_no_coverage()
    {
        await using var server = await McpTestHelpers.StartServerAsync(Catalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "CAP",
            ["latitude"] = 38.5,
            ["longitude"] = -74.5,
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError ?? false, Text(result));
        Assert.Contains("spec_not_supported_for_tool", Text(result));
    }

    [Fact]
    public async Task Identify_features_and_describe_feature_read_a_CAP_warning_area()
    {
        await using var server = await McpTestHelpers.StartServerAsync(Catalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var identified = await client.CallToolAsync("identify_features", new Dictionary<string, object?>
        {
            ["latitude"] = 38.5,
            ["longitude"] = -74.5,
            ["spec"] = "CAP",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(identified.IsError ?? false, Text(identified));
        var feature = Assert.Single(Json(identified)["features"]!.AsArray())!;
        Assert.Equal("CapWarningArea", feature["featureType"]!.GetValue<string>());
        Assert.Equal("inside", feature["containment"]!.GetValue<string>());

        var described = await client.CallToolAsync("describe_feature", new Dictionary<string, object?>
        {
            ["datasetId"] = "circle",
            ["featureId"] = feature["featureId"]!.GetValue<string>(),
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(described.IsError ?? false, Text(described));
        var payload = Json(described);
        Assert.Equal("SoundCharts portrayal: CAP 1.2 defines no symbology", payload["portrayalNote"]!.GetValue<string>());
        var attributes = payload["attributes"]!["attributes"]!;
        Assert.Equal("Hazardous Seas Warning", attributes["event"]!.GetValue<string>());
        Assert.Equal("Extreme", attributes["severity"]!.GetValue<string>());
        Assert.Equal(
            "Weather warning (CAP), not an S-100 product and not for navigation.",
            attributes["notice"]!.GetValue<string>());
    }

    private static JsonObject Json(CallToolResult result) =>
        JsonNode.Parse(Text(result))?.AsObject() ?? throw new InvalidOperationException("Tool result text was not valid JSON.");

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
}
