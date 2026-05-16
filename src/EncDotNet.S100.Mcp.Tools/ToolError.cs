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

/// <summary>
/// A tool argument failed validation (e.g. latitude or longitude
/// outside the valid WGS-84 range). The <paramref name="Parameter"/>
/// names the request property that was rejected and
/// <paramref name="Reason"/> describes why.
/// </summary>
public sealed record InvalidArgument(string Parameter, string Reason) : ToolError(
    "invalid_argument",
    $"Invalid argument '{Parameter}': {Reason}.");

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

/// <summary>
/// The requested point falls outside the spatial bounds of every loaded
/// dataset of the requested spec. Distinguished from
/// <see cref="NoDatasetCoversPoint"/> in cases where no dataset of the
/// requested spec is loaded at all — callers may want to retry once a
/// dataset is loaded — by carrying the dataset spec.
/// </summary>
public sealed record OutOfBounds(SpecRef Spec, double Latitude, double Longitude) : ToolError(
    "out_of_bounds",
    $"Point ({Latitude}, {Longitude}) is outside the bounds of every loaded {Spec.Name} dataset.");

/// <summary>
/// The grid cell at the resolved (row, col) and time step carries the
/// spec-defined no-data fill value. The cell index and time step are
/// surfaced in the message so callers can correlate with the source grid.
/// </summary>
public sealed record NoDataAtPoint(
    DatasetId Id,
    int Row,
    int Column,
    DateTime? Time) : ToolError(
    "no_data_at_point",
    Time is null
        ? $"Cell ({Row}, {Column}) in dataset '{Id}' carries the no-data fill value."
        : $"Cell ({Row}, {Column}) at {Time:O} in dataset '{Id}' carries the no-data fill value.");

/// <summary>
/// The tool understands the requested spec but cannot yet handle this
/// specific shape of dataset (e.g. S-104 data coding format other than 2).
/// Distinguished from <see cref="SpecNotSupportedForTool"/> in that the
/// spec itself is supported — only this particular variant isn't yet.
/// </summary>
public sealed record NotSupportedYet(SpecRef Spec, string Tool, string Reason) : ToolError(
    "not_supported_yet",
    $"Spec '{Spec}' is supported by tool '{Tool}', but: {Reason}.");
