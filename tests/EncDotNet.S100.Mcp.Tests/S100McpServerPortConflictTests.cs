using System.Net;
using System.Net.Sockets;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>
/// Exercises bind-time port-conflict surfacing for
/// <see cref="S100McpServer"/>. These tests are the empirical basis for
/// the exception-detection logic in <c>McpServerHost.IsPortInUse</c>:
/// they pre-bind a <see cref="TcpListener"/> on a chosen loopback port
/// and confirm that <see cref="S100McpServer.StartAsync"/> surfaces a
/// distinguishable exception whose chain contains the underlying
/// <see cref="SocketException"/> with
/// <see cref="SocketError.AddressAlreadyInUse"/>.
/// </summary>
public class S100McpServerPortConflictTests
{
    [Fact]
    public async Task StartAsync_throws_when_port_is_in_use()
    {
        // Pick a free port up-front by binding-then-releasing a listener.
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        // Now occupy that port for the duration of the test.
        var squatter = new TcpListener(IPAddress.Loopback, port);
        squatter.Start();
        try
        {
            await using var server = new S100McpServer(
                new FakeDatasetCatalog(),
                new S100McpServerOptions { BindAddress = IPAddress.Loopback, Port = port });

            var ex = await Assert.ThrowsAnyAsync<Exception>(() => server.StartAsync());

            // The exception chain must contain a SocketException with
            // AddressAlreadyInUse — this is the signal McpServerHost
            // relies on to distinguish "port taken" from other faults.
            bool foundAddressInUse = false;
            for (var e = (Exception?)ex; e is not null; e = e.InnerException)
            {
                if (e is SocketException sx && sx.SocketErrorCode == SocketError.AddressAlreadyInUse)
                {
                    foundAddressInUse = true;
                    break;
                }
            }
            Assert.True(foundAddressInUse,
                $"Expected AddressAlreadyInUse SocketException in chain. Got: {ex}");

            Assert.False(server.IsRunning);
        }
        finally
        {
            squatter.Stop();
        }
    }
}
