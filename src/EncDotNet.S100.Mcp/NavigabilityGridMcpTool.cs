using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.MutableTools;
using EncDotNet.S100.Mcp.Tools;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// The MCP wrapper of <see cref="NavigabilityGridTool"/>
/// (<c>get_navigability_grid</c>, #1558): a JSON summary, and a PNG of the
/// grid when asked for.
/// </summary>
internal static class NavigabilityGridMcpTool
{
    private const string Description =
        "Water, land and charted depth over a box on a regular grid, with overlapping chart cells resolved as check_route resolves them " +
        "(S-102 first, else the finest S-101/S-57 cell): each cell is navigable (charted depth at least the larger of draftMeters and " +
        "safetyContourMeters, least of its centre and corners), shoal, hazard (a wreck, obstruction or rock at or shallower than " +
        "hazardDepthMeters, or of unknown depth, or a structure standing in the water such as a breakwater, pier or pontoon), land, uncharted or no_depth. The reply is a summary: grid geometry, cell counts, " +
        "the shoalest navigable cell, the largest connected navigable area and its most open cell, and with connect whether two " +
        "positions join through navigable cells and where the widest way between them is narrowest. The grid itself is written to a " +
        "file on the server (format npy or raw; 'file' gives its path, resource uri and layout) for code to read: by path on the same " +
        "machine, else with resources/read on the resource_link the reply carries; grids are kept a day. includeImage adds a PNG (north up). " +
        "Use it to plan or explain a route over an area; check the route itself with check_route. Charted depths only, no water level. " +
        "Not for navigation.";

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    /// <summary>Creates the tool.</summary>
    /// <param name="inner">The tool.</param>
    /// <returns>The MCP tool.</returns>
    public static McpServerTool Create(NavigabilityGridTool inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        var del = ([Description("South latitude of the box, decimal degrees WGS-84.")] double south,
                   [Description("West longitude of the box.")] double west,
                   [Description("North latitude of the box.")] double north,
                   [Description("East longitude of the box (less than west crosses the antimeridian).")] double east,
                   [Description("Draft in metres: a navigable cell has at least this much charted depth. Give it, safetyContourMeters, or both.")] double? draftMeters = null,
                   [Description("Safety contour in metres: a navigable cell has at least this much charted depth too.")] double? safetyContourMeters = null,
                   [Description("Cell size in metres (default 50). At most 6,000,000 cells; a bigger grid is refused with a cell size that fits.")] double cellMeters = NavigabilityGridTool.DefaultCellMeters,
                   [Description("Dangers at or shallower than this mark their cells hazard (default: the minimum depth). Dangers with no known depth always do.")] double? hazardDepthMeters = null,
                   [Description("Read the charted dangers and structures (default true).")] bool includeHazards = true,
                   [Description("Two positions to test, [[lat,lon],[lat,lon]]: whether a navigable way joins them, and its narrowest place.")] JsonElement? connect = null,
                   [Description("The grid file: \"npy\" (default; NumPy structured array depth_dm int16 + class uint8), \"raw\" (int16 depths then class bytes) or \"none\".")] string format = NavigabilityGridTool.FormatNpy,
                   [Description("Add a PNG of the grid, north up, at most 1024 px a side.")] bool includeImage = false,
                   CancellationToken ct = default) =>
            InvokeAsync(inner, south, west, north, east, draftMeters, safetyContourMeters, cellMeters, hazardDepthMeters, includeHazards, connect, format, includeImage, ct);

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = NavigabilityGridTool.Name,
            Description = Description,
            SerializerOptions = McpJson.Options,
            ReadOnly = true,
            Idempotent = true,
        });
    }

    private static async Task<CallToolResult> InvokeAsync(
        NavigabilityGridTool inner,
        double south,
        double west,
        double north,
        double east,
        double? draftMeters,
        double? safetyContourMeters,
        double cellMeters,
        double? hazardDepthMeters,
        bool includeHazards,
        JsonElement? connect,
        string format,
        bool includeImage,
        CancellationToken ct)
    {
        try
        {
            if (ParseConnect(connect) is not (var points, null))
            {
                return ToolErrorPayload.AsCallToolResult(new InvalidArgument("connect", "connect is [[lat,lon],[lat,lon]]"), JsonOptions);
            }

            var (result, png) = await inner.InvokeAsync(
                new NavigabilityGridToolRequest(south, west, north, east, cellMeters, draftMeters, safetyContourMeters, hazardDepthMeters, includeHazards, points, format, includeImage),
                ct).ConfigureAwait(false);
            if (!result.TryGetValue(out var value))
            {
                result.TryGetError(out var error);
                return ToolErrorPayload.AsCallToolResult(error!, JsonOptions);
            }

            var text = JsonSerializer.Serialize(value, NavigabilityGridJsonContext.Default.NavigabilityGridResult);
            List<ContentBlock> content = [new TextContentBlock { Text = text }];
            if (value.File is { } file)
            {
                // For a client that can't read the server's files (#1615).
                content.Add(new ResourceLinkBlock
                {
                    Uri = file.Uri,
                    Name = file.Uri[(file.Uri.LastIndexOf('/') + 1)..],
                    Description = file.Layout,
                    MimeType = S100McpTools.NavigabilityGridMimeType,
                    Size = file.Bytes,
                });
            }

            if (png is not null)
            {
                content.Add(ImageContentBlock.FromBytes(png, "image/png"));
            }

            return new CallToolResult { Content = content, IsError = false };
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

    // [[lat,lon],[lat,lon]]; a string holding that JSON is accepted too, as the other tools take polylines.
    private static (IReadOnlyList<GeoPoint>? Points, string? Error) ParseConnect(JsonElement? connect)
    {
        if (connect is not { } element || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return (null, null);
        }

        try
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                using var document = JsonDocument.Parse(element.GetString()!);
                return ParseConnect(document.RootElement.Clone());
            }

            if (element.ValueKind != JsonValueKind.Array)
            {
                return (null, "not an array");
            }

            var points = new List<GeoPoint>();
            foreach (var pair in element.EnumerateArray())
            {
                if (pair.ValueKind != JsonValueKind.Array || pair.GetArrayLength() != 2)
                {
                    return (null, "not a [lat,lon] pair");
                }

                points.Add(new GeoPoint(pair[0].GetDouble(), pair[1].GetDouble()));
            }

            return (points, null);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException)
        {
            return (null, ex.Message);
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(McpJson.Options)
        {
            TypeInfoResolver = JsonTypeInfoResolver.Combine(NavigabilityGridJsonContext.Default, McpJson.TypeInfoResolver),
        };
        options.MakeReadOnly();
        return options;
    }
}

/// <summary>Source-generated metadata for <c>get_navigability_grid</c>'s result and its error.</summary>
[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(NavigabilityGridResult))]
[JsonSerializable(typeof(GridTooLarge))]
internal sealed partial class NavigabilityGridJsonContext : JsonSerializerContext;
