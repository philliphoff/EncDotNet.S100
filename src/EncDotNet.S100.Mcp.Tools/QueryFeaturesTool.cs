using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Datasets.Pipelines.Time;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="QueryFeaturesTool"/>.
/// </summary>
/// <param name="Query">
/// The geographic query. All four <see cref="GeoQuery"/> variants are
/// supported; intersection is computed against each feature's bounding
/// box unless <paramref name="Precise"/> is set. Required unless
/// <paramref name="CountOnly"/> is set, when <c>null</c> counts every
/// feature, those without geometry included.
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
/// <param name="CountOnly">
/// Count instead of listing (#1609, which folded <c>count_features</c> in
/// here): no feature rows, and <see cref="QueryFeaturesResult.Counts"/> per
/// dataset and feature type.
/// </param>
public sealed record QueryFeaturesRequest(
    [property: Description("Spatial query envelope (point / box / polygon / polyline). Intersection is computed against each feature's bounding box. Required unless countOnly, when null counts every feature, those without geometry included.")] GeoQuery? Query,
    [property: Description("Optional spec filter; null matches every spec. A default edition matches every edition of the same spec name.")] SpecRef? Spec = null,
    [property: Description("Optional case-sensitive feature-type filter (the GML element local name, e.g. \"NavwarnPart\", \"BuoyLateral\"; for S-101 the feature-type acronym, e.g. \"LIGHTS\"); null returns every feature type.")] string? FeatureType = null,
    [property: Description("Optional temporal filter. When supplied, features whose fixedDateRange/periodicDateRange validity window is disjoint from the query window are excluded; features without validity metadata are always included.")] TimeQuery? Times = null,
    [property: Description("Optional attribute-value predicates; a feature must satisfy all of them (logical AND). Discover valid attributes and enumerated values with describe_feature_type.")] IReadOnlyList<AttributePredicate>? Attributes = null,
    [property: Description("When true, replaces the default bounding-box intersection test with true full-geometry intersection: point-in-polygon containment for area features (interior-ring holes honoured) and genuine segment crossing — e.g. \"which features does this route leg actually cross?\". Slightly more expensive; default false.")] bool Precise = false,
    [property: Description("Zero-based page index into the result set.")] int Page = 0,
    [property: Description("Maximum features per page; clamped to the range 1..500.")] int PageSize = 50,
    [property: Description("Optional single-dataset filter; when supplied only the dataset with this identifier (from list_datasets) is queried. Null queries every matching dataset.")] DatasetId? Dataset = null,
    [property: Description("Add each feature's attributes as encoded (code and raw value) in 'attributes'. Default false. Numeric depths are in 'depth' either way.")] bool IncludeAttributes = false,
    [property: Description("Count instead of listing: no feature rows, and 'counts' per dataset and feature type with how many have resolvable geometry. Default false.")] bool CountOnly = false);

/// <summary>
/// Per-feature summary returned by <see cref="QueryFeaturesTool"/>.
/// </summary>
/// <param name="DatasetId">Dataset the feature belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureId">Stable feature identifier (<c>gml:id</c>; for S-101 the decimal RCID).</param>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="Bounds">Bounding box of the feature's geometry, or <c>null</c> if the feature carries no geometry.</param>
/// <param name="Depth">The feature's depth values as numbers, or <c>null</c> when it carries none (#556).</param>
/// <param name="Attributes">With <see cref="QueryFeaturesRequest.IncludeAttributes"/>, the feature's attributes as encoded.</param>
public sealed record FeatureMatch(
    DatasetId DatasetId,
    SpecRef Spec,
    string FeatureId,
    string FeatureType,
    BoundingBox? Bounds,
    [property: Description("The feature's depth values as numbers in metres, named as sample's depth result names them: depthRangeMinimumMeters, depthRangeMaximumMeters, valueOfSoundingMeters, and the waterLevelEffect code. Null when it carries none.")] FeatureDepthValues? Depth = null,
    [property: Description("With includeAttributes, the feature's attributes as encoded (code and raw value; a complex attribute's parts as complex.sub). Null otherwise.")] IReadOnlyList<IdentifyAttribute>? Attributes = null);

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

/// <summary>
/// A per-dataset, per-feature-type tally returned by
/// <see cref="QueryFeaturesTool"/> with <see cref="QueryFeaturesRequest.CountOnly"/>.
/// </summary>
/// <param name="DatasetId">Dataset the tally belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="Count">Number of matching features of this type.</param>
/// <param name="WithGeometry">Number of those features that have resolvable geometry — i.e. are addressable by spatial tools.</param>
public sealed record FeatureTypeTally(
    [property: Description("Dataset the tally belongs to.")] DatasetId DatasetId,
    [property: Description("Spec the dataset declares.")] SpecRef Spec,
    [property: Description("Feature type code (GML element local name; for S-101 the feature-type acronym).")] string FeatureType,
    [property: Description("Number of matching features of this type.")] int Count,
    [property: Description("Number of those features that have resolvable geometry (addressable by spatial tools).")] int WithGeometry);

