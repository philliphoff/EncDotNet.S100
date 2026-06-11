using System.Collections.Immutable;
using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Spec;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="FindNearestTool"/>.
/// </summary>
/// <param name="Latitude">Query latitude (decimal degrees, WGS-84). Must be in <c>[-90, 90]</c>.</param>
/// <param name="Longitude">Query longitude (decimal degrees, WGS-84). Must be in <c>[-180, 180]</c>.</param>
/// <param name="Spec">Optional spec filter; <c>null</c> matches every spec.</param>
/// <param name="FeatureType">Optional case-sensitive feature-type filter (the GML element local name); <c>null</c> matches every type.</param>
/// <param name="Attributes">Optional attribute filter applied before ranking.</param>
/// <param name="MaxResults">Maximum number of features to return; clamped to 1..200.</param>
/// <param name="MaxDistanceMeters">Optional cap; features whose distance exceeds this are excluded.</param>
public sealed record FindNearestRequest(
    [property: Description("Query latitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-90, 90].")] double Latitude,
    [property: Description("Query longitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-180, 180].")] double Longitude,
    [property: Description("Optional spec filter; null matches every spec. A default edition matches every edition of the same spec name.")] SpecRef? Spec = null,
    [property: Description("Optional case-sensitive feature-type filter (the GML element local name, e.g. \"RestrictedArea\", \"BuoyLateral\"); null matches every feature type.")] string? FeatureType = null,
    [property: Description("Optional attribute filter (AND of predicates) applied before ranking by distance.")] AttributeFilter? Attributes = null,
    [property: Description("Maximum number of nearest features to return; clamped to the range 1..200.")] int MaxResults = 10,
    [property: Description("Optional maximum distance in metres; features farther than this from the query point are excluded. Null means no cap.")] double? MaxDistanceMeters = null);

/// <summary>
/// A single ranked result from <see cref="FindNearestTool"/>.
/// </summary>
/// <param name="DatasetId">Dataset the feature belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureId">Stable feature identifier (<c>gml:id</c>).</param>
/// <param name="FeatureType">Feature type code (the GML element local name).</param>
/// <param name="Bounds">Bounding box of the feature's geometry.</param>
/// <param name="DistanceMeters">Great-circle distance from the query point to the feature's bounding box (0 when the point is inside it).</param>
public sealed record NearestFeatureMatch(
    [property: Description("Dataset the feature belongs to.")] DatasetId DatasetId,
    [property: Description("Spec the dataset declares.")] SpecRef Spec,
    [property: Description("Stable feature identifier (gml:id).")] string FeatureId,
    [property: Description("Feature type code (the GML element local name).")] string FeatureType,
    [property: Description("Bounding box of the feature's geometry.")] BoundingBox Bounds,
    [property: Description("Great-circle distance in metres from the query point to the feature's bounding box; 0 when the point is inside it.")] double DistanceMeters);

/// <summary>Result of <see cref="FindNearestTool"/>.</summary>
/// <param name="Features">Nearest features in ascending distance order (closest first).</param>
/// <param name="TotalConsidered">Number of geometry-bearing features that matched the spec/type/attribute/distance filters before truncation to <see cref="FindNearestRequest.MaxResults"/>.</param>
public sealed record FindNearestResult(
    [property: Description("Nearest features in ascending distance order (closest first), truncated to maxResults.")] ImmutableArray<NearestFeatureMatch> Features,
    [property: Description("Number of geometry-bearing features that satisfied every filter before truncation to maxResults.")] int TotalConsidered);

/// <summary>
/// Returns the loaded GML-encoded vector features nearest to a query
/// point, ranked by great-circle distance to each feature's bounding
/// box. Answers positional questions such as "what is the closest
/// restricted area to my position?" without the caller having to page
/// <see cref="QueryFeaturesTool"/> through expanding bounding boxes.
/// </summary>
/// <remarks>
/// <para>
/// Works against every GML-encoded spec the codebase supports (S-122,
/// S-124, S-125, S-127, S-128, S-129, S-131, S-201, S-411, S-421) via
/// the shared <see cref="IGmlFeature"/> abstraction. Coverage products
/// (S-102, S-104, S-111) and ISO 8211-encoded S-101 are not ranked;
/// use <see cref="SampleCoverageTool"/> or <see cref="FindAtTool"/> for
/// those.
/// </para>
/// <para>
/// Distance is measured to each feature's bounding box (0 when the
/// query point lies inside it), matching the bounding-box precision the
/// other spatial tools work at. Features without geometry are skipped.
/// </para>
/// </remarks>
public sealed class FindNearestTool
{
    /// <summary>Tool name used in <see cref="SpecNotSupportedForTool"/> errors.</summary>
    public const string Name = "find_nearest";

    internal const int DefaultMaxResults = 10;
    internal const int MaxMaxResults = 200;

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="FindNearestTool"/>.</summary>
    public FindNearestTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<FindNearestResult>> InvokeAsync(
        FindNearestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (double.IsNaN(request.Latitude) || request.Latitude < -90.0 || request.Latitude > 90.0)
        {
            return Err(new InvalidArgument("latitude", $"value {request.Latitude} is outside the WGS-84 range [-90, 90]"));
        }
        if (double.IsNaN(request.Longitude) || request.Longitude < -180.0 || request.Longitude > 180.0)
        {
            return Err(new InvalidArgument("longitude", $"value {request.Longitude} is outside the WGS-84 range [-180, 180]"));
        }
        if (request.MaxDistanceMeters is { } cap && (double.IsNaN(cap) || cap < 0.0))
        {
            return Err(new InvalidArgument("maxDistanceMeters", $"value {cap} must be a non-negative number"));
        }

        var maxResults = Math.Clamp(request.MaxResults, 1, MaxMaxResults);
        var queryPoint = new GeoPoint(request.Latitude, request.Longitude);
        var maxDistance = request.MaxDistanceMeters;

        var snapshot = _catalog.Datasets;
        var matched = new List<NearestFeatureMatch>();

        foreach (var dataset in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            var features = GmlFeatureAccessor.GetFeatures(dataset);
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

                var bounds = GmlFeatureGeometry.TryGetBoundingBox(feature);
                if (bounds is null)
                {
                    continue;
                }

                if (!GmlFeatureAttributes.Matches(feature, request.Attributes))
                {
                    continue;
                }

                var distance = GeoDistance.NearestDistanceMeters(bounds, queryPoint);
                if (maxDistance is { } cap && distance > cap)
                {
                    continue;
                }

                matched.Add(new NearestFeatureMatch(
                    dataset.Id,
                    dataset.Spec,
                    feature.Id,
                    feature.FeatureType,
                    bounds,
                    distance));
            }
        }

        var totalConsidered = matched.Count;
        var ranked = matched
            .OrderBy(m => m.DistanceMeters)
            .Take(maxResults)
            .ToImmutableArray();

        return Ok(new FindNearestResult(ranked, totalConsidered));
    }

    private static bool SpecMatches(SpecRef actual, SpecRef filter)
    {
        if (!string.Equals(actual.Name, filter.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return filter.Edition == default || actual.Edition == filter.Edition;
    }

    private static Task<ToolResult<FindNearestResult>> Ok(FindNearestResult value)
        => Task.FromResult(ToolResult<FindNearestResult>.Ok(value));

    private static Task<ToolResult<FindNearestResult>> Err(ToolError error)
        => Task.FromResult(ToolResult<FindNearestResult>.Err(error));
}
