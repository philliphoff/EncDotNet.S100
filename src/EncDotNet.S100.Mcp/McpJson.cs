using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Mcp.Tools;
using EncDotNet.S100.Mcp.Tools.Mutable;
using ModelContextProtocol;

namespace EncDotNet.S100.Mcp;

/// <summary>
/// The JSON options shared by the MCP tool wrappers: web defaults, compact,
/// nulls omitted, with source-generated metadata for every tool result and
/// <see cref="ToolError"/> (issue #764), chained with the MCP SDK's own
/// resolver for the protocol types.
/// </summary>
internal static class McpJson
{
    /// <summary>The shared options.</summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    /// <summary>Serializes <paramref name="value"/> by its run-time type, which must be known to <see cref="Options"/>.</summary>
    public static string SerializeByRuntimeType<T>(T value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(value, options.GetTypeInfo(value?.GetType() ?? typeof(T)));

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            TypeInfoResolver = JsonTypeInfoResolver.Combine(
                McpToolsJsonContext.Default.WithAddedModifier(AddSampledValuePolymorphism),
                McpJsonUtilities.DefaultOptions.TypeInfoResolver),
        };
        options.MakeReadOnly();
        return options;
    }

    // SampledValue is declared without polymorphism attributes, so the
    // discriminator is attached here.
    private static void AddSampledValuePolymorphism(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type != typeof(SampledValue))
            return;

        typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$kind",
            IgnoreUnrecognizedTypeDiscriminators = true,
            UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToBaseType,
            DerivedTypes =
            {
                new JsonDerivedType(typeof(DepthSample), "depth"),
                new JsonDerivedType(typeof(WaterLevelSample), "water_level"),
                new JsonDerivedType(typeof(WaterLevelStationSample), "water_level_station"),
                new JsonDerivedType(typeof(SurfaceCurrentSample), "surface_current"),
                new JsonDerivedType(typeof(SurfaceCurrentStationSample), "surface_current_station"),
            },
        };
    }
}

/// <summary>Source-generated metadata for the MCP tool results, errors and parameters.</summary>
// Read-only tool results.
[JsonSerializable(typeof(ListDatasetsResult))]
[JsonSerializable(typeof(DescribeFeatureResult))]
[JsonSerializable(typeof(DescribeFeatureTypeResult))]
[JsonSerializable(typeof(SampleCoverageResult))]
[JsonSerializable(typeof(FindAtResult))]
[JsonSerializable(typeof(IdentifyFeaturesResult))]
[JsonSerializable(typeof(NearestFeaturesResult))]
[JsonSerializable(typeof(QueryFeaturesResult))]
[JsonSerializable(typeof(CountFeaturesResult))]
[JsonSerializable(typeof(SearchFeaturesResult))]
[JsonSerializable(typeof(SampleCoverageAlongResult))]
[JsonSerializable(typeof(ListSpecsResult))]
[JsonSerializable(typeof(ListTimeStepsResult))]
// Mutating tool results.
[JsonSerializable(typeof(OpenDatasetResult))]
[JsonSerializable(typeof(CloseDatasetResult))]
[JsonSerializable(typeof(CloseAllDatasetsResult))]
[JsonSerializable(typeof(SetPaletteResult))]
[JsonSerializable(typeof(SetDisplayCategoryResult))]
[JsonSerializable(typeof(SetDisplayModeResult))]
[JsonSerializable(typeof(SetTimeStepResult))]
[JsonSerializable(typeof(SetViewportResult))]
[JsonSerializable(typeof(RenderToImageResult))]
// Sampled values, written polymorphically.
[JsonSerializable(typeof(DepthSample))]
[JsonSerializable(typeof(WaterLevelSample))]
[JsonSerializable(typeof(WaterLevelStationSample))]
[JsonSerializable(typeof(SurfaceCurrentSample))]
[JsonSerializable(typeof(SurfaceCurrentStationSample))]
// Errors, written by their concrete type.
[JsonSerializable(typeof(InvalidArgument))]
[JsonSerializable(typeof(DatasetNotFound))]
[JsonSerializable(typeof(DatasetClosedDuringQuery))]
[JsonSerializable(typeof(NoDatasetCoversPoint))]
[JsonSerializable(typeof(FeatureNotFound))]
[JsonSerializable(typeof(SpecNotSupportedForTool))]
[JsonSerializable(typeof(FeatureCatalogueNotAvailable))]
[JsonSerializable(typeof(FeatureTypeNotFound))]
[JsonSerializable(typeof(OutOfBounds))]
[JsonSerializable(typeof(NoDataAtPoint))]
[JsonSerializable(typeof(NotSupportedYet))]
[JsonSerializable(typeof(GeometryInvalid))]
[JsonSerializable(typeof(TimeOutOfRange))]
[JsonSerializable(typeof(HostNotReady))]
[JsonSerializable(typeof(DatasetLoadFailed))]
// Tool parameter types the SDK binds.
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonElement?))]
[JsonSerializable(typeof(DateTimeOffset?))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool?))]
[JsonSerializable(typeof(int?))]
[JsonSerializable(typeof(double?))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(bool))]
internal sealed partial class McpToolsJsonContext : JsonSerializerContext;
