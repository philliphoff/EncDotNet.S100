using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines.Query;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps a <see cref="SetOwnShipTool"/> as an <see cref="McpServerTool"/>
/// so the viewer's <c>McpServerHost</c> can inject it into the hosted
/// <c>EncDotNet.S100.Mcp.S100McpServer</c>. The tool mutates the live
/// own-ship helm and so is explicitly NOT side-effect-free.
/// </summary>
internal static class SetOwnShipMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Mutates the live viewer's simulated own-ship position by driving the helm. All fields " +
        "optional: provide lat AND lon together to reposition; cog/sog adjust course/speed; heading " +
        "(gyro heading, applied only with a position); hold=true stops the vessel, hold=false resumes " +
        "its remembered speed. Angles are degrees true; speed is metres per second. Works even when " +
        "the own-ship overlay is hidden (the state is cached and shown when enabled). Viewer-injected " +
        "tool — not available from a headless MCP host until that host supplies its own equivalent.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(SetOwnShipTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var del = (
            [Description("WGS-84 latitude in decimal degrees [-90, 90]. Must be paired with lon.")] double? lat = null,
            [Description("WGS-84 longitude in decimal degrees [-180, 180]. Must be paired with lat.")] double? lon = null,
            [Description("Course over ground in degrees true; normalised to [0, 360).")] double? cog = null,
            [Description("Speed over ground in metres per second; clamped to be non-negative.")] double? sog = null,
            [Description("Gyro heading in degrees true; only applied together with lat/lon.")] double? heading = null,
            [Description("true stops the vessel (remembering speed); false resumes the remembered speed.")] bool? hold = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(
                new SetOwnShipRequest(lat, lon, cog, sog, heading, hold),
                ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SetOwnShipTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<SetOwnShipResult>>> resultFactory)
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
    /// Test seam: translates an already-completed
    /// <see cref="ToolResult{T}"/> into a <see cref="CallToolResult"/>
    /// using the same shape this adapter produces in production.
    /// </summary>
    internal static CallToolResult TranslateResult(ToolResult<SetOwnShipResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            return Success(value);
        }
        result.TryGetError(out var err);
        return Failure(err!);
    }

    private static CallToolResult Success(SetOwnShipResult value)
    {
        var payload = new JsonObject
        {
            ["lat"] = value.Lat,
            ["lon"] = value.Lon,
            ["cog"] = value.Cog,
            ["sog"] = value.Sog,
            ["heading"] = value.Heading,
            ["holdAction"] = value.HoldAction,
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
