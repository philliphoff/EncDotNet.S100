using System.Net;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tests;

public class S100McpServerLifecycleTests
{
    [Fact]
    public async Task StartAsync_assigns_port_and_endpoint()
    {
        await using var server = new S100McpServer(
            new FakeDatasetCatalog(),
            new S100McpServerOptions { BindAddress = IPAddress.Loopback, Port = 0 });

        Assert.False(server.IsRunning);
        Assert.Null(server.Port);
        Assert.Null(server.Endpoint);

        await server.StartAsync();

        Assert.True(server.IsRunning);
        Assert.NotNull(server.Port);
        Assert.True(server.Port > 0);
        Assert.NotNull(server.Endpoint);
        Assert.Equal("http", server.Endpoint!.Scheme);
    }

    [Fact]
    public async Task StopAsync_clears_state()
    {
        var server = new S100McpServer(
            new FakeDatasetCatalog(),
            new S100McpServerOptions { BindAddress = IPAddress.Loopback, Port = 0 });
        await server.StartAsync();
        Assert.True(server.IsRunning);

        await server.StopAsync();

        Assert.False(server.IsRunning);
        Assert.Null(server.Port);
        Assert.Null(server.Endpoint);
    }

    [Fact]
    public async Task DisposeAsync_stops_cleanly()
    {
        var server = new S100McpServer(
            new FakeDatasetCatalog(),
            new S100McpServerOptions { BindAddress = IPAddress.Loopback, Port = 0 });
        await server.StartAsync();

        await server.DisposeAsync();

        Assert.False(server.IsRunning);
    }

    [Fact]
    public async Task Endpoint_is_loopback()
    {
        await using var server = new S100McpServer(
            new FakeDatasetCatalog(),
            new S100McpServerOptions { BindAddress = IPAddress.Loopback, Port = 0 });
        await server.StartAsync();

        Assert.NotNull(server.Endpoint);
        var host = server.Endpoint!.Host;
        Assert.True(host == "127.0.0.1" || host == "::1",
            $"Expected loopback host but was '{host}'.");
    }

    [Fact]
    public async Task StateChanged_fires_on_start_and_stop()
    {
        var fired = 0;
        var server = new S100McpServer(
            new FakeDatasetCatalog(),
            new S100McpServerOptions { BindAddress = IPAddress.Loopback, Port = 0 });
        server.StateChanged += (_, _) => Interlocked.Increment(ref fired);

        await server.StartAsync();
        await server.StopAsync();

        Assert.True(fired >= 2, $"StateChanged fired {fired} times; expected ≥ 2.");
    }
}
