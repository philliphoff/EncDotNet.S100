using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// Builds <see cref="McpServerTool"/> wrappers around the MCP-1 tools
/// (<see cref="ListDatasetsTool"/>, <see cref="DescribeFeatureTool"/>,
/// <see cref="SampleCoverageTool"/>, <see cref="FindAtTool"/>),
/// translating <see cref="ToolResult{T}"/> outcomes into MCP
/// <see cref="CallToolResult"/> payloads.
/// </summary>
/// <remarks>
/// <para>
/// On success, the result <c>Value</c> is serialised to JSON and
/// attached as a single <see cref="TextContentBlock"/>.
/// </para>
/// <para>
/// On failure, the wrapper returns <c>isError = true</c> with a
/// structured JSON payload <c>{ "code", "message", "details" }</c>;
/// <c>details</c> serialises any non-base members of the concrete
/// <see cref="ToolError"/> subtype so callers can recover dataset
/// IDs, feature IDs, or sample coordinates without parsing the
/// human-readable message.
/// </para>
/// </remarks>
internal static class S100McpServerToolFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver
        {
            Modifiers =
            {
                static typeInfo =>
                {
                    if (typeInfo.Type == typeof(SampledValue))
                    {
                        typeInfo.PolymorphismOptions = new System.Text.Json.Serialization.Metadata.JsonPolymorphismOptions
                        {
                            TypeDiscriminatorPropertyName = "$kind",
                            IgnoreUnrecognizedTypeDiscriminators = true,
                            UnknownDerivedTypeHandling = System.Text.Json.Serialization.JsonUnknownDerivedTypeHandling.FallBackToBaseType,
                            DerivedTypes =
                            {
                                new System.Text.Json.Serialization.Metadata.JsonDerivedType(typeof(DepthSample), "depth"),
                            },
                        };
                    }
                },
            },
        },
    };

    public static IEnumerable<McpServerTool> CreateTools(
        ListDatasetsTool listDatasets,
        DescribeFeatureTool describeFeature,
        SampleCoverageTool sampleCoverage,
        FindAtTool findAt)
    {
        yield return CreateListDatasetsTool(listDatasets);
        yield return CreateDescribeFeatureTool(describeFeature);
        yield return CreateSampleCoverageTool(sampleCoverage);
        yield return CreateFindAtTool(findAt);
    }

    private static McpServerTool CreateListDatasetsTool(ListDatasetsTool inner)
    {
        var description =
            "Lists the S-100 datasets currently loaded in the host (viewer or CLI). " +
            "Supports optional spec and bounding-box filters and pagination. " +
            "Returns dataset IDs, spec, bounds, and time range.";

        var del = ([Description("Optional spec filter (e.g. \"S-101/1.2.0\"); null matches every spec.")] string? spec = null,
                   [Description("Optional bounding box south latitude (decimal degrees, WGS-84). Pass null to omit the bbox filter; if any one of south/west/north/east is supplied, all four must be.")] double? south = null,
                   [Description("Optional bounding box west longitude.")] double? west = null,
                   [Description("Optional bounding box north latitude.")] double? north = null,
                   [Description("Optional bounding box east longitude.")] double? east = null,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new ListDatasetsRequest(
                        ParseSpec(spec),
                        ParseBox(south, west, north, east),
                        page,
                        pageSize),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = "list_datasets",
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateDescribeFeatureTool(DescribeFeatureTool inner)
    {
        var description =
            "Returns spec, feature-type code, attributes (as JSON), and xlink-resolved cross-references " +
            "for a single feature in a loaded dataset. Currently supports S-124 (Navigational Warnings); " +
            "other vector specs return spec_not_supported_for_tool.";

        var del = ([Description("Stable dataset ID returned by list_datasets.")] string datasetId,
                   [Description("The feature's GML id within the dataset.")] string featureId,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new DescribeFeatureRequest(new DatasetId(datasetId), featureId),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = DescribeFeatureTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateSampleCoverageTool(SampleCoverageTool inner)
    {
        var description =
            "Samples a coverage product at a single latitude/longitude, returning the nearest grid " +
            "cell's value. Currently supports S-102 (Bathymetric Surfaces); S-104 and S-111 return " +
            "spec_not_supported_for_tool.";

        var del = ([Description("Spec of the coverage to sample (e.g. \"S-102/2.1.0\").")] string spec,
                   [Description("Sample latitude in decimal degrees, WGS-84.")] double latitude,
                   [Description("Sample longitude in decimal degrees, WGS-84.")] double longitude,
                   [Description("Optional time selector (ISO-8601, time-varying products only).")] DateTimeOffset? time = null,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new SampleCoverageRequest(
                        ParseSpec(spec) ?? throw new ArgumentException("spec is required.", nameof(spec)),
                        latitude,
                        longitude,
                        time),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = SampleCoverageTool.Name,
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static McpServerTool CreateFindAtTool(FindAtTool inner)
    {
        var description =
            "Returns every dataset currently loaded in the host (viewer or CLI) whose declared " +
            "bounding box contains the supplied lat/lon point. Coordinates are decimal degrees, " +
            "WGS-84. Containment is bbox-only — a positive result means the point lies inside the " +
            "dataset's declared rectangle, not that the point has actual cell coverage (call " +
            "sample_coverage to read a value). Optionally filtered by spec. Returns dataset IDs, " +
            "spec, bounds, and time range, with pagination.";

        var del = ([Description("Query latitude in decimal degrees, WGS-84. Must be in [-90, 90].")] double latitude,
                   [Description("Query longitude in decimal degrees, WGS-84. Must be in [-180, 180].")] double longitude,
                   [Description("Optional spec filter (e.g. \"S-101/1.2.0\"); null matches every spec.")] string? spec = null,
                   [Description("Zero-based page index.")] int page = 0,
                   [Description("Page size (clamped to 1..500).")] int pageSize = 50,
                   CancellationToken ct = default) =>
            DispatchAsync(() =>
                inner.InvokeAsync(
                    new FindAtRequest(
                        latitude,
                        longitude,
                        ParseSpec(spec),
                        page,
                        pageSize),
                    ct));

        return McpServerTool.Create(del, new McpServerToolCreateOptions
        {
            Name = "find_at",
            Description = description,
            SerializerOptions = JsonOptions,
        });
    }

    private static SpecRef? ParseSpec(string? spec)
        => string.IsNullOrWhiteSpace(spec) ? null : SpecRef.Parse(spec);

    private static BoundingBox? ParseBox(double? south, double? west, double? north, double? east)
    {
        if (south is null && west is null && north is null && east is null) return null;
        if (south is null || west is null || north is null || east is null)
        {
            throw new ArgumentException(
                "When supplying a bbox filter, all of south, west, north and east must be provided.");
        }
        return new BoundingBox(south.Value, west.Value, north.Value, east.Value);
    }

    /// <summary>
    /// Translates a <see cref="ToolResult{T}"/> Task into the
    /// <see cref="CallToolResult"/> shape the MCP SDK expects.
    /// Catches any unexpected exception and surfaces it as a generic
    /// <c>internal_error</c> error envelope so a tool never throws
    /// into the SDK dispatcher.
    /// </summary>
    private static async Task<CallToolResult> DispatchAsync<T>(Func<Task<ToolResult<T>>> resultFactory)
    {
        try
        {
            var result = await resultFactory().ConfigureAwait(false);
            if (result.TryGetValue(out var value))
            {
                return Success(value);
            }

            result.TryGetError(out var err);
            return Failure(err!);
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

    private static CallToolResult Success<T>(T value)
    {
        var runtimeType = value?.GetType() ?? typeof(T);
        var json = JsonSerializer.Serialize(value, runtimeType, JsonOptions);
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
        var payload = new JsonObject
        {
            ["code"] = error.Code,
            ["message"] = error.Message,
            ["details"] = SerializeDetails(error),
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

    /// <summary>
    /// Serialises the concrete <see cref="ToolError"/> via System.Text.Json
    /// and strips the base <c>Code</c>/<c>Message</c> properties so the
    /// remaining members surface as <c>details</c> without duplication.
    /// </summary>
    private static JsonObject SerializeDetails(ToolError error)
    {
        var node = JsonSerializer.SerializeToNode(error, error.GetType(), JsonOptions) as JsonObject
            ?? new JsonObject();
        node.Remove("code");
        node.Remove("message");
        node.Remove("Code");
        node.Remove("Message");
        return node;
    }
}
