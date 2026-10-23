using System.Net;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Configuration options for <see cref="S100McpServer"/>.
/// </summary>
public sealed class S100McpServerOptions
{
    /// <summary>
    /// IP address to bind the HTTP listener to. Defaults to
    /// <see cref="IPAddress.Loopback"/> (<c>127.0.0.1</c>). The viewer
    /// only exposes a loopback bind in v1; non-loopback values are
    /// gated by config and produce no UI surface.
    /// </summary>
    public IPAddress BindAddress { get; init; } = IPAddress.Loopback;

    /// <summary>
    /// TCP port to listen on. <c>0</c> requests an ephemeral port
    /// assigned by the kernel — read back via <see cref="S100McpServer.Port"/>.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Soft cap on the number of concurrent MCP sessions the server
    /// will accept. Surplus connections are still accepted by Kestrel
    /// but the server library reports them via
    /// <see cref="S100McpServer.ConnectionCount"/> so callers can
    /// decide to refuse or throttle.
    /// </summary>
    public int MaxConcurrentConnections { get; init; } = 16;

    /// <summary>
    /// Time to wait before evicting an idle MCP session.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Additional MCP tools to expose alongside the built-in catalog
    /// tools. Hosts inject tools that need live UI / processor state
    /// here (e.g. the viewer's <c>render_to_image</c> tool, which
    /// snapshots the current Mapsui map). May be <see langword="null"/>
    /// or empty when no host-supplied tools are needed.
    /// </summary>
    /// <remarks>
    /// Each entry is appended verbatim to the built-in tool list before
    /// the MCP server is started; tool names must therefore not collide
    /// with the built-ins (<c>list_datasets</c>, <c>describe_feature</c>,
    /// <c>sample_coverage</c>, <c>find_at</c>, <c>query_features</c>,
    /// <c>sample_coverage_along</c>, <c>list_specs</c>,
    /// <c>list_time_steps</c>, <c>get_passage_result</c>, <c>check_route</c>).
    /// </remarks>
    public IReadOnlyList<McpServerTool>? AdditionalTools { get; init; }

    /// <summary>
    /// The host's routes, so the built-in <c>get_passage_result</c> and
    /// <c>check_route</c> tools can check the active or a named route as well
    /// as a polyline. May be
    /// <see langword="null"/> when the host has no routes.
    /// </summary>
    public EncDotNet.S100.Mcp.Tools.IPassageRouteProvider? PassageRoutes { get; init; }

    /// <summary>
    /// The route-check sources the host's own checks use, so the built-in
    /// <c>get_passage_result</c> and <c>check_route</c> tools share the cells'
    /// indexes and S-102 grids with them. May be <see langword="null"/>, and
    /// the two tools then share a cache of their own.
    /// </summary>
    public EncDotNet.S100.Datasets.Pipelines.Navigation.RouteCheckSourceCache? RouteCheckSources { get; init; }
}
