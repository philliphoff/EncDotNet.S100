using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Base type for typed tool errors.
/// </summary>
/// <param name="Code">Stable error code; safe to switch on.</param>
/// <param name="Message">Human-readable message; not localised.</param>
/// <remarks>
/// Tool implementations never throw into their callers. Every failure
/// case is reified as a concrete <see cref="ToolError"/> wrapped in
/// <see cref="ToolResult{T}.Err"/>. This keeps the eventual MCP-error
/// wire format flexible while giving in-process callers a typed
/// surface they can match on.
/// </remarks>
public abstract record ToolError(string Code, string Message);

/// <summary>The named dataset is not present in the catalog snapshot.</summary>
public sealed record DatasetNotFound(DatasetId Id) : ToolError(
    "dataset_not_found",
    $"Dataset '{Id}' is not present in the catalog.");

/// <summary>The dataset was unloaded after the catalog snapshot was taken but before the read completed.</summary>
public sealed record DatasetClosedDuringQuery(DatasetId Id) : ToolError(
    "dataset_closed_during_query",
    $"Dataset '{Id}' was closed while the query was in flight; retry once the dataset is reopened.");

/// <summary>No loaded dataset of the requested spec covers the supplied lat/lon.</summary>
public sealed record NoDatasetCoversPoint(double Latitude, double Longitude) : ToolError(
    "no_dataset_covers_point",
    $"No loaded dataset's bounds contain the point ({Latitude}, {Longitude}).");

/// <summary>The named feature is not present in the named dataset.</summary>
public sealed record FeatureNotFound(DatasetId Id, string FeatureId) : ToolError(
    "feature_not_found",
    $"Feature '{FeatureId}' is not present in dataset '{Id}'.");

/// <summary>The tool does not (yet) support the requested spec.</summary>
public sealed record SpecNotSupportedForTool(SpecRef Spec, string Tool) : ToolError(
    "spec_not_supported_for_tool",
    $"Spec '{Spec}' is not supported by tool '{Tool}'.");
