using System.Net;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Tests.Fakes;
using ModelContextProtocol.Client;

namespace EncDotNet.S100.Mcp.Tests;

/// <summary>
/// Shared MCP client helper. The MCP SDK transport defaults to
/// auto-detect (Streamable HTTP, then SSE); we point it at the
/// loopback endpoint the server reports.
/// </summary>
internal static class McpTestClient
{
    public static async Task<McpClient> ConnectAsync(S100McpServer server, CancellationToken ct = default)
    {
        if (server.Endpoint is null)
        {
            throw new InvalidOperationException("Server is not running.");
        }
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = server.Endpoint,
            TransportMode = HttpTransportMode.StreamableHttp,
            Name = "encdotnet-test",
        });
        return await McpClient.CreateAsync(transport, cancellationToken: ct);
    }
}

internal static class McpTestHelpers
{
    public static FakeDatasetCatalog NewCatalog(params LoadedDataset[] datasets)
    {
        var c = new FakeDatasetCatalog();
        foreach (var d in datasets) c.Add(d);
        return c;
    }

    public static async Task<S100McpServer> StartServerAsync(IDatasetCatalog catalog)
    {
        var server = new S100McpServer(catalog, new S100McpServerOptions
        {
            BindAddress = IPAddress.Loopback,
            Port = 0,
        });
        await server.StartAsync();
        return server;
    }
}
