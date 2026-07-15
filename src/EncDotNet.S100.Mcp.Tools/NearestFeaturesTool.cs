using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.Features;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Mcp.Tools.Geometry;
using EncDotNet.S100.Mcp.Tools.Spec;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Mcp.Tools;

/// <summary>
/// Request payload for <see cref="NearestFeaturesTool"/>.
/// </summary>
/// <param name="Latitude">Query latitude (decimal degrees, WGS-84). Must be in <c>[-90, 90]</c>.</param>
/// <param name="Longitude">Query longitude (decimal degrees, WGS-84). Must be in <c>[-180, 180]</c>.</param>
/// <param name="Spec">Optional spec filter; <c>null</c> matches every vector spec.</param>
/// <param name="Dataset">Optional single-dataset filter; <c>null</c> searches every matching dataset.</param>
/// <param name="FeatureType">Optional case-sensitive feature-type filter; <c>null</c> matches every type.</param>
/// <param name="MaxDistanceMeters">Optional maximum distance; features farther than this are excluded. <c>null</c> (default) imposes no limit.</param>
/// <param name="Limit">Maximum ranked features to return; clamped to <c>[1, 200]</c>.</param>
public sealed record NearestFeaturesRequest(
    [property: Description("Query latitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-90, 90].")] double Latitude,
    [property: Description("Query longitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-180, 180].")] double Longitude,
    [property: Description("Optional spec filter (e.g. \"S-101\" or \"S-124/1.5.0\"); null matches every vector spec.")] SpecRef? Spec = null,
    [property: Description("Optional dataset identifier (typically from list_datasets); null searches across every matching dataset.")] DatasetId? Dataset = null,
    [property: Description("Optional case-sensitive feature-type filter (the GML element local name, e.g. \"LightAllAround\"; for S-101 the feature-type acronym, e.g. \"LIGHTS\"); null matches every feature type.")] string? FeatureType = null,
    [property: Description("Optional maximum distance in metres; features whose nearest geometry is farther than this are excluded. null imposes no limit.")] double? MaxDistanceMeters = null,
    [property: Description("Maximum ranked features to return; clamped to [1, 200]. Default 10.")] int Limit = 10);

/// <summary>
/// A single ranked nearest-feature match returned by
/// <see cref="NearestFeaturesTool"/>.
/// </summary>
/// <param name="DatasetId">Dataset the feature belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureId">Stable feature identifier (<c>gml:id</c>; for S-101 the decimal RCID).</param>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="Geometry">Geometry primitive the distance was measured against: <c>point</c>, <c>curve</c>, or <c>surface</c>.</param>
/// <param name="DistanceMeters">True distance from the query point to the nearest point of the feature's geometry, in metres (0 when the point is inside an area feature).</param>
/// <param name="BearingDegrees">Initial bearing (degrees true, <c>[0, 360)</c>) from the query point toward the nearest point of the feature; <c>null</c> when the point is inside the feature.</param>
/// <param name="Containment"><c>inside</c> when the query point lies within an area feature; otherwise <c>near</c>.</param>
/// <param name="NearestLatitude">Latitude of the nearest point on the feature.</param>
/// <param name="NearestLongitude">Longitude of the nearest point on the feature.</param>
/// <param name="Bounds">Bounding box of the feature's geometry.</param>
public sealed record NearestFeatureMatch(
    [property: Description("Dataset the feature belongs to.")] DatasetId DatasetId,
    [property: Description("Spec the dataset declares.")] SpecRef Spec,
    [property: Description("Stable feature identifier (gml:id; for S-101 the decimal RCID).")] string FeatureId,
    [property: Description("Feature type code (GML element local name; for S-101 the feature-type acronym).")] string FeatureType,
    [property: Description("Geometry primitive the distance was measured against: point, curve, or surface.")] string Geometry,
    [property: Description("True distance from the query point to the nearest point of the feature's geometry, in metres (0 when inside an area feature).")] double DistanceMeters,
    [property: Description("Initial bearing (degrees true, [0, 360)) from the query point toward the nearest point of the feature; null when inside the feature.")] double? BearingDegrees,
    [property: Description("'inside' when the query point lies within an area feature; otherwise 'near'.")] string Containment,
    [property: Description("Latitude of the nearest point on the feature.")] double NearestLatitude,
    [property: Description("Longitude of the nearest point on the feature.")] double NearestLongitude,
    [property: Description("Bounding box of the feature's geometry.")] BoundingBox? Bounds);

/// <summary>Result of <see cref="NearestFeaturesTool"/>.</summary>
/// <param name="Point">The echoed query point.</param>
/// <param name="Features">Ranked matches, nearest first (distance ascending; ties broken by feature type then id).</param>
/// <param name="TotalMatched">Total number of features within range before applying <see cref="NearestFeaturesRequest.Limit"/>.</param>
/// <param name="Truncated"><c>true</c> when more features were in range than were returned.</param>
public sealed record NearestFeaturesResult(
    [property: Description("The echoed query point.")] GeoPoint Point,
    [property: Description("Ranked matches, nearest first (distance ascending; ties broken by feature type then id).")] IReadOnlyList<NearestFeatureMatch> Features,
    [property: Description("Total number of features within range before applying limit.")] int TotalMatched,
    [property: Description("True when more features were in range than were returned.")] bool Truncated);

