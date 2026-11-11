using System.Globalization;
using System.Text.Json.Nodes;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using ModelContextProtocol.Protocol;
using static EncDotNet.S100.TestSupport.NetCdfFixtures;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>
/// Issue #1071: NetCDF-CF files loaded through the file catalog (as
/// <c>s100 mcp serve</c> loads them) answer <c>sample_coverage</c> and
/// <c>sample_coverage_along</c> with wave heights and winds and their units,
/// with no MCP change.
/// </summary>
public sealed class NetCdfSampleCoverageTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("netcdf-mcp-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private FileDatasetCatalog Catalog() => FileDatasetCatalog.Build(
        [
            new FileDatasetInput(new DatasetId("waves"), "NetCDF", WriteWaveNetCdf4(Path.Combine(_dir, "waves.nc"))),
            new FileDatasetInput(new DatasetId("ww3"), "NetCDF", WriteWaveNetCdf3(Path.Combine(_dir, "ww3.nc"))),
            new FileDatasetInput(new DatasetId("wind"), "NetCDF", WriteWindNetCdf4(Path.Combine(_dir, "wind.nc"))),
        ],
        new ProjNetCrsTransformFactory());

    [Fact]
    public async Task Sample_coverage_returns_the_wave_height_with_its_unit()
    {
        var catalog = Catalog();
        Assert.Empty(catalog.Warnings);
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);
        var grid = ChartGrid;

        var result = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "NetCDF",
            ["latitude"] = grid.South + 5 * grid.Spacing,
            ["longitude"] = grid.West + 6 * grid.Spacing,
            ["time"] = "2026-10-25T03:00:00Z",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, Text(result));
        var payload = Json(result);
        Assert.Equal("in_range", payload["timeStatus"]!.GetValue<string>());
        var value = payload["value"]!;
        Assert.Equal("fields", value["$kind"]!.GetValue<string>());
        Assert.StartsWith("2026-10-25T03:00:00", value["sampleTime"]!.GetValue<string>());
        var height = value["fields"]!.AsArray().Single(f => f!["name"]!.GetValue<string>() == "wave.hs")!;
        Assert.Equal("significant_wave_height", height["quantity"]!.GetValue<string>());
        Assert.Equal("m", height["unit"]!.GetValue<string>());
        Assert.Equal(WaveHeight(5, 6, 1), height["value"]!.GetValue<double>(), 3);
    }

    [Fact]
    public async Task A_wind_only_catalog_samples_the_derived_wind()
    {
        var catalog = FileDatasetCatalog.Build(
            [new FileDatasetInput(new DatasetId("wind"), "NetCDF", WriteWindNetCdf4(Path.Combine(_dir, "wind.nc")))],
            new ProjNetCrsTransformFactory());
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);
        var grid = ChartWindGrid;

        var result = await client.CallToolAsync("sample_coverage_along", new Dictionary<string, object?>
        {
            ["spec"] = "NetCDF",
            ["polyline"] = string.Create(CultureInfo.InvariantCulture, $$"""{"vertices":[[{{grid.South + 3 * grid.Spacing}},{{grid.West + 2 * grid.Spacing}}],[{{grid.South}},{{grid.West}}],[0.0,0.0]]}"""),
            ["time"] = "2026-10-25T06:00:00Z",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, Text(result));
        var samples = Json(result)["samples"]!.AsArray();
        Assert.Equal(3, samples.Count);

        var first = samples[0]!["result"]!["value"]!["fields"]!.AsArray().ToDictionary(f => f!["name"]!.GetValue<string>());
        Assert.Equal(("wind_speed", "kn"), (first["wind.speed.kn"]!["quantity"]!.GetValue<string>(), first["wind.speed.kn"]!["unit"]!.GetValue<string>()));
        Assert.Equal(Wind(3, 2, 2, grid.Columns).Knots, first["wind.speed.kn"]!["value"]!.GetValue<double>(), 3);
        Assert.Equal(("wind_from_direction", "deg"), (first["wind.from_direction"]!["quantity"]!.GetValue<string>(), first["wind.from_direction"]!["unit"]!.GetValue<string>()));
        Assert.Equal(Wind(3, 2, 2, grid.Columns).FromDirection, first["wind.from_direction"]!["value"]!.GetValue<double>(), 2);
        Assert.Equal(("eastward_wind", "m/s"), (first["wind.u"]!["quantity"]!.GetValue<string>(), first["wind.u"]!["unit"]!.GetValue<string>()));

        Assert.NotNull(samples[1]!["result"]);
        Assert.Equal("out_of_bounds", samples[2]!["error"]!["code"]!.GetValue<string>());
    }

    private static JsonObject Json(CallToolResult result) =>
        JsonNode.Parse(Text(result))?.AsObject() ?? throw new InvalidOperationException("Tool result text was not valid JSON.");

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
}
