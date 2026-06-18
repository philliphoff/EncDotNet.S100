using System;
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Viewer.McpTools;

/// <summary>
/// Wraps a <see cref="PickFeaturesTool"/> as an <see cref="McpServerTool"/>
/// so the viewer's <c>McpServerHost</c> can inject it into the hosted
/// <c>EncDotNet.S100.Mcp.S100McpServer</c>. The tool is read-only: it
/// observes the live viewport and loaded datasets without mutating either,
/// and is the feature-aware inverse of <c>render_to_image</c> — resolving a
/// screen pixel from a captured frame back to the vector features under it.
/// </summary>
internal static class PickFeaturesMcpAdapter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
    };

    private const string Description =
        "Resolves the vector features under a point on the live viewer map — the feature-aware inverse " +
        "of render_to_image. Supply EITHER a screen pixel (x/y in device-independent pixels from the " +
        "live viewport's top-left, the same coordinate space render_to_image captures) OR a WGS-84 " +
        "geographic point (latitude/longitude); mixing or omitting both is rejected. The pixel form " +
        "projects through the live navigator's web-mercator viewport to a geographic point, then ranks " +
        "matches most-specific first (point before curve before area), identical in shape to " +
        "identify_features. Read-only. Viewer-injected tool — not available from a headless MCP host.";

    /// <summary>Creates the <see cref="McpServerTool"/>.</summary>
    public static McpServerTool Create(PickFeaturesTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        var del = (
            [Description("Screen X in device-independent pixels from the live viewport's left edge. Must be paired with y; mutually exclusive with latitude/longitude.")] double? x = null,
            [Description("Screen Y in device-independent pixels from the live viewport's top edge. Must be paired with x; mutually exclusive with latitude/longitude.")] double? y = null,
            [Description("Pick latitude in decimal degrees (WGS-84). Must be paired with longitude; mutually exclusive with the x/y form.")] double? latitude = null,
            [Description("Pick longitude in decimal degrees (WGS-84). Must be paired with latitude; mutually exclusive with the x/y form.")] double? longitude = null,
            [Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every vector spec.")] string? spec = null,
            [Description("Search tolerance for point/curve features in metres; area features use exact containment and ignore it. Clamped to [0, 100000]. Default 50.")] double radiusMeters = 50.0,
            [Description("Maximum ranked matches to return; clamped to [1, 200]. Default 20.")] int maxResults = 20,
            CancellationToken ct = default) =>
            DispatchAsync(() => inner.InvokeAsync(
                new PickFeaturesRequest(x, y, latitude, longitude, ParseSpec(spec), radiusMeters, maxResults),
                ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = PickFeaturesTool.Name,
            Description = Description,
            SerializerOptions = JsonOptions,
        });
    }

    private static SpecRef? ParseSpec(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec))
        {
            return null;
        }

        if (!SpecRef.TryParse(spec, out var parsed))
        {
            if (SpecName.TryNormalize(spec, out var name))
            {
                return new SpecRef(name, default);
            }

            throw new FormatException($"'{spec}' is not a valid spec.");
        }

        return parsed;
    }

    private static async Task<CallToolResult> DispatchAsync(
        Func<Task<ToolResult<PickFeaturesResult>>> resultFactory)
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
    internal static CallToolResult TranslateResult(ToolResult<PickFeaturesResult> result)
    {
        if (result.TryGetValue(out var value))
        {
            return Success(value);
        }
        result.TryGetError(out var err);
        return Failure(err!);
    }

    private static CallToolResult Success(PickFeaturesResult value)
    {
        var json = JsonSerializer.Serialize(value, JsonOptions);
        return new CallToolResult
        {
            Content =
            [
                new TextContentBlock { Text = json },
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
