using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// Request payload for <see cref="IdentifyFeaturesService"/>.
/// </summary>
/// <param name="Latitude">Pick latitude (decimal degrees, WGS-84). Must be in <c>[-90, 90]</c>.</param>
/// <param name="Longitude">Pick longitude (decimal degrees, WGS-84). Must be in <c>[-180, 180]</c>.</param>
/// <param name="Spec">Optional spec filter; <c>null</c> matches every vector spec.</param>
/// <param name="RadiusMeters">
/// Search tolerance for point and curve features (metres). A point or
/// curve feature matches when it lies within this distance of the pick.
/// Area features always use exact point-in-polygon containment and ignore
/// the radius. Clamped to <c>[0, 100000]</c>.
/// </param>
/// <param name="MaxResults">Maximum ranked matches to return; clamped to <c>[1, 200]</c>.</param>
public sealed record IdentifyFeaturesRequest(
    [property: Description("Pick latitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-90, 90].")] double Latitude,
    [property: Description("Pick longitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-180, 180].")] double Longitude,
    [property: Description("Optional spec filter; null matches every vector spec.")] SpecRef? Spec = null,
    [property: Description("Search tolerance for point/curve features in metres; area features use exact containment and ignore it. Clamped to [0, 100000]. Default 50.")] double RadiusMeters = 50.0,
    [property: Description("Maximum ranked matches to return; clamped to [1, 200]. Default 20.")] int MaxResults = 20);

/// <summary>
/// A single feature picked at the query point by
/// <see cref="IdentifyFeaturesService"/>, in most-specific-first order.
/// </summary>
/// <param name="DatasetId">Dataset the feature belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureId">Stable feature identifier (<c>gml:id</c>; for S-101 the decimal RCID).</param>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="Geometry">Geometry primitive: <c>point</c>, <c>curve</c>, or <c>surface</c>.</param>
/// <param name="Bounds">Bounding box of the feature's geometry.</param>
/// <param name="Containment"><c>inside</c> when the pick is within an area feature; <c>near</c> when within the radius of a point/curve feature.</param>
/// <param name="DistanceMeters">Approximate distance from the pick to the feature (0 for <c>inside</c>); <c>null</c> when not computed.</param>
/// <param name="ReferencedTexts">Resolved external text files referenced by the feature's <c>fileReference</c> / <c>TXTDSC</c> / <c>NTXTDS</c> attributes; empty when none or unresolvable.</param>
public sealed record IdentifyMatch(
    [property: Description("Dataset the feature belongs to.")] DatasetId DatasetId,
    [property: Description("Spec the dataset declares.")] SpecRef Spec,
    [property: Description("Stable feature identifier (gml:id; for S-101 the decimal RCID).")] string FeatureId,
    [property: Description("Feature type code (GML element local name; for S-101 the feature-type acronym).")] string FeatureType,
    [property: Description("Geometry primitive: point, curve, or surface.")] string Geometry,
    [property: Description("Bounding box of the feature's geometry.")] BoundingBox? Bounds,
    [property: Description("'inside' when the pick is within an area feature; 'near' when within the radius of a point/curve feature.")] string Containment,
    [property: Description("Approximate distance from the pick to the feature in metres (0 for 'inside'); null when not computed.")] double? DistanceMeters,
    [property: Description("Resolved external text files referenced by the feature's fileReference / TXTDSC / NTXTDS attributes; empty when none or unresolvable.")] IReadOnlyList<ReferencedText>? ReferencedTexts = null);

/// <summary>
/// The resolved content of an external text file referenced by a feature's
/// <c>fileReference</c> attribute (S-101 Feature Catalogue, aliases
/// <c>TXTDSC</c> / <c>NTXTDS</c>) — the headless counterpart of the
/// viewer's referenced-text cards.
/// </summary>
/// <param name="FileName">The referenced file name as written in the attribute.</param>
/// <param name="Text">The resolved textual content of the referenced file.</param>
public sealed record ReferencedText(
    [property: Description("The referenced file name as written in the fileReference attribute.")] string FileName,
    [property: Description("The resolved textual content of the referenced file.")] string Text);

