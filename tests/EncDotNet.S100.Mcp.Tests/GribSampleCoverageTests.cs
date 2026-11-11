using System.Globalization;
using System.Text.Json.Nodes;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Quantities;
using ModelContextProtocol.Protocol;
using static EncDotNet.S100.TestSupport.GribFixtures;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>
/// Issue #1074: the NOAA GRIB2 fixtures loaded through the file catalog (as
/// <c>s100 mcp serve</c> loads them) answer <c>sample_coverage</c> and
/// <c>sample_coverage_along</c> with ecCodes' wave heights and winds and their
/// units, with no MCP change.
/// </summary>
public sealed class GribSampleCoverageTests
{
    [Fact]
    public async Task Sample_coverage_returns_the_wave_height_with_its_unit()
    {
        var catalog = FileDatasetCatalog.Build(
            [new FileDatasetInput(new DatasetId("gfswave"), "GRIB2", Fixture(WaveFilter))],
            new ProjNetCrsTransformFactory());
        Assert.Empty(catalog.Warnings);
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);
        var (latitude, longitude) = CropPoint(566);

        var result = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "GRIB2",
            ["latitude"] = latitude,
            ["longitude"] = longitude,
            ["time"] = "2026-10-25T03:00:00Z",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, Text(result));
        var payload = Json(result);
        Assert.Equal("in_range", payload["timeStatus"]!.GetValue<string>());
        var value = payload["value"]!;
        Assert.Equal("fields", value["$kind"]!.GetValue<string>());
        Assert.StartsWith("2026-10-25T03:00:00", value["sampleTime"]!.GetValue<string>());
        var fields = value["fields"]!.AsArray().ToDictionary(f => f!["name"]!.GetValue<string>());
        Assert.Equal("significant_wave_height", fields["wave.hs"]!["quantity"]!.GetValue<string>());
        Assert.Equal("m", fields["wave.hs"]!["unit"]!.GetValue<string>());
        Assert.Equal(FilterAt566.Height[1], fields["wave.hs"]!["value"]!.GetValue<double>(), 1e-4);
        Assert.Equal(FilterAt566.Period[1], fields["wave.period"]!["value"]!.GetValue<double>(), 1e-4);
        Assert.Equal(
            Speed.FromMetresPerSecond(FilterAt566.WindSpeed[1]).TotalKnots,
            fields["wind.speed.kn"]!["value"]!.GetValue<double>(),
            1e-3);
    }

    [Fact]
    public async Task Sample_coverage_along_reads_the_GFS_wind_and_reports_points_outside()
    {
        var catalog = FileDatasetCatalog.Build(
            [new FileDatasetInput(new DatasetId("gfs"), "GRIB2", Fixture(Wind10m))],
            new ProjNetCrsTransformFactory());
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);
        var (latitude, longitude) = CropPoint(566);

        var result = await client.CallToolAsync("sample_coverage_along", new Dictionary<string, object?>
        {
            ["spec"] = "GRIB2",
            ["polyline"] = string.Create(CultureInfo.InvariantCulture, $$"""{"vertices":[[{{latitude}},{{longitude}}],[{{CropSouth}},{{CropEast}}],[0.0,0.0]]}"""),
            ["time"] = "2026-10-25T06:00:00Z",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, Text(result));
        var samples = Json(result)["samples"]!.AsArray();
        Assert.Equal(3, samples.Count);

        var first = samples[0]!["result"]!["value"]!["fields"]!.AsArray().ToDictionary(f => f!["name"]!.GetValue<string>());
        Assert.Equal(("eastward_wind", "m/s"), (first["wind.u"]!["quantity"]!.GetValue<string>(), first["wind.u"]!["unit"]!.GetValue<string>()));
        Assert.Equal(Wind10mAt566.U[2], first["wind.u"]!["value"]!.GetValue<double>(), 1e-4);
        Assert.Equal(Wind10mAt566.V[2], first["wind.v"]!["value"]!.GetValue<double>(), 1e-4);
        Assert.Equal(("wind_speed", "kn"), (first["wind.speed.kn"]!["quantity"]!.GetValue<string>(), first["wind.speed.kn"]!["unit"]!.GetValue<string>()));
        Assert.Equal(("wind_from_direction", "deg"), (first["wind.from_direction"]!["quantity"]!.GetValue<string>(), first["wind.from_direction"]!["unit"]!.GetValue<string>()));
        Assert.NotNull(samples[1]!["result"]);
        Assert.Equal("out_of_bounds", samples[2]!["error"]!["code"]!.GetValue<string>());
    }

    private static JsonObject Json(CallToolResult result) =>
        JsonNode.Parse(Text(result))?.AsObject() ?? throw new InvalidOperationException("Tool result text was not valid JSON.");

    private static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";
}
