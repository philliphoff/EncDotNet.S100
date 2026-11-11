using System.Text.Json.Nodes;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>
/// A product the MCP layer has never heard of — a fake weather processor that
/// implements only <see cref="ICoverageSampler"/> — is sampled through
/// <c>sample_coverage</c> and <c>sample_coverage_along</c>, with its fields'
/// quantities and units in the result (issue #1063).
/// </summary>
public class CoverageSamplerRoundTripTests
{
    // S-412 (weather overlay) has no sampler of its own in the library, so
    // the fake is the only thing that makes it samplable.
    private static readonly SpecRef WeatherSpec = new("S-412", default);

    private static LoadedDataset WaveDataset(string id = "gfs-wave") => new(
        new DatasetId(id),
        WeatherSpec,
        LoadedDatasetFactory.Box(9.5, 19.5, 12.5, 22.5),
        TimeRange: null,
        new WaveData(new FakeWaveProcessor()));

    [Fact]
    public async Task Sample_coverage_returns_the_fake_processors_fields_with_units()
    {
        var catalog = McpTestHelpers.NewCatalog(WaveDataset());
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "S-412",
            ["latitude"] = 11.1,
            ["longitude"] = 21.9,
            ["time"] = "2026-01-01T05:00:00Z",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, $"sample_coverage returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        Assert.Equal("gfs-wave", payload["datasetId"]!.GetValue<string>());
        Assert.Equal("in_range", payload["timeStatus"]!.GetValue<string>());

        var value = payload["value"]!;
        Assert.Equal("fields", value["$kind"]!.GetValue<string>());
        Assert.StartsWith("2026-01-01T06:00:00", value["sampleTime"]!.GetValue<string>());
        Assert.Equal(1, value["row"]!.GetValue<int>());
        Assert.Equal(2, value["column"]!.GetValue<int>());

        var fields = value["fields"]!.AsArray();
        Assert.Equal(2, fields.Count);
        Assert.Equal("significantWaveHeight", fields[0]!["name"]!.GetValue<string>());
        Assert.Equal("significant_wave_height", fields[0]!["quantity"]!.GetValue<string>());
        Assert.Equal("m", fields[0]!["unit"]!.GetValue<string>());
        Assert.Equal(2.5, fields[0]!["value"]!.GetValue<double>());
        Assert.Equal("wind_speed", fields[1]!["quantity"]!.GetValue<string>());
        Assert.Equal("kn", fields[1]!["unit"]!.GetValue<string>());
        Assert.Equal(11.0, fields[1]!["value"]!.GetValue<double>());
    }

