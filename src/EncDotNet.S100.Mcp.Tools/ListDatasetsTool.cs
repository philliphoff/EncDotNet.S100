using System.Collections.Immutable;
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
    SpecRef? Spec = null,
    BoundingBox? IntersectsBounds = null,
    int Page = 0,
    int PageSize = 50);

/// <summary>Per-dataset summary returned by <see cref="ListDatasetsTool"/>.</summary>
public sealed record DatasetSummary(
    DatasetId Id,
    SpecRef Spec,
    BoundingBox Bounds,
    TimeRange? TimeRange);

/// <summary>Result of <see cref="ListDatasetsTool"/>.</summary>
public sealed record ListDatasetsResult(
    ImmutableArray<DatasetSummary> Datasets,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasMore);

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
