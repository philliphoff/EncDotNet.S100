using System.Collections.Immutable;
using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="FindAtTool"/>.
/// </summary>
/// <param name="Latitude">Query latitude (decimal degrees, WGS-84). Must be in <c>[-90, 90]</c>.</param>
/// <param name="Longitude">Query longitude (decimal degrees, WGS-84). Must be in <c>[-180, 180]</c>.</param>
/// <param name="Spec">Optional spec filter; <c>null</c> matches every spec.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Page size; clamped to 1..500.</param>
public sealed record FindAtRequest(
    [property: Description("Query latitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-90, 90].")] double Latitude,
    [property: Description("Query longitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-180, 180].")] double Longitude,
    [property: Description("Optional spec filter; null matches every spec.")] SpecRef? Spec = null,
    [property: Description("Zero-based page index into the result set.")] int Page = 0,
    [property: Description("Maximum datasets per page; clamped to the range 1..500.")] int PageSize = 50);

/// <summary>Result of <see cref="FindAtTool"/>.</summary>
/// <param name="Datasets">Datasets whose declared bounding box contains the query point, in catalog insertion order.</param>
/// <param name="Page">Echoed (and floored) page index.</param>
/// <param name="PageSize">Echoed (and clamped) page size.</param>
/// <param name="TotalCount">Total number of matching datasets across all pages.</param>
/// <param name="HasMore"><c>true</c> if additional pages remain.</param>
public sealed record FindAtResult(
    [property: Description("Datasets whose declared bounding box contains the query point, in catalog insertion order.")] ImmutableArray<DatasetSummary> Datasets,
    [property: Description("Echoed (and floored) zero-based page index.")] int Page,
    [property: Description("Echoed (and clamped) page size.")] int PageSize,
    [property: Description("Total number of matching datasets across all pages.")] int TotalCount,
    [property: Description("True if additional pages of matches remain after this one.")] bool HasMore);

/// <summary>
/// Returns every loaded dataset whose declared bounding box contains the
/// supplied latitude/longitude point.
/// </summary>
/// <remarks>
/// <para>
/// Containment is evaluated against the dataset's declared bounding box
/// only — there is no per-cell coverage check, no NoData masking, and no
/// vector geometry intersection. A positive result means the point lies
/// within the rectangle <c>[SouthLatitude, NorthLatitude] × [WestLongitude,
/// EastLongitude]</c>. Actual cell coverage may differ; callers that need
/// the actual value at the point should follow up with
/// <see cref="SampleCoverageTool"/>.
/// </para>
/// <para>
/// Bounds are treated as inclusive on every edge, matching
/// <see cref="SampleCoverageTool"/>'s contains check.
/// </para>
/// <para>
/// The current implementation assumes <c>WestLongitude ≤ EastLongitude</c>,
/// matching <see cref="ListDatasetsTool"/>'s intersection helper. Datasets
/// whose bounding box crosses the antimeridian (stored with
/// <c>West &gt; East</c>) will not match. See S-100 Part 10b §6.2.
/// </para>
/// </remarks>
public sealed class FindAtTool
{
    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="FindAtTool"/>.</summary>
    public FindAtTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<FindAtResult>> InvokeAsync(
        FindAtRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (double.IsNaN(request.Latitude) || request.Latitude < -90.0 || request.Latitude > 90.0)
        {
            return Task.FromResult(ToolResult<FindAtResult>.Err(
                new InvalidArgument("latitude", $"value {request.Latitude} is outside the WGS-84 range [-90, 90]")));
        }

        if (double.IsNaN(request.Longitude) || request.Longitude < -180.0 || request.Longitude > 180.0)
        {
            return Task.FromResult(ToolResult<FindAtResult>.Err(
                new InvalidArgument("longitude", $"value {request.Longitude} is outside the WGS-84 range [-180, 180]")));
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var page = Math.Max(0, request.Page);

        var snapshot = _catalog.Datasets;
        var matched = ImmutableArray.CreateBuilder<DatasetSummary>();
        foreach (var dataset in snapshot)
        {
            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            if (!Contains(dataset.Bounds, request.Latitude, request.Longitude))
            {
                continue;
            }

            matched.Add(new DatasetSummary(dataset.Id, dataset.Spec, dataset.Bounds, dataset.TimeRange));
        }

        var totalCount = matched.Count;
        var skip = page * pageSize;
        var take = Math.Max(0, Math.Min(pageSize, totalCount - skip));
        var pageBuilder = ImmutableArray.CreateBuilder<DatasetSummary>(take);
        for (var i = 0; i < take; i++)
        {
            pageBuilder.Add(matched[skip + i]);
        }

        var hasMore = skip + take < totalCount;
        var result = new FindAtResult(
            pageBuilder.MoveToImmutable(),
            page,
            pageSize,
            totalCount,
            hasMore);

        return Task.FromResult(ToolResult<FindAtResult>.Ok(result));
    }

    private static bool SpecMatches(SpecRef actual, SpecRef filter)
    {
        if (!string.Equals(actual.Name, filter.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return filter.Edition == default || actual.Edition == filter.Edition;
    }

    private static bool Contains(BoundingBox b, double lat, double lon) =>
        lat >= b.SouthLatitude
        && lat <= b.NorthLatitude
        && lon >= b.WestLongitude
        && lon <= b.EastLongitude;
}
