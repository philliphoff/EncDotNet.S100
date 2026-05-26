using System;
using System.ComponentModel;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools.Catalog;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp.Tests;

public class S100McpServerAdditionalToolsTests
{
    [Fact]
    public async Task AdditionalTools_are_advertised_alongside_built_ins()
    {
        var catalog = McpTestHelpers.NewCatalog();
        var extra = McpServerTool.Create(
            ([Description("Anything")] string echo) => echo,
            new McpServerToolCreateOptions { Name = "extra_echo", Description = "test-only tool" });

        await using var server = new S100McpServer(catalog, new S100McpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            AdditionalTools = new[] { extra },
        });
        await server.StartAsync();
        await using var client = await McpTestClient.ConnectAsync(server);

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "extra_echo");
        Assert.Contains(tools, t => t.Name == "list_datasets");
    }

    [Fact]
    public async Task Null_AdditionalTools_does_not_break_built_ins()
    {
        var catalog = McpTestHelpers.NewCatalog();
        await using var server = new S100McpServer(catalog, new S100McpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
            AdditionalTools = null,
        });
        await server.StartAsync();
        await using var client = await McpTestClient.ConnectAsync(server);

        var tools = await client.ListToolsAsync();
        Assert.Contains(tools, t => t.Name == "list_datasets");
    }
}
