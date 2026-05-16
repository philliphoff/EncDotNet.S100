using System.ComponentModel;
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
public abstract record ToolError(
    [property: Description("Stable error code; safe to switch on.")] string Code,
    [property: Description("Human-readable error message; not localised.")] string Message);

/// <summary>The named dataset is not present in the catalog snapshot.</summary>
/// <param name="Id">The dataset identifier that could not be resolved.</param>
[Description("Raised when the requested datasetId is not present in the current catalog snapshot.")]
public sealed record DatasetNotFound(
    [property: Description("The dataset identifier that could not be resolved.")] DatasetId Id) : ToolError(
    "dataset_not_found",
    $"Dataset '{Id}' is not present in the catalog.");

/// <summary>The dataset was unloaded after the catalog snapshot was taken but before the read completed.</summary>
/// <param name="Id">The dataset whose coverage handle was disposed mid-read.</param>
[Description("Raised when a coverage handle was disposed between the catalog snapshot and the read; safe to retry once the dataset is reopened.")]
public sealed record DatasetClosedDuringQuery(
    [property: Description("The dataset whose coverage handle was disposed mid-read.")] DatasetId Id) : ToolError(
    "dataset_closed_during_query",
    $"Dataset '{Id}' was closed while the query was in flight; retry once the dataset is reopened.");

/// <summary>No loaded dataset of the requested spec covers the supplied lat/lon.</summary>
/// <param name="Latitude">Requested latitude (decimal degrees, WGS-84).</param>
/// <param name="Longitude">Requested longitude (decimal degrees, WGS-84).</param>
[Description("Raised when no loaded dataset's bounds contain the requested point (decimal degrees, WGS-84).")]
public sealed record NoDatasetCoversPoint(
    [property: Description("Requested latitude in decimal degrees, WGS-84.")] double Latitude,
    [property: Description("Requested longitude in decimal degrees, WGS-84.")] double Longitude) : ToolError(
    "no_dataset_covers_point",
    $"No loaded dataset's bounds contain the point ({Latitude}, {Longitude}).");

/// <summary>The named feature is not present in the named dataset.</summary>
/// <param name="Id">The dataset that was searched.</param>
/// <param name="FeatureId">The feature identifier that could not be located.</param>
[Description("Raised when the requested featureId is not present in the named dataset.")]
public sealed record FeatureNotFound(
    [property: Description("The dataset that was searched.")] DatasetId Id,
    [property: Description("The feature identifier that could not be located.")] string FeatureId) : ToolError(
    "feature_not_found",
    $"Feature '{FeatureId}' is not present in dataset '{Id}'.");

/// <summary>The tool does not (yet) support the requested spec.</summary>
/// <param name="Spec">The product specification carried by the dataset.</param>
/// <param name="Tool">The tool that has no end-to-end implementation for this spec.</param>
[Description("Raised when the tool has no end-to-end implementation for the dataset's product specification.")]
public sealed record SpecNotSupportedForTool(
    [property: Description("The product specification carried by the dataset.")] SpecRef Spec,
    [property: Description("Name of the tool that has no end-to-end implementation for this spec.")] string Tool) : ToolError(
    "spec_not_supported_for_tool",
    $"Spec '{Spec}' is not supported by tool '{Tool}'.");

/// <summary>
/// The requested point falls outside the spatial bounds of every loaded
/// dataset of the requested spec. Distinguished from
/// <see cref="NoDatasetCoversPoint"/> in cases where no dataset of the
/// requested spec is loaded at all — callers may want to retry once a
/// dataset is loaded — by carrying the dataset spec.
/// </summary>
/// <param name="Spec">The product specification that was searched.</param>
/// <param name="Latitude">Requested latitude (decimal degrees, WGS-84).</param>
/// <param name="Longitude">Requested longitude (decimal degrees, WGS-84).</param>
[Description("Raised when the requested point lies outside the bounds of every loaded dataset of the requested spec.")]
public sealed record OutOfBounds(
    [property: Description("The product specification that was searched.")] SpecRef Spec,
    [property: Description("Requested latitude in decimal degrees, WGS-84.")] double Latitude,
    [property: Description("Requested longitude in decimal degrees, WGS-84.")] double Longitude) : ToolError(
    "out_of_bounds",
    $"Point ({Latitude}, {Longitude}) is outside the bounds of every loaded {Spec.Name} dataset.");

/// <summary>
/// The grid cell at the resolved (row, col) and time step carries the
/// spec-defined no-data fill value. The cell index and time step are
/// surfaced in the message so callers can correlate with the source grid.
/// </summary>
/// <param name="Id">The dataset that produced the no-data cell.</param>
/// <param name="Row">Zero-based row index of the resolved cell.</param>
/// <param name="Column">Zero-based column index of the resolved cell.</param>
/// <param name="Time">UTC instant of the selected time step, or null for static products.</param>
[Description("Raised when the resolved grid cell carries the spec-defined no-data fill value (e.g. 1,000,000 in S-102).")]
public sealed record NoDataAtPoint(
    [property: Description("The dataset that produced the no-data cell.")] DatasetId Id,
    [property: Description("Zero-based row index of the resolved cell in the source grid.")] int Row,
    [property: Description("Zero-based column index of the resolved cell in the source grid.")] int Column,
    [property: Description("UTC instant of the selected time step, or null for static products.")] DateTime? Time) : ToolError(
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
/// <param name="Spec">The product specification carried by the dataset.</param>
/// <param name="Tool">The tool that supports the spec but not this particular variant.</param>
/// <param name="Reason">Single-sentence reason explaining what is not yet wired.</param>
[Description("Raised when the spec is supported by the tool, but this specific dataset shape is not yet wired (e.g. S-104 data coding format other than 2).")]
public sealed record NotSupportedYet(
    [property: Description("The product specification carried by the dataset.")] SpecRef Spec,
    [property: Description("Name of the tool that supports the spec but not this particular variant.")] string Tool,
    [property: Description("Single-sentence reason explaining what is not yet wired.")] string Reason) : ToolError(
    "not_supported_yet",
    $"Spec '{Spec}' is supported by tool '{Tool}', but: {Reason}.");
