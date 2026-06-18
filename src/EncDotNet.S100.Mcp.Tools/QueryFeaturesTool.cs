using System.Collections.Immutable;
using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Spec;
using EncDotNet.S100.Mcp.Tools.Time;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="QueryFeaturesTool"/>.
/// </summary>
/// <param name="Query">
/// The geographic query. All four <see cref="GeoQuery"/> variants are
/// supported; intersection is computed against each feature's bounding
/// box (no full polygon intersection).
/// </param>
/// <param name="Spec">
/// Optional spec filter. When supplied, only loaded datasets whose
/// spec name (and optionally edition) match are queried. A
/// <c>default</c> edition matches every edition of the same spec name.
/// </param>
/// <param name="FeatureType">
/// Optional case-sensitive feature-type filter (the GML element local
/// name, e.g. <c>NavwarnPart</c>, <c>BuoyLateral</c>, <c>Berth</c>).
/// </param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Page size; clamped to 1..500.</param>
public sealed record QueryFeaturesRequest(
    [property: Description("Spatial query envelope (point / box / polygon / polyline). Intersection is computed against each feature's bounding box.")] GeoQuery Query,
    [property: Description("Optional spec filter; null matches every spec. A default edition matches every edition of the same spec name.")] SpecRef? Spec = null,
    [property: Description("Optional case-sensitive feature-type filter (the GML element local name, e.g. \"NavwarnPart\", \"BuoyLateral\"; for S-101 the feature-type acronym, e.g. \"LIGHTS\"); null returns every feature type.")] string? FeatureType = null,
    [property: Description("Optional temporal filter. When supplied, features whose fixedDateRange/periodicDateRange validity window is disjoint from the query window are excluded; features without validity metadata are always included.")] TimeQuery? Times = null,
    [property: Description("Zero-based page index into the result set.")] int Page = 0,
    [property: Description("Maximum features per page; clamped to the range 1..500.")] int PageSize = 50);

/// <summary>
/// Per-feature summary returned by <see cref="QueryFeaturesTool"/>.
/// </summary>
/// <param name="DatasetId">Dataset the feature belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureId">Stable feature identifier (<c>gml:id</c>; for S-101 the decimal RCID).</param>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="Bounds">Bounding box of the feature's geometry, or <c>null</c> if the feature carries no geometry.</param>
public sealed record FeatureMatch(
    DatasetId DatasetId,
    SpecRef Spec,
    string FeatureId,
    string FeatureType,
    BoundingBox? Bounds);

/// <summary>
/// A per-feature-type tally of the full (all-pages) match set, returned
/// by <see cref="QueryFeaturesTool"/> so callers can gauge a result's
/// shape before paging through it.
/// </summary>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="Count">Number of matching features of this type across all pages.</param>
public sealed record FeatureTypeBreakdown(
    [property: Description("Feature type code (GML element local name; for S-101 the feature-type acronym).")] string FeatureType,
    [property: Description("Number of matching features of this type across all pages.")] int Count);

/// <summary>Result of <see cref="QueryFeaturesTool"/>.</summary>
public sealed record QueryFeaturesResult(
    [property: Description("Matching features for the requested page, in catalog insertion order then per-dataset feature order.")] ImmutableArray<FeatureMatch> Features,
    [property: Description("Echoed (and floored) zero-based page index.")] int Page,
    [property: Description("Echoed (and clamped) page size.")] int PageSize,
    [property: Description("Total number of matching features across all pages.")] int TotalCount,
    [property: Description("True if additional pages remain after the current one.")] bool HasMore,
    [property: Description("Per-feature-type tally of the full (all-pages) match set, ordered by descending count then feature type. Lets a caller gauge the result shape before paging.")] ImmutableArray<FeatureTypeBreakdown> TypeBreakdown);


