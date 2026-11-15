using System.Net;
using System.Text.Json.Nodes;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>
/// The one-line JSON document <c>tiles serve --ready-json</c> prints when the
/// server is listening (issue #1496), so a supervising process (a GIS add-in,
/// a test harness) learns the bound port and URLs without parsing the
/// human-readable start-up text.
/// </summary>
/// <remarks>
/// The URLs use an address the supervisor can connect to: the listening
/// address, or the loopback address when listening on all addresses
/// (<c>0.0.0.0</c> or <c>::</c>). <c>healthz</c> is outside the token, as the
/// route is. <c>preview</c> is <see langword="null"/> with <c>--no-viewer</c>,
/// and <c>token</c> when there is none.
/// </remarks>
internal static class ServeReadyLine
{
    /// <summary>The value of the <c>event</c> member.</summary>
    public const string ReadyEvent = "ready";

    /// <summary>Builds the line, without a trailing newline.</summary>
    /// <param name="address">The address the server listens on.</param>
    /// <param name="port">The bound port.</param>
    /// <param name="token">The access token, or <see langword="null"/>.</param>
    /// <param name="format">The tile image format.</param>
    /// <param name="viewer">Whether the preview page is served.</param>
    /// <returns>A compact JSON object on one line.</returns>
    public static string Format(IPAddress address, int port, string? token, TileImageFormat format, bool viewer)
    {
        ArgumentNullException.ThrowIfNull(address);

        var host = ConnectableAddress(address);
        var baseUri = ServeHost.BaseUri(host, port, token);
        var root = ServeHost.BaseUri(host, port, token: null);
        var line = new JsonObject
        {
            ["event"] = ReadyEvent,
            ["host"] = host.ToString(),
            ["port"] = port,
            ["token"] = token,
            ["baseUrl"] = baseUri.AbsoluteUri,
            ["tiles"] = TileServer.TileUrlTemplate(baseUri, format),
            ["tileJson"] = new Uri(baseUri, XyzDirectoryTileSink.TileJsonFileName).AbsoluteUri,
            ["wmts"] = new Uri(baseUri, WmtsService.CapabilitiesPath).AbsoluteUri,
            ["wms"] = new Uri(baseUri, WmsService.Path).AbsoluteUri,
            ["healthz"] = new Uri(root, ServeHost.HealthPath).AbsoluteUri,
            ["preview"] = viewer ? baseUri.AbsoluteUri : null,
        };
        return line.ToJsonString();
    }

    /// <summary>The listening address, or loopback when listening on every address.</summary>
    internal static IPAddress ConnectableAddress(IPAddress address)
    {
        if (address.Equals(IPAddress.Any))
            return IPAddress.Loopback;
        if (address.Equals(IPAddress.IPv6Any))
            return IPAddress.IPv6Loopback;
        return address;
    }
}