/// <summary>Result of <see cref="QueryFeaturesTool"/>.</summary>
public sealed record QueryFeaturesResult(
    [property: Description("Matching features for the requested page, in catalog insertion order then per-dataset feature order.")] IReadOnlyList<FeatureMatch> Features,
    [property: Description("Echoed (and floored) zero-based page index.")] int Page,
    [property: Description("Echoed (and clamped) page size.")] int PageSize,
    [property: Description("Total number of matching features across all pages.")] int TotalCount,
    [property: Description("True if additional pages remain after the current one.")] bool HasMore,
    [property: Description("Per-feature-type tally of the full (all-pages) match set, ordered by descending count then feature type. Lets a caller gauge the result shape before paging.")] IReadOnlyList<FeatureTypeBreakdown> TypeBreakdown,
    [property: Description("With countOnly: per-dataset, per-feature-type tallies, ordered by dataset then descending count then feature type. Null otherwise.")] IReadOnlyList<FeatureTypeTally>? Counts = null,
    [property: Description("With countOnly: the number of datasets with at least one match. Null otherwise.")] int? DatasetCount = null);


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
/// <see cref="SampleTool"/> for those.
/// </para>
/// <para>
/// Intersection is computed at bounding-box precision per feature.
/// Features with no geometry at all (e.g. container-style features
/// such as <c>S131:Authority</c>) are not returned even when their
/// dataset's bounds intersect the query; with
/// <see cref="QueryFeaturesRequest.CountOnly"/> and no query they are
/// counted, and excluded from <see cref="FeatureTypeTally.WithGeometry"/>.
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

        var query = request.Query;
        if (query is null && !request.CountOnly)
        {
            return Task.FromResult(ToolResult<QueryFeaturesResult>.Err(
                new InvalidArgument("query", "must be supplied unless countOnly is set")));
        }

        if (query is not null && GeoQueryValidator.Validate(query) is { } err)
        {
            return Task.FromResult(ToolResult<QueryFeaturesResult>.Err(err));
        }

        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        var page = Math.Max(0, request.Page);

        var snapshot = _catalog.Datasets;
        var matched = new List<FeatureMatch>();
        var matchedFeatures = new List<EncDotNet.S100.Features.IS100Feature>();
        var counts = request.CountOnly ? new List<FeatureTypeTally>() : null;
        var datasetCount = 0;

        foreach (var dataset in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Dataset is { } datasetId && dataset.Id != datasetId)
            {
                continue;
            }

            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            // Coarse skip: if the dataset's declared bounds are disjoint
            // from the query, no feature in it can match either.
            if (query is not null && !SpatialPredicates.Intersects(dataset.Bounds, query))
            {
                continue;
            }

            var features = FeatureAccessor.GetFeatures(dataset);
            if (features is null)
            {
                continue;
            }

            var datasetStart = matched.Count;
            foreach (var feature in features)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (request.FeatureType is { } ft
                    && !string.Equals(feature.FeatureType, ft, StringComparison.Ordinal))
                {
                    continue;
                }

                if (query is not null && !FeatureGeometryQuery.Intersects(feature, query))
                {
                    continue;
                }

                if (query is not null && request.Precise && !GeometryIntersection.Intersects(feature, query))
                {
                    continue;
                }

                if (request.Times is { } times
                    && FeatureValidity.Check(feature, times) == FeatureValidity.Verdict.Disjoint)
                {
                    continue;
                }

                if (!AttributePredicateEvaluator.Matches(feature, request.Attributes ?? []))
                {
                    continue;
                }

                matched.Add(new FeatureMatch(
                    dataset.Id,
                    dataset.Spec,
                    feature.Id,
                    feature.FeatureType,
                    FeatureGeometryQuery.TryGetBoundingBox(feature)));
                matchedFeatures.Add(feature);
            }

            if (counts is not null && matched.Count > datasetStart)
            {
                datasetCount++;
                counts.AddRange(Tally(dataset.Id, dataset.Spec, matched, datasetStart));
            }
        }

        var totalCount = matched.Count;
        var skip = page * pageSize;
        var take = request.CountOnly ? 0 : Math.Max(0, Math.Min(pageSize, totalCount - skip));
        var pageBuilder = new List<FeatureMatch>(take);
        for (var i = 0; i < take; i++)
        {
            // Values for the page only (#556).
            var feature = matchedFeatures[skip + i];
            pageBuilder.Add(matched[skip + i] with
            {
                Depth = FeatureDepthValues.From(feature),
                Attributes = request.IncludeAttributes ? IdentifyAttribute.Raw(feature) : null,
            });
        }

        var hasMore = !request.CountOnly && skip + take < totalCount;

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

        var breakdown = new List<FeatureTypeBreakdown>(typeOrder.Count);
        foreach (var type in typeOrder
                     .OrderByDescending(t => byType[t])
                     .ThenBy(t => t, StringComparer.Ordinal))
        {
            breakdown.Add(new FeatureTypeBreakdown(type, byType[type]));
        }

        return Task.FromResult(ToolResult<QueryFeaturesResult>.Ok(
            new QueryFeaturesResult(
                pageBuilder,
                page,
                pageSize,
                totalCount,
                hasMore,
                breakdown,
                counts,
                counts is null ? null : datasetCount)));
    }

    // One dataset's matches by feature type: descending count, then type.
    private static IEnumerable<FeatureTypeTally> Tally(DatasetId datasetId, SpecRef spec, List<FeatureMatch> matched, int start)
    {
        var byType = new Dictionary<string, (int Count, int WithGeometry)>(StringComparer.Ordinal);
        for (var i = start; i < matched.Count; i++)
        {
            var match = matched[i];
            byType.TryGetValue(match.FeatureType, out var current);
            byType[match.FeatureType] = (current.Count + 1, current.WithGeometry + (match.Bounds is null ? 0 : 1));
        }

        return byType
            .OrderByDescending(kv => kv.Value.Count)
            .ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new FeatureTypeTally(datasetId, spec, kv.Key, kv.Value.Count, kv.Value.WithGeometry));
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