/// <summary>Result of <see cref="IdentifyFeaturesService"/>.</summary>
/// <param name="Point">The echoed pick point.</param>
/// <param name="Features">Ranked matches, most-specific first (point before curve before area; ties broken by smaller area / nearer distance).</param>
/// <param name="TotalMatched">Total number of features that matched before applying <see cref="IdentifyFeaturesRequest.MaxResults"/>.</param>
/// <param name="Truncated"><c>true</c> when more features matched than were returned.</param>
public sealed record IdentifyFeaturesResult(
    [property: Description("The echoed pick point.")] GeoPoint Point,
    [property: Description("Ranked matches, most-specific first (point before curve before area; ties broken by smaller area / nearer distance).")] IReadOnlyList<IdentifyMatch> Features,
    [property: Description("Total number of features that matched before applying maxResults.")] int TotalMatched,
    [property: Description("True when more features matched than were returned.")] bool Truncated);

/// <summary>
/// Identifies the vector features at a geographic point — the ECDIS
/// cursor-pick interaction — ranked most-specific first.
/// </summary>
/// <remarks>
/// <para>
/// The feature-aware complement to the find-at query (which answers
/// "which <em>datasets</em>' bounds cover this point?"). Area features use
/// exact point-in-polygon containment (with interior-ring holes honoured);
/// point and curve features match within <see cref="IdentifyFeaturesRequest.RadiusMeters"/>.
/// Works across every vector spec exposed through <see cref="FeatureAccessor"/>,
/// including the ISO 8211-encoded S-101.
/// </para>
/// <para>
/// Ranking approximates ECDIS draw order without consulting a portrayal
/// catalogue: point features rank above curves, which rank above areas;
/// within a primitive, smaller-area / nearer features rank first, so the
/// most specific feature under the cursor is returned at the top.
/// </para>
/// </remarks>
public sealed class IdentifyFeaturesService
{
    /// <summary>The MCP tool name.</summary>
    public const string Name = "identify_features";

    private const double MetersPerDegreeLatitude = 111_320.0;

    /// <summary>
    /// Attribute codes whose value names an externally referenced text file
    /// (S-101 Feature Catalogue simple attribute <c>fileReference</c>, aliases
    /// <c>TXTDSC</c> / <c>NTXTDS</c>). Mirrors
    /// <c>FeatureInfoBuilder.FileReferenceAttributeCodes</c> without taking a
    /// project dependency on the pipeline layer.
    /// </summary>
    private static readonly HashSet<string> FileReferenceAttributeCodes =
        new(StringComparer.OrdinalIgnoreCase) { "fileReference", "TXTDSC", "NTXTDS" };

    private readonly IDatasetCatalog _catalog;

