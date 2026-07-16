using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Spec;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="CountFeaturesTool"/>.
/// </summary>
/// <param name="Spec">
/// Optional spec filter. When supplied, only loaded datasets whose spec
/// name (and optionally edition) match are counted. A <c>default</c>
/// edition matches every edition of the same spec name.
/// </param>
/// <param name="Dataset">
/// Optional single-dataset filter. When supplied, only the dataset with
/// this identifier is counted (still subject to <paramref name="Spec"/>).
/// </param>
/// <param name="Query">
/// Optional spatial filter. When supplied, only features whose geometry
/// bounding box intersects the query envelope are counted; features
/// without geometry are excluded. When <c>null</c>, every feature is
/// counted (including geometry-less container features).
/// </param>
public sealed record CountFeaturesRequest(
    [property: Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every spec. A default edition matches every edition of the same spec name.")] SpecRef? Spec = null,
    [property: Description("Optional dataset identifier; null counts across every matching dataset.")] DatasetId? Dataset = null,
    [property: Description("Optional spatial filter envelope (point / box / polygon / polyline). When supplied, only features whose bounding box intersects are counted; geometry-less features are excluded.")] GeoQuery? Query = null);

/// <summary>
/// A per-dataset, per-feature-type tally returned by
/// <see cref="CountFeaturesTool"/>.
/// </summary>
/// <param name="DatasetId">Dataset the tally belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="Count">Number of features of this type (after any spatial filter).</param>
/// <param name="WithGeometry">Number of those features that have resolvable geometry — i.e. are addressable by spatial tools.</param>
public sealed record FeatureTypeTally(
    [property: Description("Dataset the tally belongs to.")] DatasetId DatasetId,
    [property: Description("Spec the dataset declares.")] SpecRef Spec,
    [property: Description("Feature type code (GML element local name; for S-101 the feature-type acronym).")] string FeatureType,
    [property: Description("Number of features of this type after any spatial filter.")] int Count,
    [property: Description("Number of those features that have resolvable geometry (addressable by spatial tools).")] int WithGeometry);

/// <summary>Result of <see cref="CountFeaturesTool"/>.</summary>
/// <param name="Types">
/// Per-dataset, per-feature-type tallies, ordered by dataset (catalog
/// order) then descending count then feature type.
/// </param>
/// <param name="TotalFeatures">Total number of features counted across every tally.</param>
/// <param name="DistinctTypeCount">Number of distinct (dataset, feature-type) tallies returned.</param>
/// <param name="DatasetCount">Number of datasets that contributed at least one feature.</param>
public sealed record CountFeaturesResult(
    [property: Description("Per-dataset, per-feature-type tallies, ordered by dataset then descending count then feature type.")] IReadOnlyList<FeatureTypeTally> Types,
    [property: Description("Total number of features counted across every tally.")] int TotalFeatures,
    [property: Description("Number of distinct (dataset, feature-type) tallies returned.")] int DistinctTypeCount,
    [property: Description("Number of datasets that contributed at least one feature.")] int DatasetCount);

/// <summary>
/// Enumerates the feature types present in loaded vector datasets and
/// counts how many features of each type they contain.
/// </summary>
/// <remarks>
/// <para>
/// Answers "what kinds of features, and how many, are in this cell?" — the
/// discovery question that <see cref="DescribeFeatureTool"/> cannot, because
/// it needs a feature identifier the caller can only obtain by already
/// knowing the dataset's contents. Works across every vector spec exposed
/// through <see cref="FeatureAccessor"/>, including the ISO 8211-encoded
/// S-101.
/// </para>
/// <para>
/// Coverage products (S-102 / S-104 / S-111) carry no enumerable features
/// and never contribute tallies. Container-style features without geometry
/// (e.g. <c>S131:Authority</c>) are counted in <see cref="FeatureTypeTally.Count"/>
/// but excluded from <see cref="FeatureTypeTally.WithGeometry"/>; when a
/// spatial <see cref="CountFeaturesRequest.Query"/> is supplied they are
/// dropped entirely.
/// </para>
/// </remarks>
public sealed class CountFeaturesTool
{
    /// <summary>Tool name used in <see cref="SpecNotSupportedForTool"/> errors.</summary>
    public const string Name = "count_features";

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="CountFeaturesTool"/>.</summary>
    public CountFeaturesTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<CountFeaturesResult>> InvokeAsync(
        CountFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Query is { } query && GeoQueryValidator.Validate(query) is { } err)
        {
            return Task.FromResult(ToolResult<CountFeaturesResult>.Err(err));
        }

        var snapshot = _catalog.Datasets;
        var tallies = new List<FeatureTypeTally>();
        var totalFeatures = 0;
        var datasetCount = 0;

        foreach (var dataset in snapshot)
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

            // Accumulate per feature-type counts for this dataset, preserving
            // first-seen order so equal counts sort deterministically.
            var byType = new Dictionary<string, (int Count, int WithGeometry)>(StringComparer.Ordinal);
            var typeOrder = new List<string>();

            foreach (var feature in features)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var bounds = FeatureGeometryQuery.TryGetBoundingBox(feature);

                if (request.Query is { } qf
                    && (bounds is null || !FeatureGeometryQuery.Intersects(feature, qf)))
                {
                    continue;
                }

                if (!byType.TryGetValue(feature.FeatureType, out var current))
                {
                    typeOrder.Add(feature.FeatureType);
                    current = (0, 0);
                }

                byType[feature.FeatureType] = (current.Count + 1, current.WithGeometry + (bounds is null ? 0 : 1));
                totalFeatures++;
            }

            if (byType.Count == 0)
            {
                continue;
            }

            datasetCount++;

            foreach (var type in typeOrder
                         .OrderByDescending(t => byType[t].Count)
                         .ThenBy(t => t, StringComparer.Ordinal))
            {
                var (count, withGeometry) = byType[type];
                tallies.Add(new FeatureTypeTally(dataset.Id, dataset.Spec, type, count, withGeometry));
            }
        }

        return Task.FromResult(ToolResult<CountFeaturesResult>.Ok(
            new CountFeaturesResult(
                tallies,
                totalFeatures,
                tallies.Count,
                datasetCount)));
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
