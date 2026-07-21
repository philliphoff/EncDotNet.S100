using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Datasets.Pipelines.Query;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps a <see cref="CaptureAppScreenshotTool"/> as an
/// <see cref="McpServerTool"/> so the viewer's <c>McpServerHost</c> can
/// inject it into the hosted <c>S100McpServer</c>. Like
/// <see cref="RenderToImageMcpAdapter"/> the success path returns the PNG
/// as a first-class <see cref="ImageContentBlock"/> followed by a
/// <see cref="TextContentBlock"/> carrying JSON metadata.
/// </summary>
internal static class CaptureAppScreenshotMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Captures the whole viewer application window as a PNG image — the chart plus the surrounding "
        + "chrome (activity docks, panels, timeline, status bar) — and returns it as an MCP ImageContentBlock "
        + "alongside a JSON metadata block. Primary use case: agent-driven verification of non-rendering UX "
        + "changes, e.g. confirming that set_panel actually opened a panel. Complements render_to_image, which "
        + "captures only the map surface. Read-only and side-effect free; the window is not mutated. "
        + "Viewer-injected tool — not available from a headless MCP host until that host supplies its own "
        + "equivalent.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(CaptureAppScreenshotTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var del = (CancellationToken ct = default) => DispatchAsync(() => inner.InvokeAsync(ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = CaptureAppScreenshotTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<CaptureAppScreenshotResult>>> resultFactory)
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
            return ToolErrorPayload.InternalError(ex, JsonOptions);
        }
    }

    /// <summary>
    /// Test seam: translates an already-completed <see cref="ToolResult{T}"/>
    /// into a <see cref="CallToolResult"/> using the same shape this adapter
    /// produces in production.
    /// </summary>
    internal static CallToolResult TranslateResult(ToolResult<CaptureAppScreenshotResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            return Success(value);
        }
        result.TryGetError(out var err);
        return ToolErrorPayload.AsCallToolResult(err!, JsonOptions);
    }

    private static CallToolResult Success(CaptureAppScreenshotResult value)
    {
        var metadata = new JsonObject
        {
            ["imageFormat"] = value.ImageFormat,
            ["byteLength"] = value.ImageBytes.Length,
        };
        if (value.Width > 0) metadata["width"] = value.Width;
        if (value.Height > 0) metadata["height"] = value.Height;

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
}