    /// <summary>Creates a new <see cref="IdentifyFeaturesService"/>.</summary>
    public IdentifyFeaturesService(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>Executes the tool.</summary>
    public Task<ToolResult<IdentifyFeaturesResult>> InvokeAsync(
        IdentifyFeaturesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (double.IsNaN(request.Latitude) || request.Latitude < -90.0 || request.Latitude > 90.0)
        {
            return Task.FromResult(ToolResult<IdentifyFeaturesResult>.Err(
                new InvalidArgument("latitude", $"value {request.Latitude} is outside the WGS-84 range [-90, 90]")));
        }

        if (double.IsNaN(request.Longitude) || request.Longitude < -180.0 || request.Longitude > 180.0)
        {
            return Task.FromResult(ToolResult<IdentifyFeaturesResult>.Err(
                new InvalidArgument("longitude", $"value {request.Longitude} is outside the WGS-84 range [-180, 180]")));
        }

        if (!double.IsFinite(request.RadiusMeters))
        {
            return Task.FromResult(ToolResult<IdentifyFeaturesResult>.Err(
                new InvalidArgument("radiusMeters", $"value {request.RadiusMeters} is not a finite number")));
        }

        var point = new GeoPoint(request.Latitude, request.Longitude);
        var radius = Math.Clamp(request.RadiusMeters, 0.0, 100_000.0);
        var maxResults = Math.Clamp(request.MaxResults, 1, 200);

        // Convert the radius to a per-axis degree tolerance for the coarse
        // bbox pre-filter (longitude scaled by latitude).
        var latPad = radius / MetersPerDegreeLatitude;
        var lonScale = Math.Cos(request.Latitude * Math.PI / 180.0);
        var lonPad = radius / (MetersPerDegreeLatitude * (Math.Abs(lonScale) < 1e-6 ? 1e-6 : Math.Abs(lonScale)));

        var snapshot = _catalog.Datasets;
        var hits = new List<Hit>();

        foreach (var dataset in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            if (!SpatialPredicates.Contains(Inflated(dataset.Bounds, latPad, lonPad), point))
            {
                continue;
            }

            var features = FeatureAccessor.GetFeatures(dataset);
            if (features is null)
            {
                continue;
            }

            var resolver = (dataset.Data as S101DatasetData)?.ExternalTextResolver;

            foreach (var feature in features)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (TryMatch(feature, point, latPad, lonPad, out var hit)
                    && (hit.Inside || hit.DistanceMeters <= radius))
                {
                    hits.Add(hit with { DatasetId = dataset.Id, Spec = dataset.Spec, Resolver = resolver, Feature = feature });
                }
            }
        }

        hits.Sort(CompareHits);

        var total = hits.Count;
        var take = Math.Min(maxResults, total);
        var builder = new List<IdentifyMatch>(take);
        for (var i = 0; i < take; i++)
        {
            var h = hits[i];
            builder.Add(new IdentifyMatch(
                h.DatasetId,
                h.Spec,
                h.FeatureId,
                h.FeatureType,
                PrimitiveName(h.Specificity),
                h.Bounds,
                h.Inside ? "inside" : "near",
                h.Inside ? 0.0 : h.DistanceMeters,
                ResolveReferencedTexts(h.Feature, h.Resolver)));
        }

        return Task.FromResult(ToolResult<IdentifyFeaturesResult>.Ok(
            new IdentifyFeaturesResult(
                point,
                builder,
                total,
                take < total)));
    }

    private bool TryMatch(
        IS100Feature feature,
        GeoPoint point,
        double latPad,
        double lonPad,
        out Hit hit)
    {
        hit = default;

        var bounds = FeatureGeometryQuery.TryGetBoundingBox(feature);
        if (bounds is null)
        {
            return false;
        }

        // Area: exact point-in-polygon, honouring interior-ring holes.
        if (feature.ExteriorRing.Count > 0)
        {
            if (!SpatialPredicates.ContainsPoint(ToRing(feature.ExteriorRing), point))
            {
                return false;
            }

            if (feature.InteriorRings.Count > 0)
            {
                foreach (var hole in feature.InteriorRings)
                {
                    if (hole.Count > 0 && SpatialPredicates.ContainsPoint(ToRing(hole), point))
                    {
                        return false;
                    }
                }
            }

            hit = new Hit
            {
                FeatureId = feature.Id,
                FeatureType = feature.FeatureType,
                Specificity = 2,
                Bounds = bounds,
                Area = Area(bounds),
                Inside = true,
                DistanceMeters = 0.0,
            };
            return true;
        }

        // Curve: nearest-vertex distance; coarse bbox pre-filter here, the
        // precise radius test is applied by the caller.
        if (feature.Curves.Count > 0)
        {
            if (!SpatialPredicates.Contains(Inflated(bounds, latPad, lonPad), point))
            {
                return false;
            }

            var dist = double.PositiveInfinity;
            foreach (var curve in feature.Curves)
            {
                foreach (var v in curve)
                {
                    dist = Math.Min(dist, Meters(point, v));
                }
            }

            hit = new Hit
            {
                FeatureId = feature.Id,
                FeatureType = feature.FeatureType,
                Specificity = 1,
                Bounds = bounds,
                Area = Area(bounds),
                Inside = false,
                DistanceMeters = dist,
            };
            return true;
        }

        // Point: nearest-point distance; coarse bbox pre-filter here, the
        // precise radius test is applied by the caller.
        if (feature.Points.Count > 0)
        {
            if (!SpatialPredicates.Contains(Inflated(bounds, latPad, lonPad), point))
            {
                return false;
            }

            var dist = double.PositiveInfinity;
            foreach (var p in feature.Points)
            {
                dist = Math.Min(dist, Meters(point, p));
            }

            hit = new Hit
            {
                FeatureId = feature.Id,
                FeatureType = feature.FeatureType,
                Specificity = 0,
                Bounds = bounds,
                Area = Area(bounds),
                Inside = false,
                DistanceMeters = dist,
            };
            return true;
        }

        return false;
    }

