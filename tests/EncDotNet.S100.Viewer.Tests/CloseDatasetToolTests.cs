using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Viewer.McpTools;
using EncDotNet.S100.Viewer.Services;
using Xunit;

namespace EncDotNet.S100.Viewer.Tests;

public class CloseDatasetToolTests
{
    [Fact]
    public async Task Close_existing_dataset_reports_removed_metadata()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add("a.000", "S-101");
        catalog.Add("b.000", "S-102");
        var gateway = new FakeDatasetLoadGateway
        {
            OnRemove = id => Task.FromResult(catalog.Remove(id)),
        };
        var tool = new CloseDatasetTool(catalog, gateway);

        var result = await tool.InvokeAsync(new CloseDatasetRequest("a.000"));

        Assert.True(result.TryGetValue(out var ok));
        Assert.True(ok!.Removed);
        Assert.Equal(1, ok.Count);
        var removed = Assert.Single(ok.RemovedDatasets);
        Assert.Equal("a.000", removed.Id);
        Assert.Equal("S-101", removed.Spec);
    }

    [Fact]
    public async Task Unknown_id_resolves_gracefully_as_not_removed()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add("a.000", "S-101");
        var gateway = new FakeDatasetLoadGateway
        {
            OnRemove = id => Task.FromResult(catalog.Remove(id)),
        };
        var tool = new CloseDatasetTool(catalog, gateway);

        var result = await tool.InvokeAsync(new CloseDatasetRequest("nope.000"));

        Assert.True(result.TryGetValue(out var ok));
        Assert.False(ok!.Removed);
        Assert.Equal(0, ok.Count);
        Assert.Empty(ok.RemovedDatasets);
    }

    [Fact]
    public async Task Empty_id_returns_invalid_argument()
    {
        var tool = new CloseDatasetTool(new FakeDatasetCatalog(), new FakeDatasetLoadGateway());

        var result = await tool.InvokeAsync(new CloseDatasetRequest("  "));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<InvalidArgument>(err);
    }

    [Fact]
    public async Task Not_ready_returns_map_not_ready()
    {
        var gateway = new FakeDatasetLoadGateway { IsReady = false };
        var tool = new CloseDatasetTool(new FakeDatasetCatalog(), gateway);

        var result = await tool.InvokeAsync(new CloseDatasetRequest("a.000"));

        Assert.True(result.TryGetError(out var err));
        Assert.IsType<MapNotReady>(err);
    }

    [Fact]
    public void Adapter_translates_success_payload()
    {
        var ok = ToolResult<CloseDatasetResult>.Ok(new CloseDatasetResult(
            "a.000", true, 1, new[] { new RemovedDataset("a.000", "S-101") }));

        var call = CloseDatasetMcpAdapter.TranslateResult(ok);

        Assert.False(call.IsError);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(call.Content[0]).Text;
        Assert.Contains("\"removed\":true", text);
        Assert.Contains("a.000", text);
    }

    [Fact]
    public async Task Close_all_datasets_removes_everything()
    {
        var catalog = new FakeDatasetCatalog();
        catalog.Add("a.000", "S-101");
        catalog.Add("b.h5", "S-102");
        var gateway = new FakeDatasetLoadGateway
        {
            OnRemove = id => Task.FromResult(catalog.Remove(id)),
        };
        var tool = new CloseAllDatasetsTool(catalog, gateway);

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var ok));
        Assert.True(ok!.Removed);
        Assert.Equal(2, ok.Count);
        Assert.Empty(catalog.Datasets);
    }

    [Fact]
    public async Task Close_all_when_empty_is_graceful()
    {
        var tool = new CloseAllDatasetsTool(new FakeDatasetCatalog(), new FakeDatasetLoadGateway());

        var result = await tool.InvokeAsync();

        Assert.True(result.TryGetValue(out var ok));
        Assert.False(ok!.Removed);
        Assert.Equal(0, ok.Count);
        Assert.Empty(ok.RemovedDatasets);
    }

    [Fact]
    public void Close_all_adapter_translates_success_payload()
    {
        var ok = ToolResult<CloseAllDatasetsResult>.Ok(new CloseAllDatasetsResult(
            true, 2, new[]
            {
                new RemovedDataset("a.000", "S-101"),
                new RemovedDataset("b.h5", "S-102"),
            }));

        var call = CloseAllDatasetsMcpAdapter.TranslateResult(ok);

        Assert.False(call.IsError);
        var text = Assert.IsType<ModelContextProtocol.Protocol.TextContentBlock>(call.Content[0]).Text;
        Assert.Contains("\"removed\":true", text);
        Assert.Contains("\"count\":2", text);
    }
}
