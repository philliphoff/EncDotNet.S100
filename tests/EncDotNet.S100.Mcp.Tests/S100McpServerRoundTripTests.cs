using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Gml;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using EncDotNet.S100.Pipelines;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Mcp.Tests;

public class S100McpServerRoundTripTests
{
    [Fact]
    public async Task ListTools_returns_seven_tools_with_schemas()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            new[]
            {
                "describe_feature",
                "find_at",
                "list_datasets",
                "list_specs",
                "list_time_steps",
                "query_features",
                "sample_coverage",
                "sample_coverage_along",
            },
            names);
        foreach (var tool in tools)
        {
            // ProtocolTool exposes the JSON schema; ensure it is non-empty.
            var schema = tool.ProtocolTool.InputSchema;
            Assert.True(schema.ValueKind == JsonValueKind.Object && schema.GetRawText().Length > 2,
                $"Tool {tool.Name} has no input schema.");
        }
    }

    [Fact]
    public async Task ListDatasets_round_trip_returns_summaries()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S102("synth-bathy-1"),
            LoadedDatasetFactory.S124("synth-warn-1"));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("list_datasets", new Dictionary<string, object?>
        {
            ["page"] = 0,
            ["pageSize"] = 50,
        });

        Assert.False(result.IsError ?? false, $"list_datasets returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var datasets = payload["datasets"]!.AsArray();
        Assert.Equal(2, datasets.Count);
        var ids = datasets
            .Select(d => d!["id"]!["value"]!.GetValue<string>())
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(new[] { "synth-bathy-1", "synth-warn-1" }, ids);
    }

    [Fact]
    public async Task DescribeFeature_round_trip_returns_feature_payload()
    {
        var feature = S124Synth.Feature(
            id: "feat-1",
            featureType: "NavwarnPart",
            attributes: new Dictionary<string, string>
            {
                ["warningInformation"] = "Test warning text.",
            });
        var dataset = S124Synth.Dataset(feature);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-1", model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("describe_feature", new Dictionary<string, object?>
        {
            ["datasetId"] = "synth-warn-1",
            ["featureId"] = "feat-1",
        });

        Assert.False(result.IsError ?? false, $"describe_feature returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        Assert.Equal("S-124", payload["spec"]!["name"]!.GetValue<string>());
        Assert.Equal("NavwarnPart", payload["featureTypeName"]!.GetValue<string>());
        Assert.NotNull(payload["attributes"]);
    }

    [Fact]
    public async Task SampleCoverage_round_trip_returns_depth()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S102("synth-bathy-1",
                bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
                source: S102Synth.Source(S102Synth.Dataset(depth: 17.5f, uncertainty: 0.5f))));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("sample_coverage", new Dictionary<string, object?>
        {
            ["spec"] = "S-102/2.1.0",
            ["latitude"] = 0.01,
            ["longitude"] = 0.01,
        });

        Assert.False(result.IsError ?? false, $"sample_coverage returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var depth = payload["value"]!["depthMeters"]!.GetValue<double>();
        Assert.InRange(depth, 17.49, 17.51);
    }

    [Fact]
    public async Task DescribeFeature_unknown_dataset_returns_structured_error()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("describe_feature", new Dictionary<string, object?>
        {
            ["datasetId"] = "missing",
            ["featureId"] = "x",
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for unknown dataset.");
        var payload = ParseSingleJson(result);
        Assert.Equal("dataset_not_found", payload["code"]!.GetValue<string>());
        Assert.False(string.IsNullOrEmpty(payload["message"]?.GetValue<string>()));
    }

    [Fact]
    public async Task FindAt_round_trip_returns_matching_datasets()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("warn-here", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)),
            LoadedDatasetFactory.S124("warn-elsewhere", bounds: LoadedDatasetFactory.Box(50, 50, 60, 60)));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("find_at", new Dictionary<string, object?>
        {
            ["latitude"] = 5.0,
            ["longitude"] = 5.0,
        });

        Assert.False(result.IsError ?? false, $"find_at returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var datasets = payload["datasets"]!.AsArray();
        Assert.Single(datasets);
        Assert.Equal("warn-here", datasets[0]!["id"]!["value"]!.GetValue<string>());
        Assert.Equal(1, payload["totalCount"]!.GetValue<int>());
    }

    [Fact]
    public async Task FindAt_invalid_latitude_returns_structured_error()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("find_at", new Dictionary<string, object?>
        {
            ["latitude"] = 95.0,
            ["longitude"] = 0.0,
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for out-of-range latitude.");
        var payload = ParseSingleJson(result);
        Assert.Equal("invalid_argument", payload["code"]!.GetValue<string>());
        Assert.Equal("latitude", payload["details"]!["parameter"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_round_trip_returns_matching_features()
    {
        var feature = new S124Feature
        {
            Id = "feat-1",
            FeatureType = "NavwarnPart",
            GeometryType = GmlGeometryType.Point,
            Points = ImmutableArray.Create((5.0, 5.0)),
            Curves = default,
            ExteriorRing = default,
            InteriorRings = default,
            Attributes = ImmutableDictionary<string, string>.Empty.Add("warningInformation", "Test warning text."),
            ComplexAttributes = ImmutableArray<S124ComplexAttribute>.Empty,
            References = ImmutableArray<GmlReference>.Empty,
        };
        var dataset = S124Synth.Dataset(feature);
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("synth-warn-1", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10), model: dataset));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = """{"kind":"box","south":-5,"west":-5,"north":15,"east":15}""",
        });

        Assert.False(result.IsError ?? false, $"query_features returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var features = payload["features"]!.AsArray();
        Assert.NotEmpty(features);
        Assert.Equal("feat-1", features[0]!["featureId"]!.GetValue<string>());
        Assert.Equal("NavwarnPart", features[0]!["featureType"]!.GetValue<string>());
    }

    [Fact]
    public async Task QueryFeatures_invalid_query_json_returns_error()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("query_features", new Dictionary<string, object?>
        {
            ["query"] = """{"kind":"unknown"}""",
        });

        Assert.True(result.IsError ?? false, "Expected isError=true for unknown query kind.");
        var payload = ParseSingleJson(result);
        Assert.Equal("internal_error", payload["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task SampleCoverageAlong_round_trip_returns_per_vertex_results()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S102("synth-bathy-1",
                bounds: LoadedDatasetFactory.Box(0, 0, 0.04, 0.04),
                source: S102Synth.Source(S102Synth.Dataset(depth: 17.5f, uncertainty: 0.5f))));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("sample_coverage_along", new Dictionary<string, object?>
        {
            ["spec"] = "S-102/2.1.0",
            ["polyline"] = """{"vertices":[[0.01,0.01],[0.02,0.02],[50.0,50.0]]}""",
        });

        Assert.False(result.IsError ?? false, $"sample_coverage_along returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var samples = payload["samples"]!.AsArray();
        Assert.Equal(3, samples.Count);
        // First two vertices are inside the bathy bounds and should resolve.
        Assert.NotNull(samples[0]!["result"]);
        Assert.NotNull(samples[1]!["result"]);
        // Last vertex is far outside any coverage — per-vertex miss -> null.
        Assert.Null(samples[2]!["result"]);
    }

    [Fact]
    public async Task ListSpecs_round_trip_returns_capabilities()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S102("synth-bathy-1"),
            LoadedDatasetFactory.S124("synth-warn-1"));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("list_specs", new Dictionary<string, object?>());

        Assert.False(result.IsError ?? false, $"list_specs returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var specs = payload["specs"]!.AsArray();
        Assert.NotEmpty(specs);
        // Every entry exposes its capability flags.
        foreach (var entry in specs)
        {
            Assert.NotNull(entry!["name"]);
            var caps = entry!["capabilities"]!.AsObject();
            Assert.NotNull(caps["canQueryFeatures"]);
            Assert.NotNull(caps["canDescribeFeature"]);
            Assert.NotNull(caps["canSampleCoverage"]);
            Assert.NotNull(caps["canListTimeSteps"]);
        }
    }

    [Fact]
    public async Task ListTimeSteps_round_trip_returns_cadence_for_S104()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S104("wl-1"));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync(
            "list_time_steps",
            new Dictionary<string, object?> { ["datasetId"] = "wl-1" });

        Assert.False(result.IsError ?? false, $"list_time_steps returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var times = payload["times"]!.AsArray();
        Assert.NotEmpty(times);
        Assert.NotNull(payload["firstTime"]);
        Assert.NotNull(payload["lastTime"]);
        Assert.NotNull(payload["cadence"]);
        Assert.Equal("S-104", payload["spec"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task FindAt_with_box_query_envelope_returns_intersecting_datasets()
    {
        var catalog = McpTestHelpers.NewCatalog(
            LoadedDatasetFactory.S124("warn-here", bounds: LoadedDatasetFactory.Box(0, 0, 10, 10)),
            LoadedDatasetFactory.S124("warn-elsewhere", bounds: LoadedDatasetFactory.Box(50, 50, 60, 60)));

        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var result = await client.CallToolAsync("find_at", new Dictionary<string, object?>
        {
            // latitude/longitude are ignored when 'query' is supplied.
            ["latitude"] = 0.0,
            ["longitude"] = 0.0,
            ["query"] = """{"kind":"box","south":-5,"west":-5,"north":15,"east":15}""",
        });

        Assert.False(result.IsError ?? false, $"find_at returned an error: {DumpText(result)}");
        var payload = ParseSingleJson(result);
        var datasets = payload["datasets"]!.AsArray();
        Assert.Single(datasets);
        Assert.Equal("warn-here", datasets[0]!["id"]!["value"]!.GetValue<string>());
    }

    private static JsonObject ParseSingleJson(CallToolResult result)
    {
        var text = DumpText(result);
        var node = JsonNode.Parse(text)
            ?? throw new InvalidOperationException("Tool result text was not valid JSON.");
        return node.AsObject();
    }

    private static string DumpText(CallToolResult result)
    {
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock t) return t.Text;
        }
        return "";
    }
}
