using System.Text.Json.Nodes;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary><c>get_navigability_grid</c> (#1558) over the MCP protocol: a JSON summary, a PNG when asked, and errors as payloads.</summary>
public class NavigabilityGridMcpTests
{
    [Fact]
    public async Task The_tool_returns_a_summary_and_a_png_and_writes_no_file_when_asked_not_to()
    {
        await using var server = await McpTestHelpers.StartServerAsync(McpTestHelpers.NewCatalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var tool = (await client.ListToolsAsync(cancellationToken: TestContext.Current.CancellationToken)).Single(t => t.Name == NavigabilityGridTool.Name);
        Assert.True(tool.ProtocolTool.Annotations?.ReadOnlyHint);

        var result = await client.CallToolAsync(
            NavigabilityGridTool.Name,
            new Dictionary<string, object?>
            {
                ["south"] = 0.0,
                ["west"] = 0.0,
                ["north"] = 0.01,
                ["east"] = 0.01,
                ["cellMeters"] = 100.0,
                ["draftMeters"] = 2.0,
                ["format"] = "none",
                ["includeImage"] = true,
                ["connect"] = new[] { new[] { 0.001, 0.001 }, new[] { 0.009, 0.009 } },
            },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.False(result.IsError ?? false);
        var summary = JsonNode.Parse(Assert.IsType<TextContentBlock>(result.Content[0]).Text)!.AsObject();
        Assert.Equal(12, summary["grid"]!["rows"]!.GetValue<int>());
        Assert.Equal(summary["grid"]!["rows"]!.GetValue<int>() * summary["grid"]!["columns"]!.GetValue<int>(), summary["counts"]!["uncharted"]!.GetValue<int>());
        Assert.Equal("from_not_navigable", summary["connection"]!["reason"]!.GetValue<string>());
        Assert.Null(summary["file"]);
        Assert.Equal("image/png", Assert.IsType<ImageContentBlock>(result.Content[1]).MimeType);
    }

    [Fact]
    public async Task A_grid_over_the_cap_is_an_error_payload_with_a_cell_size_that_fits()
    {
        await using var server = await McpTestHelpers.StartServerAsync(McpTestHelpers.NewCatalog());
        await using var client = await McpTestClient.ConnectAsync(server, TestContext.Current.CancellationToken);

        var result = await client.CallToolAsync(
            NavigabilityGridTool.Name,
            new Dictionary<string, object?> { ["south"] = 40.0, ["west"] = -130.0, ["north"] = 50.0, ["east"] = -120.0, ["cellMeters"] = 10.0, ["draftMeters"] = 2.0 },
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(result.IsError);
        var payload = JsonNode.Parse(Assert.IsType<TextContentBlock>(result.Content[0]).Text)!.AsObject();
        Assert.Equal("grid_too_large", payload["code"]!.GetValue<string>());
        Assert.True(payload["details"]!["suggestedCellMeters"]!.GetValue<double>() > 10);
    }
}
