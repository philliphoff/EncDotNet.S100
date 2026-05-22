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
/// Wraps a <see cref="RenderToImageTool"/> as an <see cref="McpServerTool"/>
/// so the viewer's <c>McpServerHost</c> can inject it into the hosted
/// <c>EncDotNet.S100.Mcp.S100McpServer</c>. The wrapper returns the
/// PNG as a first-class <see cref="ImageContentBlock"/> (the first
/// MCP tool in this codebase to do so), followed by a
/// <see cref="TextContentBlock"/> carrying JSON metadata so agents
/// still see echoed dimensions.
/// </summary>
internal static class RenderToImageMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private const string Description =
        "Captures the viewer's current map view as a PNG image and returns it as an MCP " +
        "ImageContentBlock alongside a JSON metadata block. Primary use case: agent-driven " +
        "diagnosis of rendering issues — palette banding, NoData voids, augmented-geometry " +
        "artefacts, missing features, etc. The snapshot reflects exactly what the user sees: " +
        "viewport, palette, time step, loaded datasets. Read-only and side-effect free; the " +
        "live map control is not mutated. Viewer-injected tool — not available from a headless " +
        "MCP host until that host supplies its own equivalent.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(RenderToImageTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var del = (
            [Description("Output image width in pixels; null defaults to 1024. Clamped to [64, 4096].")] int? width = null,
            [Description("Output image height in pixels; null defaults to 768. Clamped to [64, 4096].")] int? height = null,
            [Description("Display pixel-density multiplier (1.0 = device-independent pixels; 2.0 = HiDPI). Null defaults to 1.0. Clamped to [0.5, 3.0].")] double? pixelDensity = null,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(
                new RenderToImageRequest(width, height, pixelDensity),
                ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = RenderToImageTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<RenderToImageResult>>> resultFactory)
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
    /// adapter produces in production. Lets tests assert that successes
    /// surface as <see cref="ImageContentBlock"/> + <see cref="TextContentBlock"/>
    /// (in that order) without standing up an MCP server.
    /// </summary>
    internal static CallToolResult TranslateResult(ToolResult<RenderToImageResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            return Success(value);
        }
        result.TryGetError(out var err);
        return Failure(err!);
    }

    private static CallToolResult Success(RenderToImageResult value)
    {
        var metadata = new JsonObject
        {
            ["width"] = value.Width,
            ["height"] = value.Height,
            ["pixelDensity"] = value.PixelDensity,
            ["imageFormat"] = value.ImageFormat,
            ["byteLength"] = value.ImageBytes.Length,
        };
        if (value.Notes is not null)
        {
            metadata["notes"] = value.Notes;
        }

        return new CallToolResult
        {
            Content =
            [
                ImageContentBlock.FromBytes(value.ImageBytes, "image/png"),
                new TextContentBlock { Text = metadata.ToJsonString(JsonOptions) },
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
