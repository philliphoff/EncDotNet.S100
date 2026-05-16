using System.Collections.Immutable;
using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="FindAtTool"/>.
/// </summary>
/// <param name="Latitude">Query latitude (decimal degrees, WGS-84). Must be in <c>[-90, 90]</c>. Ignored when <paramref name="Query"/> is supplied.</param>
/// <param name="Longitude">Query longitude (decimal degrees, WGS-84). Must be in <c>[-180, 180]</c>. Ignored when <paramref name="Query"/> is supplied.</param>
/// <param name="Spec">Optional spec filter; <c>null</c> matches every spec.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Page size; clamped to 1..500.</param>
/// <param name="Query">
/// Optional richer geographic query (point, bbox, polygon, polyline).
/// When supplied, takes precedence over <paramref name="Latitude"/> /
/// <paramref name="Longitude"/>. For point queries the semantics match
/// the legacy lat/lon path; for bbox/polygon/polyline the tool returns
/// every dataset whose declared bounding box intersects the query's
/// coarse bounding box.
/// </param>
public sealed record FindAtRequest(
    [property: Description("Query latitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-90, 90].")] double Latitude,
    [property: Description("Query longitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-180, 180].")] double Longitude,
    [property: Description("Optional spec filter; null matches every spec.")] SpecRef? Spec = null,
    [property: Description("Zero-based page index into the result set.")] int Page = 0,
    [property: Description("Maximum datasets per page; clamped to the range 1..500.")] int PageSize = 50,
    [property: Description("Optional spatial query envelope (point / box / polygon / polyline). When supplied, supersedes the lat/lon point.")] GeoQuery? Query = null);

/// <summary>Result of <see cref="FindAtTool"/>.</summary>
/// <param name="Datasets">Datasets whose declared bounding box contains or intersects the query, in catalog insertion order.</param>
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
/// Returns every loaded dataset whose declared bounding box intersects
/// the supplied geographic query.
/// </summary>
/// <remarks>
/// <para>
/// Intersection is evaluated against the dataset's declared bounding
/// box only — there is no per-cell coverage check, no NoData masking,
/// and no vector geometry intersection.
/// </para>
/// <para>
/// For point queries, a positive result means the point lies within
/// the rectangle <c>[SouthLatitude, NorthLatitude] × [WestLongitude,
/// EastLongitude]</c>. Actual cell coverage may differ; callers that
/// need the actual value at the point should follow up with
/// <see cref="SampleCoverageTool"/>.
/// </para>
/// <para>
/// For bbox, polygon, and polyline queries the tool projects the
/// query to its coarse bounding box (polyline corridors are inflated
/// by <c>CorridorWidthMeters</c> using an equirectangular
/// approximation) and returns datasets whose bounds intersect that
/// rectangle.
/// </para>
/// <para>
/// Bounds are treated as inclusive on every edge. Antimeridian-
/// crossing dataset bounding boxes (stored with <c>West &gt; East</c>)
/// will not match; see S-100 Part 10b §6.2.
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

        // Resolve the spatial query. If the caller supplied a typed
        // GeoQuery it wins; otherwise we synthesise a point query from
        // the legacy lat/lon fields.
        GeoQuery query;
        if (request.Query is { } supplied)
        {
            if (GeoQueryValidator.Validate(supplied) is { } err)
            {
                return Task.FromResult(ToolResult<FindAtResult>.Err(err));
            }
            query = supplied;
        }
        else
        {
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

            query = new GeoQuery.Point(new GeoPoint(request.Latitude, request.Longitude));
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

            if (!MatchesQuery(dataset.Bounds, query))
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

    private static bool MatchesQuery(BoundingBox bounds, GeoQuery query) => query switch
    {
        GeoQuery.Point p => SpatialPredicates.Contains(bounds, p.Value),
        _ => SpatialPredicates.Intersects(bounds, query),
    };
}
