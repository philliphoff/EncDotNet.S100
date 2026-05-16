using System.Collections.Immutable;
using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="ListDatasetsTool"/>.
/// </summary>
/// <param name="Spec">Optional spec filter; <c>null</c> matches all specs.</param>
/// <param name="IntersectsBounds">Optional bbox filter; only datasets whose bounds overlap are returned.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Page size; clamped to 1..500.</param>
public sealed record ListDatasetsRequest(
    [property: Description("Optional spec filter (e.g. \"S-101/1.2.0\"); null matches every spec.")] SpecRef? Spec = null,
    [property: Description("Optional WGS-84 bounding box; only datasets whose bounds intersect this rectangle are returned.")] BoundingBox? IntersectsBounds = null,
    [property: Description("Zero-based page index.")] int Page = 0,
    [property: Description("Page size; the tool clamps the effective value to the range 1..500.")] int PageSize = 50);

/// <summary>Per-dataset summary returned by <see cref="ListDatasetsTool"/>.</summary>
/// <param name="Id">Stable dataset identifier; pass back into other tools.</param>
/// <param name="Spec">Product specification (name and edition) the dataset declares.</param>
/// <param name="Bounds">Geographic extent of the dataset (decimal degrees, WGS-84).</param>
/// <param name="TimeRange">UTC time interval covered by the dataset; null for static products such as S-102.</param>
public sealed record DatasetSummary(
    [property: Description("Stable dataset identifier; pass back into other tools.")] DatasetId Id,
    [property: Description("Product specification (name and edition) the dataset declares.")] SpecRef Spec,
    [property: Description("Geographic extent of the dataset in decimal degrees, WGS-84.")] BoundingBox Bounds,
    [property: Description("UTC time interval covered by the dataset; null for static products such as S-102.")] TimeRange? TimeRange);

/// <summary>Result of <see cref="ListDatasetsTool"/>.</summary>
/// <param name="Datasets">Page of dataset summaries, in catalog order.</param>
/// <param name="Page">Zero-based page index actually returned (post-clamp).</param>
/// <param name="PageSize">Page size actually applied (post-clamp to 1..500).</param>
/// <param name="TotalCount">Total number of datasets matching the filter, across all pages.</param>
/// <param name="HasMore">True when more pages exist beyond the one returned.</param>
public sealed record ListDatasetsResult(
    [property: Description("Page of dataset summaries, in catalog order.")] ImmutableArray<DatasetSummary> Datasets,
    [property: Description("Zero-based page index actually returned (post-clamp).")] int Page,
    [property: Description("Page size actually applied (post-clamp to 1..500).")] int PageSize,
    [property: Description("Total number of datasets matching the filter, across all pages.")] int TotalCount,
    [property: Description("True when more pages exist beyond the one returned.")] bool HasMore);

/// <summary>
/// Lists datasets currently loaded in an <see cref="IDatasetCatalog"/>,
/// optionally filtered by spec and bounding-box intersection.
/// </summary>
public sealed class ListDatasetsTool
{
    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="ListDatasetsTool"/>.</summary>
    public ListDatasetsTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<ListDatasetsResult>> InvokeAsync(
        ListDatasetsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var page = Math.Max(0, request.Page);

        var snapshot = _catalog.Datasets;
        var filtered = ImmutableArray.CreateBuilder<DatasetSummary>();
        foreach (var dataset in snapshot)
        {
            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            if (request.IntersectsBounds is { } bbox && !Intersects(dataset.Bounds, bbox))
            {
                continue;
            }

            filtered.Add(new DatasetSummary(dataset.Id, dataset.Spec, dataset.Bounds, dataset.TimeRange));
        }

        var totalCount = filtered.Count;
        var skip = page * pageSize;
        var take = Math.Max(0, Math.Min(pageSize, totalCount - skip));
        var pageBuilder = ImmutableArray.CreateBuilder<DatasetSummary>(take);
        for (var i = 0; i < take; i++)
        {
            pageBuilder.Add(filtered[skip + i]);
        }

        var hasMore = skip + take < totalCount;
        var result = new ListDatasetsResult(
            pageBuilder.MoveToImmutable(),
            page,
            pageSize,
            totalCount,
            hasMore);

        return Task.FromResult(ToolResult<ListDatasetsResult>.Ok(result));
    }

    private static bool SpecMatches(SpecRef actual, SpecRef filter)
    {
        // Match on name; treat a default (unset) edition in the filter as
        // "any edition of this spec".
        if (!string.Equals(actual.Name, filter.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return filter.Edition == default || actual.Edition == filter.Edition;
    }

    private static bool Intersects(BoundingBox a, BoundingBox b) =>
        a.WestLongitude <= b.EastLongitude
        && a.EastLongitude >= b.WestLongitude
        && a.SouthLatitude <= b.NorthLatitude
        && a.NorthLatitude >= b.SouthLatitude;
}
