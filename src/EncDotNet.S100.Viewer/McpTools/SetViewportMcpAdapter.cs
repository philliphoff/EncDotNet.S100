using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps a <see cref="SetViewportTool"/> as an <see cref="McpServerTool"/>
/// so the viewer's <c>McpServerHost</c> can inject it into the hosted
/// <c>EncDotNet.S100.Mcp.S100McpServer</c>. The tool mutates the live
/// viewer's navigator and so is explicitly NOT side-effect-free —
/// callers should treat it as the counterpart to the read-only
/// <c>render_to_image</c> tool when scripting measurement runs.
/// </summary>
internal static class SetViewportMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Mutates the live viewer's map navigator to a specific WGS-84 viewport. Supply EITHER a " +
        "bbox (south/west/north/east) OR centre+zoom (centerLat/centerLon/zoom); mixing the two " +
        "is rejected. Coordinates are decimal degrees. Zoom is the standard web-mercator level " +
        "(0–24). Antimeridian-crossing bboxes are not supported in v1. Companion to render_to_image: " +
        "this tool drives the navigator, render_to_image then captures the resulting frame. " +
        "Viewer-injected tool — not available from a headless MCP host until that host supplies its own equivalent.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(SetViewportTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var del = (
            [Description("Bounding-box south edge in decimal degrees (WGS-84). Must be paired with west/north/east; mutually exclusive with centre+zoom.")] double? south = null,
            [Description("Bounding-box west edge in decimal degrees (WGS-84). Must be paired with south/north/east; mutually exclusive with centre+zoom.")] double? west = null,
            [Description("Bounding-box north edge in decimal degrees (WGS-84). Must be paired with south/west/east; mutually exclusive with centre+zoom.")] double? north = null,
            [Description("Bounding-box east edge in decimal degrees (WGS-84). Must be paired with south/west/north; mutually exclusive with centre+zoom.")] double? east = null,
            [Description("Centre latitude in decimal degrees (WGS-84). Must be paired with centerLon and zoom; mutually exclusive with the bbox form.")] double? centerLat = null,
            [Description("Centre longitude in decimal degrees (WGS-84). Must be paired with centerLat and zoom; mutually exclusive with the bbox form.")] double? centerLon = null,
            [Description("Web-mercator zoom level in [0, 24]. Must be paired with centerLat/centerLon; mutually exclusive with the bbox form.")] double? zoom = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(
                new SetViewportRequest(south, west, north, east, centerLat, centerLon, zoom),
                ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetViewportTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<SetViewportResult>>> resultFactory)
    {
        try
        {
            var result = await resultFactory().ConfigureAwait(false);
            return TranslateResult(result);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return InternalError(ex);
        }
    }

    /// <summary>
    /// Test seam: translates an already-completed <see cref="ToolResult{T}"/>
    /// into a <see cref="CallToolResult"/> using the same shape this
    /// adapter produces in production.
    /// </summary>
    internal static CallToolResult TranslateResult(ToolResult<SetViewportResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            return Success(value);
        }
        result.TryGetError(out var err);
        return Failure(err!);
    }

    private static CallToolResult Success(SetViewportResult value)
    {
        var payload = new JsonObject
        {
            ["mode"] = value.Mode,
            ["south"] = value.South,
            ["west"] = value.West,
            ["north"] = value.North,
            ["east"] = value.East,
        };

        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = payload.ToJsonString(JsonOptions) },
            ],
            IsError = false,
        };
    }

    private static CallToolResult Failure(ToolError error)
    {
        var details = JsonSerializer.SerializeToNode(error, error.GetType(), JsonOptions) as JsonObject
            ?? new JsonObject();
        details.Remove("code");
        details.Remove("message");
        details.Remove("Code");
        details.Remove("Message");

        var payload = new JsonObject
        {
            ["code"] = error.Code,
            ["message"] = error.Message,
            ["details"] = details,
        };
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = payload.ToJsonString(JsonOptions) },
            ],
            IsError = true,
        };
    }

    private static CallToolResult InternalError(Exception ex)
    {
        var payload = new JsonObject
        {
            ["code"] = "internal_error",
            ["message"] = ex.Message,
            ["details"] = new JsonObject
            {
                ["exceptionType"] = ex.GetType().FullName,
            },
        };
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = payload.ToJsonString(JsonOptions) },
            ],
            IsError = true,
        };
    }
}