    [Fact]
    public async Task Sample_coverage_applies_the_shared_time_handling()
    {
        var catalog = McpTestHelpers.NewCatalog(WaveDataset());
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        // Past the last step by more than one interval: out of range.
        var late = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "S-412",
            ["latitude"] = 10.0,
            ["longitude"] = 20.0,
            ["time"] = "2026-01-02T00:00:00Z",
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.True(late.IsError ?? false, "Expected time_out_of_range.");
        var error = ParseSingleJson(late);
        Assert.Equal("time_out_of_range", error["code"]!.GetValue<string>());
        Assert.Equal("gfs-wave", error["details"]!["datasetId"]!.GetValue<string>());
        Assert.StartsWith("2026-01-01T06:00:00", error["details"]!["nearestStep"]!.GetValue<string>());

        // A range over both steps: one series entry per step.
        var range = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "S-412",
            ["latitude"] = 10.0,
            ["longitude"] = 20.0,
            ["times"] = """{"kind":"range","from":"2026-01-01T00:00:00Z","to":"2026-01-01T06:00:00Z"}""",
        }, cancellationToken: TestContext.Current.CancellationToken);
        Assert.False(range.IsError ?? false, $"sample_coverage returned an error: {DumpText(range)}");
        var series = ParseSingleJson(range)["series"]!.AsArray();
        Assert.Equal(2, series.Count);
        Assert.Equal(1.5, series[0]!["value"]!["fields"]![0]!["value"]!.GetValue<double>());
        Assert.Equal(2.5, series[1]!["value"]!["fields"]![0]!["value"]!.GetValue<double>());
    }

    [Fact]
    public async Task Sample_coverage_along_samples_each_vertex_of_the_fake_processor()
    {
        var catalog = McpTestHelpers.NewCatalog(WaveDataset());
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("sample_coverage_along", new Dictionary<string, object?>
        {
            ["spec"] = "S-412",
            ["polyline"] = """{"vertices":[[10.0,20.0],[12.0,22.0],[40.0,40.0]]}""",
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false, $"sample_coverage_along returned an error: {DumpText(result)}");
        var samples = ParseSingleJson(result)["samples"]!.AsArray();
        Assert.Equal(3, samples.Count);
        Assert.Equal(10.0, samples[0]!["result"]!["value"]!["fields"]![1]!["value"]!.GetValue<double>());
        Assert.Equal(12.0, samples[1]!["result"]!["value"]!["fields"]![1]!["value"]!.GetValue<double>());
        Assert.Null(samples[2]!["result"]);
        Assert.Equal("out_of_bounds", samples[2]!["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task A_spec_with_no_sampler_loaded_is_not_supported()
    {
        var catalog = McpTestHelpers.NewCatalog(WaveDataset());
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "S-413",
            ["latitude"] = 10.0,
            ["longitude"] = 20.0,
        }, cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError ?? false);
        Assert.Equal("spec_not_supported_for_tool", ParseSingleJson(result)["code"]!.GetValue<string>());
    }

    [Fact]
    public void A_single_sampler_samples_at_a_point_and_time_without_a_catalog()
    {
        var sampler = new FakeWaveProcessor();
        var query = new CoverageSampleQuery(12.0, 20.0, new EncDotNet.S100.Crs.ProjNet.ProjNetCrsTransformFactory());

        var early = Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(
            sampler.SampleAt(query, new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero), nearestWhenOutOfRange: true));
        Assert.Equal(SampleTimeStatus.BeforeStart, early.TimeStatus);
        Assert.Equal(1.5, early.Value.GetFields()[0].Value);

        var along = sampler.SampleAlong([(10.0, 20.0), (30.0, 30.0)], query.Transforms);
        Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.Sampled>(along[0]);
        Assert.IsType<CoverageSampleOutcome<ICoverageSampler>.OutsideGrid>(along[1]);
    }

    [Fact]
    public void Typed_samples_describe_their_values_with_quantity_and_unit()
    {
        var fields = new SurfaceCurrentSample(1.0, 1.94, 90, DateTime.UnixEpoch, null, 0, 0, 0, 0).GetFields();
        Assert.Equal(("surfaceCurrentSpeed", "current_speed", "kn", 1.94), (fields[0].Name, fields[0].Quantity, fields[0].Unit, fields[0].Value));
        Assert.Equal(("current_direction", "deg"), (fields[1].Quantity, fields[1].Unit));

        var depth = new DepthSample(12.5, null).GetFields();
        Assert.Equal(("depth", "m", 12.5), (depth[0].Quantity, depth[0].Unit, depth[0].Value));
    }

    private static JsonObject ParseSingleJson(CallToolResult result) =>
        JsonNode.Parse(DumpText(result))?.AsObject()
            ?? throw new InvalidOperationException("Tool result text was not valid JSON.");

    private static string DumpText(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text ?? "";

    /// <summary>
    /// A stand-in for a weather processor: a 3 × 3 one-degree grid (10..12°N,
    /// 20..22°E) with two six-hourly steps of significant wave height (1.5 m,
    /// then 2.5 m) and wind speed (10 kn plus the row). It implements only
    /// <see cref="ICoverageSampler"/>.
    /// </summary>
    /// <summary>
    /// The fake product's catalog payload: a <see cref="ProductDatasetData"/>
    /// (the payload of a product with no built-in variant) that samples
    /// through its processor.
    /// </summary>
    private sealed record WaveData(FakeWaveProcessor Processor) : ProductDatasetData, ICoverageSampler
    {
        public override DatasetMetadata Metadata { get; } = new() { Spec = WeatherSpec };

        public CoverageSiteSearch FindSites(CoverageSampleQuery query) => Processor.FindSites(query);
    }

    private sealed class FakeWaveProcessor : ICoverageSampler
    {
        private static readonly DateTime[] Times =
        [
            new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new(2026, 1, 1, 6, 0, 0, DateTimeKind.Utc),
        ];

        public CoverageSiteSearch FindSites(CoverageSampleQuery query)
        {
            var row = (int)Math.Round(query.Latitude - 10.0);
            var col = (int)Math.Round(query.Longitude - 20.0);
            return row is < 0 or > 2 || col is < 0 or > 2
                ? CoverageSiteSearch.None with { OutsideGrid = true }
                : new CoverageSiteSearch([new Site(row, col)]);
        }

        private sealed class Site(int row, int col)
            : CoverageSampleSite(CoverageSiteKind.Grid, rank: 1.0, run: Times[0], CoverageStepWindow.FromSteps(Times))
        {
            public override IReadOnlyList<DateTime> Steps => Times;

            public override CoverageReading Read(DateTimeOffset? requestedTime) =>
                new CoverageReading.Value(Sample(CoverageSampling.SelectTimeStep(Times, requestedTime), requestedTime));

            public override CoverageReading ReadSteps(IReadOnlyList<CoverageStepRequest> steps) =>
                new CoverageReading.Series(
                    steps.Select(s => new CoverageTimedSample(Times[s.StepIndex], s.RequestedTime, Sample(s.StepIndex, s.RequestedTime))).ToList(),
                    Sample(steps[0].StepIndex, steps[0].RequestedTime));

            private CoverageFieldsSample Sample(int step, DateTimeOffset? requestedTime) => new(
                [
                    new SampledField("significantWaveHeight", "significant_wave_height", "m", 1.5 + step),
                    new SampledField("windSpeed", "wind_speed", "kn", 10 + row),
                ],
                Times[step],
                requestedTime,
                row,
                col,
                CellCentreLatitude: 10.0 + row,
                CellCentreLongitude: 20.0 + col);
        }
    }
}
