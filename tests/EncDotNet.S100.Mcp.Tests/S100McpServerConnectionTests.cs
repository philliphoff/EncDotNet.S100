using System.Net;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tests;

public class S100McpServerConnectionTests
{
    [Fact]
    public async Task ConnectionCount_changes_when_client_connects_and_disconnects()
    {
        await using var server = new S100McpServer(
            new FakeDatasetCatalog(),
            new S100McpServerOptions { BindAddress = IPAddress.Loopback, Port = 0 });
        await server.StartAsync();

        var peak = 0;
        var fired = 0;
        server.ConnectionsChanged += (_, _) =>
        {
            Interlocked.Increment(ref fired);
            var current = server.ConnectionCount;
            if (current > peak) peak = current;
        };

        // Open and immediately close an MCP client; the SDK does a
        // streamable-HTTP handshake which exercises the request pipeline
        // at least once. Connections are tracked at the HTTP-request
        // level by ConnectionTrackingMiddleware.
        var client = await McpTestClient.ConnectAsync(server);
        await client.DisposeAsync();

        // Allow lingering middleware decrements to settle.
        await Task.Delay(100);

        Assert.True(fired > 0, "ConnectionsChanged did not fire.");
        Assert.True(peak > 0, $"Peak connection count was {peak}; expected > 0.");
        Assert.Equal(0, server.ConnectionCount);
    }
}
