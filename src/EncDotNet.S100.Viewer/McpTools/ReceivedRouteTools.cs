using System.ComponentModel;
using System.Text.Json;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Viewer.Routing;
using EncDotNet.S100.Viewer.Services;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

// Received routes (#960): display mode, and later import, copy and export.

// ---------------------------------------------------------------------------
// set_route_display_mode
// ---------------------------------------------------------------------------

/// <summary>Request for <see cref="SetRouteDisplayModeTool"/>.</summary>
[Description("Request for set_route_display_mode: choose how the map draws a route.")]
internal sealed record SetRouteDisplayModeRequest(
    [property: Description("\"native\" (the route's S-421 dataset draws it with its own portrayal; the route renderer draws nothing) or \"planning\" (the route renderer draws it, without editing handles on a received route; the dataset is hidden).")] string Mode,
    [property: Description("Id of the route; omit to use the active route.")] string? RouteId = null);

/// <summary>
/// Sets a route's display mode, as the Native / Planning control under a
/// received route's header does. The route's mode decides its dataset's
/// visibility. While Check route is on for the route it's drawn in Planning
/// mode whatever its mode, and its dataset stays on with only its action
/// points.
/// </summary>
internal sealed class SetRouteDisplayModeTool : RouteToolBase
{
    /// <summary>Public tool name as exposed over MCP.</summary>
    public const string Name = "set_route_display_mode";

    /// <summary>Creates a new <see cref="SetRouteDisplayModeTool"/>.</summary>
    public SetRouteDisplayModeTool(RoutesService routes, IRouteEditInvoker invoker)
        : base(routes, invoker) { }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<RouteDetail>> InvokeAsync(
        SetRouteDisplayModeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        RouteDisplayMode mode;
        switch (request.Mode?.Trim().ToLowerInvariant())
        {
            case "native": mode = RouteDisplayMode.Native; break;
            case "planning": mode = RouteDisplayMode.Planning; break;
            default:
                return Task.FromResult(ToolResult<RouteDetail>.Err(new InvalidArgument("mode", "use \"native\" or \"planning\"")));
        }

        return Invoker.InvokeAsync(() =>
        {
            var route = Resolve(request.RouteId, out var error);
            if (route is null)
                return ToolResult<RouteDetail>.Err(error!);
            if (mode == RouteDisplayMode.Native && route.Source?.Format != RouteSourceFormats.S421)
                return ToolResult<RouteDetail>.Err(new InvalidArgument("mode", "native needs a route received from an S-421 dataset"));
            route.SetDisplayMode(mode);
            return Ok(route, Routes);
        });
    }
}

/// <summary>Wraps the received-route tools as <see cref="McpServerTool"/>s.</summary>
internal static class ReceivedRouteMcpAdapters
{
    private static readonly JsonSerializerOptions JsonOptions = McpAdapterShared.Options;

    private const string SetDisplayModeDescription =
        "Sets how the live viewer's map draws a route: \"native\" draws a received S-421 route with its " +
        "dataset's own portrayal (waypoints, legs, action points) and the route renderer draws nothing; " +
        "\"planning\" hides the dataset and the route renderer draws the route (without editing handles on " +
        "a received route). The dataset's visibility follows the mode. While Check route is on for the " +
        "route it is drawn in Planning mode and its dataset stays on with only its action points (S-421 viewing " +
        "group 52270). Only received " +
        "S-421 routes have a Native mode. Returns the route (displayMode, readOnly, source). " +
        "Viewer-injected tool — mutates live state.";

    /// <summary>Creates the <c>set_route_display_mode</c> tool.</summary>
    public static McpServerTool Create(SetRouteDisplayModeTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = (
            [Description("\"native\" or \"planning\".")] string mode,
            [Description("Id of the route; omit to use the active route.")] string? routeId = null,
            CancellationToken ct = default) =>
            McpAdapterShared.DispatchAsync(() => inner.InvokeAsync(new SetRouteDisplayModeRequest(mode, routeId), ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetRouteDisplayModeTool.Name,
            Description = SetDisplayModeDescription,
            SerializerOptions = JsonOptions,
        });
    }

    /// <summary>Test seam mirroring the production translation.</summary>
    internal static CallToolResult TranslateResult(ToolResult<RouteDetail> result)
        => McpAdapterShared.Translate(result);
}
