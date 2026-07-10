using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Spec;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="SearchFeaturesTool"/>.
/// </summary>
/// <param name="Text">The text to search for in feature names.</param>
/// <param name="Spec">Optional spec filter; null matches every spec.</param>
/// <param name="Dataset">Optional single-dataset filter; null searches every matching dataset.</param>
/// <param name="Query">Optional spatial filter envelope; null searches everywhere.</param>
/// <param name="CaseSensitive">When true, the match is case-sensitive (default false).</param>
/// <param name="Exact">When true, a name must equal <paramref name="Text"/>; when false (default), substring containment matches.</param>
/// <param name="Page">Zero-based page index.</param>
/// <param name="PageSize">Page size; clamped to 1..500.</param>
public sealed record SearchFeaturesRequest(
    [property: Description("The text to search for in feature names (OBJNAM / NOBJNM / objectName / featureName, etc.). Required.")] string Text,
    [property: Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every spec. A default edition matches every edition of the same spec name.")] SpecRef? Spec = null,
    [property: Description("Optional dataset identifier; null searches across every matching dataset.")] DatasetId? Dataset = null,
    [property: Description("Optional spatial filter envelope (point / box / polygon / polyline). When supplied, only features whose bounding box intersects are searched; geometry-less features are excluded.")] GeoQuery? Query = null,
    [property: Description("When true the match is case-sensitive; default false.")] bool CaseSensitive = false,
    [property: Description("When true a name must equal the search text exactly; when false (default) any name containing the text matches.")] bool Exact = false,
    [property: Description("Zero-based page index into the result set.")] int Page = 0,
    [property: Description("Maximum features per page; clamped to the range 1..500.")] int PageSize = 50);

/// <summary>
/// A single name match returned by <see cref="SearchFeaturesTool"/>.
/// </summary>
/// <param name="DatasetId">Dataset the feature belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureId">Stable feature identifier (<c>gml:id</c>; for S-101 the decimal RCID).</param>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="MatchedName">The name value that matched the query.</param>
/// <param name="MatchedAttribute">The attribute path the matched name came from (e.g. <c>OBJNAM</c> or <c>featureName.name</c>).</param>
/// <param name="Bounds">Bounding box of the feature's geometry, or <c>null</c> if the feature carries no geometry.</param>
public sealed record FeatureNameMatch(
    [property: Description("Dataset the feature belongs to.")] DatasetId DatasetId,
    [property: Description("Spec the dataset declares.")] SpecRef Spec,
    [property: Description("Stable feature identifier (gml:id; for S-101 the decimal RCID).")] string FeatureId,
    [property: Description("Feature type code (GML element local name; for S-101 the feature-type acronym).")] string FeatureType,
    [property: Description("The name value that matched the query.")] string MatchedName,
    [property: Description("The attribute path the matched name came from (e.g. \"OBJNAM\" or \"featureName.name\").")] string MatchedAttribute,
    [property: Description("Bounding box of the feature's geometry, or null if the feature carries no geometry.")] BoundingBox? Bounds);

/// <summary>Result of <see cref="SearchFeaturesTool"/>.</summary>
/// <param name="Features">Matching features for the requested page.</param>
/// <param name="Page">Echoed (and floored) zero-based page index.</param>
/// <param name="PageSize">Echoed (and clamped) page size.</param>
/// <param name="TotalCount">Total number of matching features across all pages.</param>
/// <param name="HasMore">True if additional pages remain after the current one.</param>
public sealed record SearchFeaturesResult(
    [property: Description("Matching features for the requested page, in catalog insertion order then per-dataset feature order.")] IReadOnlyList<FeatureNameMatch> Features,
    [property: Description("Echoed (and floored) zero-based page index.")] int Page,
    [property: Description("Echoed (and clamped) page size.")] int PageSize,
    [property: Description("Total number of matching features across all pages.")] int TotalCount,
    [property: Description("True if additional pages remain after the current one.")] bool HasMore);

/// <summary>
/// Finds vector features by name across loaded S-100 datasets.
/// </summary>
/// <remarks>
/// <para>
/// Answers "where is the feature called <c>X</c>?" — the name-oriented
/// counterpart to the geometry-first <see cref="QueryFeaturesTool"/>.
/// A feature's name may live in several places depending on encoding
/// (simple <c>OBJNAM</c> / <c>NOBJNM</c> / <c>objectName</c>, or the
/// repeatable complex <c>featureName</c> compound's <c>name</c> /
/// <c>displayName</c> sub-attributes); <see cref="FeatureNames"/> unifies
/// them so a single query matches across every vector spec exposed through
/// <see cref="FeatureAccessor"/>, including the ISO 8211-encoded S-101.
/// </para>
/// <para>
/// Matching is substring containment by default (set
/// <see cref="SearchFeaturesRequest.Exact"/> for whole-name equality) and
/// case-insensitive unless <see cref="SearchFeaturesRequest.CaseSensitive"/>
/// is set. A feature is returned at most once even when several of its
/// names match; the first matching name (in enumeration order) is
/// reported. Coverage products (S-102 / S-104 / S-111) carry no searchable
/// features and never contribute matches.
/// </para>
/// </remarks>
public sealed class SearchFeaturesTool
{
    /// <summary>Tool name used in error payloads.</summary>
    public const string Name = "search_features";

    private const int MaxPageSize = 500;

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="SearchFeaturesTool"/>.</summary>
    public SearchFeaturesTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<SearchFeaturesResult>> InvokeAsync(
        SearchFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return Task.FromResult(ToolResult<SearchFeaturesResult>.Err(
                new InvalidArgument(nameof(request.Text), "search text is required and cannot be blank.")));
        }

        if (request.Query is { } query && GeoQueryValidator.Validate(query) is { } err)
        {
            return Task.FromResult(ToolResult<SearchFeaturesResult>.Err(err));
        }

        var page = Math.Max(0, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);
        var comparison = request.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

        var matched = new List<FeatureNameMatch>();

        foreach (var dataset in _catalog.Datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Dataset is { } id && dataset.Id != id)
            {
                continue;
            }

            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            if (request.Query is { } q && !SpatialPredicates.Intersects(dataset.Bounds, q))
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
                cancellationToken.ThrowIfCancellationRequested();

                if (request.Query is { } qf && !FeatureGeometryQuery.Intersects(feature, qf))
                {
                    continue;
                }

                (string Source, string Value)? hit = null;
                foreach (var candidate in FeatureNames.Enumerate(feature))
                {
                    if (IsMatch(candidate.Value, request.Text, request.Exact, comparison))
                    {
                        hit = candidate;
                        break;
                    }
                }

                if (hit is not { } name)
                {
                    continue;
                }

                matched.Add(new FeatureNameMatch(
                    dataset.Id,
                    dataset.Spec,
                    feature.Id,
                    feature.FeatureType,
                    name.Value,
                    name.Source,
                    FeatureGeometryQuery.TryGetBoundingBox(feature)));
            }
        }

        var totalCount = matched.Count;
        var skip = page * pageSize;
        var take = Math.Max(0, Math.Min(pageSize, totalCount - skip));
        var pageBuilder = new List<FeatureNameMatch>(take);
        for (var i = 0; i < take; i++)
        {
            pageBuilder.Add(matched[skip + i]);
        }

        var hasMore = skip + take < totalCount;

        return Task.FromResult(ToolResult<SearchFeaturesResult>.Ok(
            new SearchFeaturesResult(
                pageBuilder,
                page,
                pageSize,
                totalCount,
                hasMore)));
    }

    private static bool IsMatch(string value, string text, bool exact, StringComparison comparison)
        => exact
            ? string.Equals(value, text, comparison)
            : value.Contains(text, comparison);

    private static bool SpecMatches(SpecRef actual, SpecRef filter)
    {
        if (!string.Equals(actual.Name, filter.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return filter.Edition == default || actual.Edition == filter.Edition;
    }
}