/// <summary>
/// Ranks the vector features nearest to a geographic point by <em>true</em>
/// geometric distance — the distance-ranking and containment query that
/// <see cref="FindAtTool"/> (dataset-bbox membership) and
/// <see cref="QueryFeaturesTool"/> (feature-bbox intersection) cannot answer.
/// </summary>
/// <remarks>
/// <para>
/// Answers "what is the nearest light/buoy/berth to my position?" and
/// "is this point inside any restricted area?" in a single call: an area
/// feature that contains the query point is reported at
/// <see cref="NearestFeatureMatch.DistanceMeters"/> = 0 with
/// <c>containment = inside</c>; every other feature is reported at the true
/// distance to the nearest point on its geometry (the nearest point on a
/// segment, not merely the nearest vertex). Works across every vector spec
/// exposed through <see cref="FeatureAccessor"/>, including the ISO
/// 8211-encoded S-101.
/// </para>
/// <para>
/// Distances use the same fast equirectangular approximation as the rest
/// of the tools surface (see <see cref="GeometryDistance"/>); they are
/// accurate to a fraction of a percent over the span of a single dataset
/// and are intended for ranking and rough range read-out, not survey-grade
/// measurement. Coverage products (S-102 / S-104 / S-111) carry no vector
/// features and never contribute matches.
/// </para>
/// </remarks>
public sealed class NearestFeaturesTool
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "nearest_features";

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="NearestFeaturesTool"/>.</summary>
    public NearestFeaturesTool(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<NearestFeaturesResult>> InvokeAsync(
        NearestFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (double.IsNaN(request.Latitude) || request.Latitude < -90.0 || request.Latitude > 90.0)
        {
            return Task.FromResult(ToolResult<NearestFeaturesResult>.Err(
                new InvalidArgument("latitude", $"value {request.Latitude} is outside the WGS-84 range [-90, 90]")));
        }

        if (double.IsNaN(request.Longitude) || request.Longitude < -180.0 || request.Longitude > 180.0)
        {
            return Task.FromResult(ToolResult<NearestFeaturesResult>.Err(
                new InvalidArgument("longitude", $"value {request.Longitude} is outside the WGS-84 range [-180, 180]")));
        }

        if (request.MaxDistanceMeters is { } max && (double.IsNaN(max) || max < 0.0))
        {
            return Task.FromResult(ToolResult<NearestFeaturesResult>.Err(
                new InvalidArgument("maxDistanceMeters", $"value {max} must be non-negative")));
        }

        var point = new GeoPoint(request.Latitude, request.Longitude);
        var limit = Math.Clamp(request.Limit, 1, 200);
        var maxDistance = request.MaxDistanceMeters;

        // Coarse bbox pre-filter: inflate the dataset bounds by the search
        // radius so we never skip a dataset whose features lie just outside
        // its declared bounds at the query point. With no max distance every
        // dataset is considered.
        var (latPad, lonPad) = maxDistance is { } m
            ? Padding(m, request.Latitude)
            : (double.PositiveInfinity, double.PositiveInfinity);

        var hits = new List<NearestFeatureMatch>();

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

            if (maxDistance is not null && !SpatialPredicates.Contains(Inflated(dataset.Bounds, latPad, lonPad), point))
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

                if (request.FeatureType is { } ft
                    && !string.Equals(feature.FeatureType, ft, StringComparison.Ordinal))
                {
                    continue;
                }

                if (GeometryDistance.Measure(feature, point) is not { } measured)
                {
                    continue;
                }

                if (maxDistance is { } limitMeters && measured.DistanceMeters > limitMeters)
                {
                    continue;
                }

                hits.Add(new NearestFeatureMatch(
                    dataset.Id,
                    dataset.Spec,
                    feature.Id,
                    feature.FeatureType,
                    PrimitiveName(measured.Primitive),
                    measured.DistanceMeters,
                    measured.Inside ? null : GeometryDistance.Bearing(point, measured.NearestLatitude, measured.NearestLongitude),
                    measured.Inside ? "inside" : "near",
                    measured.NearestLatitude,
                    measured.NearestLongitude,
                    FeatureGeometryQuery.TryGetBoundingBox(feature)));
            }
        }

        hits.Sort(CompareMatches);

        var total = hits.Count;
        var take = Math.Min(limit, total);
        var builder = new List<NearestFeatureMatch>(take);
        for (var i = 0; i < take; i++)
        {
            builder.Add(hits[i]);
        }

        return Task.FromResult(ToolResult<NearestFeaturesResult>.Ok(
            new NearestFeaturesResult(
                point,
                builder,
                total,
                take < total)));
    }

    private static int CompareMatches(NearestFeatureMatch a, NearestFeatureMatch b)
    {
        var c = a.DistanceMeters.CompareTo(b.DistanceMeters);
        if (c != 0) return c;

        c = string.CompareOrdinal(a.FeatureType, b.FeatureType);
        if (c != 0) return c;

        return string.CompareOrdinal(a.FeatureId, b.FeatureId);
    }

    private static string PrimitiveName(S100GeometryType primitive) => primitive switch
    {
        S100GeometryType.Point => "point",
        S100GeometryType.Curve => "curve",
        S100GeometryType.Surface => "surface",
        _ => "none",
    };

    private static (double LatPad, double LonPad) Padding(double meters, double latitude)
    {
        var latPad = meters / GeometryDistance.MetersPerDegreeLatitude;
        var cosLat = Math.Abs(Math.Cos(latitude * Math.PI / 180.0));
        var lonPad = meters / (GeometryDistance.MetersPerDegreeLatitude * (cosLat < 1e-6 ? 1e-6 : cosLat));
        return (latPad, lonPad);
    }

    private static BoundingBox Inflated(BoundingBox b, double latPad, double lonPad) =>
        new(b.SouthLatitude - latPad, b.WestLongitude - lonPad, b.NorthLatitude + latPad, b.EastLongitude + lonPad);

    private static bool SpecMatches(SpecRef actual, SpecRef filter)
    {
        if (!string.Equals(actual.Name, filter.Name, StringComparison.Ordinal))
        {
            return false;
        }

        return filter.Edition == default || actual.Edition == filter.Edition;
    }
}