    // Most-specific first: point (0) < curve (1) < area (2); then within a
    // primitive the nearer / smaller feature wins; then a stable tiebreak.
    private static int CompareHits(Hit a, Hit b)
    {
        var c = a.Specificity.CompareTo(b.Specificity);
        if (c != 0) return c;

        c = a.Specificity == 2
            ? a.Area.CompareTo(b.Area)
            : a.DistanceMeters.CompareTo(b.DistanceMeters);
        if (c != 0) return c;

        c = string.CompareOrdinal(a.FeatureType, b.FeatureType);
        if (c != 0) return c;

        return string.CompareOrdinal(a.FeatureId, b.FeatureId);
    }

    private static string PrimitiveName(int specificity) => specificity switch
    {
        0 => "point",
        1 => "curve",
        _ => "surface",
    };

    /// <summary>
    /// Resolves every <c>fileReference</c> / <c>TXTDSC</c> / <c>NTXTDS</c>
    /// attribute (S-101 Feature Catalogue) carried by <paramref name="feature"/>
    /// — across simple and complex (sub-)attributes — to its referenced text
    /// content via <paramref name="resolver"/>, in first-seen order with
    /// duplicate file names collapsed. Returns an empty array when no resolver
    /// is available, the feature carries no file references, or none resolve.
    /// </summary>
    internal static IReadOnlyList<ReferencedText> ResolveReferencedTexts(
        IS100Feature? feature, Func<string, string?>? resolver)
    {
        if (feature is null || resolver is null)
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        List<ReferencedText>? builder = null;

        void Add(string code, string value)
        {
            if (!FileReferenceAttributeCodes.Contains(code)
                || string.IsNullOrWhiteSpace(value)
                || !seen.Add(value))
            {
                return;
            }

            if (resolver(value) is { Length: > 0 } text)
            {
                (builder ??= new List<ReferencedText>()).Add(new ReferencedText(value, text));
            }
        }

        foreach (var (code, value) in feature.Attributes)
        {
            Add(code, value);
        }

        foreach (var complex in feature.ComplexAttributes)
        {
            foreach (var (code, value) in complex.SubAttributes)
            {
                Add(code, value);
            }
        }

        return builder is null ? [] : builder;
    }

    private static IReadOnlyList<GeoPoint> ToRing(IReadOnlyList<GeoPosition> ring)
    {
        var builder = new List<GeoPoint>(ring.Count);
        foreach (var (lat, lon) in ring)
        {
            builder.Add(new GeoPoint(lat, lon));
        }
        return builder;
    }

    private static double Area(BoundingBox b) =>
        Math.Max(0.0, b.NorthLatitude - b.SouthLatitude) * Math.Max(0.0, b.EastLongitude - b.WestLongitude);

    private double Meters(GeoPoint a, GeoPosition b)
    {
        var dLat = (a.Latitude - b.Latitude) * MetersPerDegreeLatitude;
        var dLon = (a.Longitude - b.Longitude) * MetersPerDegreeLatitude
            * Math.Cos((a.Latitude + b.Latitude) * 0.5 * Math.PI / 180.0);
        return Math.Sqrt(dLat * dLat + dLon * dLon);
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

    private record struct Hit
    {
        public DatasetId DatasetId { get; init; }
        public SpecRef Spec { get; init; }
        public string FeatureId { get; init; }
        public string FeatureType { get; init; }
        public int Specificity { get; init; }
        public BoundingBox? Bounds { get; init; }
        public double Area { get; init; }
        public bool Inside { get; init; }
        public double DistanceMeters { get; init; }
        public Func<string, string?>? Resolver { get; init; }
        public IS100Feature? Feature { get; init; }
    }
}
