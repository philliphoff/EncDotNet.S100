using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Mcp.Tests;

public class S100McpServerRoundTripTests
{
    [Fact]
    public async Task ListTools_returns_three_tools_with_schemas()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = await McpTestHelpers.StartServerAsync(catalog);
        await using var client = await McpTestClient.ConnectAsync(server);

        var tools = await client.ListToolsAsync();
        var names = tools.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(new[] { "describe_feature", "list_datasets", "sample_coverage" }, names);
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
