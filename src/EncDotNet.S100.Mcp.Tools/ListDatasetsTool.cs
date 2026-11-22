using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.S57;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="ListDatasetsTool"/>.
/// </summary>
/// <param name="Spec">Optional spec filter; <c>null</c> matches all specs.</param>
/// <param name="Query">
/// Optional spatial filter (#1609, which folded <c>find_at</c> in here): a
/// point keeps the datasets whose declared bounding box contains it; a box,
/// polygon or polyline keeps those whose bounding box intersects it.
/// </param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Page size; clamped to 1..500.</param>
public sealed record ListDatasetsRequest(
    [property: Description("Optional spec filter (e.g. \"S-101/1.2.0\"); null matches every spec.")] SpecRef? Spec = null,
    [property: Description("Optional spatial filter (point / box / polygon / polyline): a point keeps the datasets whose declared bounding box contains it, any other shape those whose bounding box intersects it.")] GeoQuery? Query = null,
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
    [property: Description("UTC time interval covered by the dataset; null for static products such as S-102.")] TimeRange? TimeRange,
    [property: Description("NATO Additional Military Layers facts when the dataset is AML (military overlay data, not a chart); otherwise null.")] AmlDatasetSummary? Aml = null);

/// <summary>Result of <see cref="ListDatasetsTool"/>.</summary>
/// <param name="Datasets">Page of dataset summaries, in catalog order.</param>
/// <param name="Page">Zero-based page index actually returned (post-clamp).</param>
/// <param name="PageSize">Page size actually applied (post-clamp to 1..500).</param>
/// <param name="TotalCount">Total number of datasets matching the filter, across all pages.</param>
/// <param name="HasMore">True when more pages exist beyond the one returned.</param>
public sealed record ListDatasetsResult(
    [property: Description("Page of dataset summaries, in catalog order.")] IReadOnlyList<DatasetSummary> Datasets,
    [property: Description("Zero-based page index actually returned (post-clamp).")] int Page,
    [property: Description("Page size actually applied (post-clamp to 1..500).")] int PageSize,
    [property: Description("Total number of datasets matching the filter, across all pages.")] int TotalCount,
    [property: Description("True when more pages exist beyond the one returned.")] bool HasMore,
    [property: Description("The highest protective marking of the AML datasets matching the filter (\"+N\" when others tie), or null when none is AML. Say it before describing the map.")] string? HighestMarking = null);

/// <summary>
/// Lists datasets currently loaded in an <see cref="IDatasetCatalog"/>,
/// optionally filtered by spec and a spatial query.
/// </summary>
/// <remarks>
/// The spatial test is against each dataset's declared bounding box only: a
/// match means the query falls inside the dataset's rectangle, not that the
/// dataset has data there (sample reads the value). Antimeridian-crossing
/// dataset bounds (stored with West &gt; East) don't match.
/// </remarks>
public sealed class ListDatasetsTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "list_datasets";

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

        if (request.Query is { } query && GeoQueryValidator.Validate(query) is { } err)
        {
            return Task.FromResult(ToolResult<ListDatasetsResult>.Err(err));
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var page = Math.Max(0, request.Page);

        var snapshot = _catalog.Datasets;
        var filtered = new List<DatasetSummary>();
        foreach (var dataset in snapshot)
        {
            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            if (request.Query is { } q && !Matches(dataset.Bounds, q))
            {
                continue;
            }

            filtered.Add(new DatasetSummary(
                dataset.Id,
                dataset.Spec,
                dataset.Bounds,
                dataset.TimeRange,
                dataset.Data is AmlDatasetData aml
                    ? new AmlDatasetSummary(aml.Info.ProductCode, aml.Info.ProtectiveMarking, aml.Drawn, AmlReport.StatusOf(aml.Drawn))
                    : null));
        }

        var totalCount = filtered.Count;
        var skip = page * pageSize;
        var take = Math.Max(0, Math.Min(pageSize, totalCount - skip));
        var pageBuilder = new List<DatasetSummary>(take);
        for (var i = 0; i < take; i++)
        {
            pageBuilder.Add(filtered[skip + i]);
        }

        var hasMore = skip + take < totalCount;
        var highest = AmlProtectiveMarking.Highest(filtered.Select(d => d.Aml?.ProtectiveMarking));
        var result = new ListDatasetsResult(
            pageBuilder,
            page,
            pageSize,
            totalCount,
            hasMore,
            highest?.DisplayText);

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

    private static bool Matches(BoundingBox bounds, GeoQuery query) => query switch
    {
        GeoQuery.Point p => SpatialPredicates.Contains(bounds, p.Value),
        _ => SpatialPredicates.Intersects(bounds, query),
    };
}