/// <summary>
/// Returns features from loaded S-100 vector datasets whose
/// geometry intersects the supplied geographic query.
/// </summary>
/// <remarks>
/// <para>
/// This tool works against every GML-encoded vector spec the codebase
/// supports — S-122, S-124, S-125, S-127, S-128, S-129, S-131,
/// S-201, S-411, S-421 — via the shared <see cref="IS100Feature"/>
/// abstraction, plus the ISO 8211-encoded S-101 (whose pipeline
/// <see cref="EncDotNet.S100.Pipelines.Vector.Feature"/> records
/// implement <see cref="IS100Feature"/> directly). For S-101
/// the <see cref="QueryFeaturesRequest.FeatureType"/> filter matches the
/// feature-type acronym (e.g. <c>LIGHTS</c>, <c>BOYLAT</c>) and each
/// <see cref="FeatureMatch.FeatureId"/> is the feature record's decimal
/// RCID. Coverage products (S-102, S-104, S-111) are not queried; use
/// <see cref="SampleCoverageTool"/> for those.
/// </para>
/// <para>
/// Intersection is computed at bounding-box precision per feature.
/// Features with no geometry at all (e.g. container-style features
/// such as <c>S131:Authority</c>) are not returned even when their
/// dataset's bounds intersect the query.
/// </para>
/// <para>
/// First-pass filtering uses the dataset-level bounding box; datasets
/// whose declared bounds are disjoint from the query bbox are skipped
/// without enumerating their features.
/// </para>
/// </remarks>
public sealed class QueryFeaturesTool
{
    /// <summary>Tool name used in <see cref="SpecNotSupportedForTool"/> errors.</summary>
    public const string Name = "query_features";

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="QueryFeaturesTool"/>.</summary>
    public QueryFeaturesTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<QueryFeaturesResult>> InvokeAsync(
        QueryFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Query is null)
        {
            return Task.FromResult(ToolResult<QueryFeaturesResult>.Err(
                new InvalidArgument("query", "must be supplied")));
        }

        if (GeoQueryValidator.Validate(request.Query) is { } err)
        {
            return Task.FromResult(ToolResult<QueryFeaturesResult>.Err(err));
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var page = Math.Max(0, request.Page);

        var snapshot = _catalog.Datasets;
        var matched = ImmutableArray.CreateBuilder<FeatureMatch>();

        foreach (var dataset in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            // Coarse skip: if the dataset's declared bounds are disjoint
            // from the query, no feature in it can match either.
            if (!SpatialPredicates.Intersects(dataset.Bounds, request.Query))
            {
                continue;
            }

            var features = FeatureAccessor.GetFeatures(dataset);
            if (features is null)
            {
                continue;
            }

            foreach (var feature in features)
            {
                if (request.FeatureType is { } ft
                    && !string.Equals(feature.FeatureType, ft, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!FeatureGeometryQuery.Intersects(feature, request.Query))
                {
                    continue;
                }

                if (request.Times is { } times
                    && FeatureValidity.Check(feature, times) == FeatureValidity.Verdict.Disjoint)
                {
                    continue;
                }

                matched.Add(new FeatureMatch(
                    dataset.Id,
                    dataset.Spec,
                    feature.Id,
                    feature.FeatureType,
                    FeatureGeometryQuery.TryGetBoundingBox(feature)));
            }
        }

        var totalCount = matched.Count;
        var skip = page * pageSize;
        var take = Math.Max(0, Math.Min(pageSize, totalCount - skip));
        var pageBuilder = ImmutableArray.CreateBuilder<FeatureMatch>(take);
        for (var i = 0; i < take; i++)
        {
            pageBuilder.Add(matched[skip + i]);
        }

        var hasMore = skip + take < totalCount;

        // Per-type breakdown of the full match set (all pages), so a caller
        // can gauge the result's shape before paging through it.
        var byType = new Dictionary<string, int>(StringComparer.Ordinal);
        var typeOrder = new List<string>();
        foreach (var match in matched)
        {
            if (!byType.TryGetValue(match.FeatureType, out var n))
            {
                typeOrder.Add(match.FeatureType);
            }
            byType[match.FeatureType] = n + 1;
        }

        var breakdown = ImmutableArray.CreateBuilder<FeatureTypeBreakdown>(typeOrder.Count);
        foreach (var type in typeOrder
                     .OrderByDescending(t => byType[t])
                     .ThenBy(t => t, StringComparer.Ordinal))
        {
            breakdown.Add(new FeatureTypeBreakdown(type, byType[type]));
        }

        return Task.FromResult(ToolResult<QueryFeaturesResult>.Ok(
            new QueryFeaturesResult(
                pageBuilder.MoveToImmutable(),
                page,
                pageSize,
                totalCount,
                hasMore,
                breakdown.MoveToImmutable())));
    }

    private static bool SpecMatches(SpecRef actual, SpecRef filter)
    {
        if (!string.Equals(actual.Name, filter.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return filter.Edition == default || actual.Edition == filter.Edition;
    }
}
