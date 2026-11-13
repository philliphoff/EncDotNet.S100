using System.ComponentModel;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Geometry;
using EncDotNet.S100.Datasets.Pipelines.Spec;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Features;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Query;

/// <summary>
/// Request payload for <see cref="IdentifyFeaturesService"/>.
/// </summary>
/// <param name="Latitude">Pick latitude (decimal degrees, WGS-84). Must be in <c>[-90, 90]</c>.</param>
/// <param name="Longitude">
/// Pick longitude (decimal degrees, WGS-84). Must be in <c>[-540, 540]</c>: a
/// longitude past ±180° (as a pick on a map's adjacent world copy reads)
/// matches the same place one world east or west.
/// </param>
/// <param name="Spec">Optional spec filter; <c>null</c> matches every vector spec.</param>
/// <param name="RadiusMeters">
/// Search tolerance for point and curve features (metres). A point or
/// curve feature matches when it lies within this distance of the pick.
/// Area features always use exact point-in-polygon containment and ignore
/// the radius. Clamped to <c>[0, 100000]</c>.
/// </param>
/// <param name="MaxResults">Maximum ranked matches to return; clamped to <c>[1, 200]</c>.</param>
/// <param name="Time">
/// The time (UTC) to identify at: time-dependent products (S-412, CAP,
/// surface analyses, tropical cyclones) return only the features drawn then.
/// <see langword="null"/> follows the host's map (<see cref="IMapDrawState"/>)
/// when it has one, else identifies every drawable feature.
/// </param>
public sealed record IdentifyFeaturesRequest(
    [property: Description("Pick latitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-90, 90].")] double Latitude,
    [property: Description("Pick longitude in decimal degrees on WGS-84 (EPSG:4326). Must be in [-540, 540]; a value past ±180 (a point on an adjacent world copy) matches the same place one world east or west, so data kept in a 0…360 or other continuous longitude frame is found either way.")] double Longitude,
    [property: Description("Optional spec filter; null matches every vector spec.")] SpecRef? Spec = null,
    [property: Description("Search tolerance for point/curve features in metres; area features use exact containment and ignore it. Clamped to [0, 100000]. Default 50.")] double RadiusMeters = 50.0,
    [property: Description("Maximum ranked matches to return; clamped to [1, 200]. Default 20.")] int MaxResults = 20,
    [property: Description("Optional UTC ISO-8601 time to identify at: time-dependent products (S-412, CAP, surface analyses, tropical cyclones) return only the features drawn then. Omit to follow the host's map (SoundCharts: the view time and the warnings it shows), or, in a headless host, to identify every drawable feature.")] DateTimeOffset? Time = null);

/// <summary>
/// A single feature picked at the query point by
/// <see cref="IdentifyFeaturesService"/>, in pick-report order.
/// </summary>
/// <param name="DatasetId">Dataset the feature belongs to.</param>
/// <param name="Spec">Spec the dataset declares.</param>
/// <param name="FeatureId">Stable feature identifier (<c>gml:id</c>; for S-101 the decimal RCID).</param>
/// <param name="FeatureType">Feature type code (the GML element local name; for S-101 the feature-type acronym).</param>
/// <param name="Geometry">Geometry primitive: <c>point</c>, <c>curve</c>, or <c>surface</c>.</param>
/// <param name="Bounds">Bounding box of the feature's geometry.</param>
/// <param name="Containment"><c>inside</c> when the pick is within an area feature; <c>near</c> when within the radius of a point/curve feature.</param>
/// <param name="DistanceMeters">Approximate distance from the pick to the feature (0 for <c>inside</c>); <c>null</c> when not computed.</param>
/// <param name="ReferencedTexts">Resolved external text files referenced by the feature's <c>fileReference</c> / <c>TXTDSC</c> / <c>NTXTDS</c> attributes, or by those of the information types it links to; empty when none or unresolvable.</param>
/// <param name="DrawingPriority">The highest drawing priority of the feature's drawing instructions in the dataset's portrayal; <c>null</c> when it isn't drawn or the dataset wasn't portrayed.</param>
/// <param name="FeatureTypeName">The feature type's catalogue name ("Obstruction"); <c>null</c> where the product doesn't report it.</param>
/// <param name="Attributes">The feature's attributes as Object Information shows them; <c>null</c> where the product doesn't report them.</param>
/// <param name="Aml">Where an AML feature comes from and whether it's drawn; <c>null</c> for every other product.</param>
/// <param name="Sections">Pick report content that isn't attribute rows, such as an ice egg code; <c>null</c> when the feature has none.</param>
public sealed record IdentifyMatch(
    [property: Description("Dataset the feature belongs to.")] DatasetId DatasetId,
    [property: Description("Spec the dataset declares.")] SpecRef Spec,
    [property: Description("Stable feature identifier (gml:id; for S-101 the decimal RCID).")] string FeatureId,
    [property: Description("Feature type code (GML element local name; for S-101 the feature-type acronym).")] string FeatureType,
    [property: Description("Geometry primitive: point, curve, or surface.")] string Geometry,
    [property: Description("Bounding box of the feature's geometry.")] BoundingBox? Bounds,
    [property: Description("'inside' when the pick is within an area feature; 'near' when within the radius of a point/curve feature.")] string Containment,
    [property: Description("Approximate distance from the pick to the feature in metres (0 for 'inside'); null when not computed.")] double? DistanceMeters,
    [property: Description("Resolved external text files referenced by the feature's fileReference / TXTDSC / NTXTDS attributes, or by those of the information types it links to; empty when none or unresolvable.")] IReadOnlyList<ReferencedText>? ReferencedTexts = null,
    [property: Description("Highest drawing priority of the feature's drawing instructions in the dataset's portrayal; null when it isn't drawn or the dataset wasn't portrayed.")] int? DrawingPriority = null,
    [property: Description("Feature type name from the product's catalogue (\"Obstruction\" for an AML OBSTRN). Reported for NATO Additional Military Layers (AML) features; null otherwise.")] string? FeatureTypeName = null,
    [property: Description("The feature's attributes as the viewer's Object Information shows them: catalogue names, decoded values, and on a standard S-57 class the AML-only attributes last with mark \"AML\". Reported for AML features; null otherwise.")] IReadOnlyList<IdentifyAttribute>? Attributes = null,
    [property: Description("Provenance of a NATO Additional Military Layers (AML) feature (product tag, cell, protective marking) and whether it's drawn; null for every other product.")] IdentifyAmlFeature? Aml = null,
    [property: Description("Object Information content that isn't attribute rows, such as the WMO ice egg code of a sea-ice feature; null when the feature has none.")] IReadOnlyList<PickSectionResult>? Sections = null);

/// <summary>One attribute of an <see cref="IdentifyMatch"/>, as Object Information shows it.</summary>
/// <param name="Code">The attribute acronym as encoded (<c>CATOBS</c>, <c>sonsig</c>).</param>
/// <param name="Name">The catalogue name ("Category of obstruction"); <c>null</c> when the catalogue doesn't have it.</param>
/// <param name="Value">The value to show: decoded names for enumerated and list values, otherwise the value as encoded.</param>
/// <param name="RawValue">The value as encoded ("6", "1,3").</param>
/// <param name="Mark"><c>"AML"</c> on an attribute an AML catalogue adds to a standard S-57 class; <c>null</c> otherwise.</param>
public sealed record IdentifyAttribute(
    [property: Description("Attribute acronym as encoded (CATOBS, sonsig).")] string Code,
    [property: Description("Catalogue name of the attribute (\"Category of obstruction\"); null when the catalogue doesn't define it.")] string? Name,
    [property: Description("Value to show: the decoded names of enumerated and list values, otherwise the value as encoded.")] string Value,
    [property: Description("Value as encoded (\"6\", \"1,3\").")] string RawValue,
    [property: Description("\"AML\" on an attribute an AML catalogue adds to a standard S-57 class (listed after the standard ones); null otherwise.")] string? Mark = null)
{
    internal static IdentifyAttribute From(PickAttribute attribute) =>
        new(attribute.Code, attribute.Name, attribute.DisplayText, attribute.RawValue, attribute.Mark);
}

/// <summary>
/// The provenance strip of an AML feature (#1372, design decision 13): the
/// AML tag, the cell and the protective marking, plus whether it's drawn.
/// </summary>
/// <param name="Tag">The AML tag, "AML · LBO" or "AML 3.0 · SBO".</param>
/// <param name="Product">The product code (LBO, SBO), or <c>null</c> for an AML 3.0 cell whose name gives none.</param>
/// <param name="ProductName">The product's full name.</param>
/// <param name="Cell">The cell (data set) name.</param>
/// <param name="ProtectiveMarking">The cell's protective marking, as given; <c>null</c> when the cell has none.</param>
/// <param name="ClassKind"><c>"aml"</c> for a class an AML catalogue adds, <c>"standard"</c> for a standard S-57 class.</param>
/// <param name="Drawn">Whether the map draws the feature.</param>
/// <param name="Note">"Not drawn. Picked from its position." when it isn't drawn; otherwise <c>null</c>.</param>
public sealed record IdentifyAmlFeature(
    [property: Description("AML tag, \"AML · LBO\" or \"AML 3.0 · SBO\".")] string Tag,
    [property: Description("AML product code (LBO, SBO, CLB …); null for an AML 3.0 cell whose name gives none.")] string? Product,
    [property: Description("Full product name.")] string ProductName,
    [property: Description("Cell (data set) name.")] string Cell,
    [property: Description("The cell's protective marking, as given (\"UNCLASSIFIED\"); null when the cell has none.")] string? ProtectiveMarking,
    [property: Description("\"aml\" for an object class an AML catalogue adds; \"standard\" for a standard S-57 class.")] string ClassKind,
    [property: Description("Whether the map draws this feature. AML features are pickable whether or not they're drawn.")] bool Drawn,
    [property: Description("\"Not drawn. Picked from its position.\" when the feature isn't drawn; null otherwise.")] string? Note);

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
/// <param name="Features">Ranked matches: drawn features by drawing priority, highest first, where the dataset was portrayed (S-98 15.1); then most-specific first (point before curve before area; ties broken by smaller area / nearer distance).</param>
/// <param name="TotalMatched">Total number of features that matched before applying <see cref="IdentifyFeaturesRequest.MaxResults"/>.</param>
/// <param name="Truncated"><c>true</c> when more features matched than were returned.</param>
public sealed record IdentifyFeaturesResult(
    [property: Description("The echoed pick point.")] GeoPoint Point,
    [property: Description("Ranked matches: drawn features by drawing priority, highest first, where the dataset was portrayed (S-98 15.1); then most-specific first (point before curve before area; ties broken by smaller area / nearer distance).")] IReadOnlyList<IdentifyMatch> Features,
    [property: Description("Total number of features that matched before applying maxResults.")] int TotalMatched,
    [property: Description("True when more features matched than were returned.")] bool Truncated);

/// <summary>
/// Identifies the vector features at a geographic point — the ECDIS
/// cursor-pick interaction — ranked by drawing priority where the dataset
/// was portrayed (S-98 15.1), then most-specific first.
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

    // Three worlds: the standard one and a world copy either side, as a map
    // that repeats data across world copies shows them.
    private const double MaxLongitude = 540.0;

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
    private readonly IMapDrawState? _drawState;

    /// <summary>
    /// Creates a new <see cref="IdentifyFeaturesService"/> for a host with no
    /// map: a request with no time identifies every drawable feature.
    /// </summary>
    public IdentifyFeaturesService(IDatasetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <summary>
    /// Creates a new <see cref="IdentifyFeaturesService"/> for a host whose
    /// map draws as <paramref name="drawState"/> says: a request with no
    /// time identifies what the map draws.
    /// </summary>
    /// <param name="catalog">The catalog.</param>
    /// <param name="drawState">What the host's map draws.</param>
    public IdentifyFeaturesService(IDatasetCatalog catalog, IMapDrawState drawState)
        : this(catalog)
    {
        ArgumentNullException.ThrowIfNull(drawState);
        _drawState = drawState;
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

        if (double.IsNaN(request.Longitude) || request.Longitude < -MaxLongitude || request.Longitude > MaxLongitude)
        {
            return Task.FromResult(ToolResult<IdentifyFeaturesResult>.Err(
                new InvalidArgument("longitude", $"value {request.Longitude} is outside the range [-{MaxLongitude}, {MaxLongitude}]")));
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

        // Spatial pre-filter passed down to FeatureAccessor: skips
        // features whose MBR doesn't touch the pick box before we
        // compute per-feature containment / distance. Index-backed
        // sources (S-101) answer this in sub-linear time; other
        // products fall back to a linear MBR scan (still cheaper than
        // resolving distance for every feature). See issue #490.
        var snapshot = _catalog.Datasets;
        var hits = new List<Hit>();

        // An explicit time identifies what a render then draws; without one,
        // follow the host's map: its time and the features it hides.
        var followsMap = request.Time is null && _drawState is not null;
        var time = request.Time ?? _drawState?.Time;

        foreach (var dataset in snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (request.Spec is { } spec && !SpecMatches(dataset.Spec, spec))
            {
                continue;
            }

            // A dataset kept in a 0…360 or other continuous frame holds the
            // place one world east or west of a standard longitude, and a
            // pick on a map's adjacent world copy reads past ±180°: match the
            // dataset at whichever of those longitudes it covers (issue #773).
            if (DatasetLongitude(dataset.Bounds, request.Latitude, request.Longitude, latPad, lonPad) is not { } longitude)
            {
                continue;
            }

            var datasetPoint = new GeoPoint(request.Latitude, longitude);
            var pickExtent = new BoundingBox(
                Math.Clamp(request.Latitude - latPad, -90.0, 90.0),
                longitude - lonPad,
                Math.Clamp(request.Latitude + latPad, -90.0, 90.0),
                longitude + lonPad);
            var features = FeatureAccessor.GetFeatures(dataset, pickExtent, time);
            if (features is null)
            {
                continue;
            }

            var hidden = followsMap ? FeatureAccessor.UndrawnFeatures(dataset, _drawState!.HiddenFeatures(dataset)) : null;

            var s101 = dataset.Data as S101DatasetData;
            var aml = dataset.Data as AmlDatasetData;
            var resolver = s101?.ExternalTextResolver;
            var priorityOf = s101?.DrawingPriority;

            foreach (var feature in features)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (hidden is not null && hidden.Contains(feature.Id))
                {
                    continue;
                }

                // An AML cell's meta features describe the cell, not a place.
                if (aml?.Cell is { } amlCell && amlCell.IsMetaClass(feature.FeatureType))
                {
                    continue;
                }

                if (TryMatch(feature, datasetPoint, latPad, lonPad, out var hit)
                    && (hit.Inside || hit.DistanceMeters <= radius))
                {
                    hits.Add(hit with { DatasetId = dataset.Id, Spec = dataset.Spec, Resolver = resolver, Feature = feature, Document = s101?.Dataset.Document, DrawingPriority = priorityOf?.Invoke(hit.FeatureId) ?? aml?.DrawingPriority?.Invoke(hit.FeatureId), Aml = aml });
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
            var amlInfo = AmlFeatureInfo(h);
            builder.Add(new IdentifyMatch(
                h.DatasetId,
                h.Spec,
                h.FeatureId,
                h.FeatureType,
                PrimitiveName(h.Specificity),
                h.Bounds,
                h.Inside ? "inside" : "near",
                h.Inside ? 0.0 : h.DistanceMeters,
                ResolveReferencedTexts(h.Feature, h.Resolver, LinkedFileReferences(h)),
                h.DrawingPriority,
                amlInfo?.Info.FeatureTypeName,
                amlInfo?.Info.Attributes.Select(IdentifyAttribute.From).ToList(),
                amlInfo?.Provenance,
                h.Feature is { } feature ? PickSectionResult.For(h.Spec, feature, amlInfo?.Info.Attributes) : null));
        }

        return Task.FromResult(ToolResult<IdentifyFeaturesResult>.Ok(
            new IdentifyFeaturesResult(
                point,
                builder,
                total,
                take < total)));
    }

    /// <summary>
    /// The longitude at which <paramref name="bounds"/> (padded) holds the
    /// pick: <paramref name="longitude"/> itself, else the same place one
    /// world east or west; <see langword="null"/> when none is covered.
    /// </summary>
    private static double? DatasetLongitude(
        BoundingBox bounds, double latitude, double longitude, double latPad, double lonPad)
    {
        var padded = Inflated(bounds, latPad, lonPad);
        foreach (var candidate in (ReadOnlySpan<double>)[longitude, longitude + 360.0, longitude - 360.0])
        {
            if (SpatialPredicates.Contains(padded, new GeoPoint(latitude, candidate)))
            {
                return candidate;
            }
        }

        return null;
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
            // Each surface with its own holes; see IS100Feature.Surfaces.
            bool inside = feature.Surfaces.Any(surface =>
                SpatialPredicates.ContainsPoint(ToRing(surface.ExteriorRing), point)
                && !surface.InteriorRings.Any(hole =>
                    hole.Count > 0 && SpatialPredicates.ContainsPoint(ToRing(hole), point)));
            if (!inside)
            {
                return false;
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

    // S-98 15.1: drawn features by drawing priority, highest first, then
    // features with none (not drawn, or no portrayal). Then most-specific
    // first: point (0) < curve (1) < area (2); then within a primitive the
    // nearer / smaller feature wins; then a stable tiebreak.
    private static int CompareHits(Hit a, Hit b)
    {
        var c = PickOrder.ComparePriority(a.DrawingPriority, b.DrawingPriority);
        if (c != 0) return c;

        c = a.Specificity.CompareTo(b.Specificity);
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
    /// — across simple and complex (sub-)attributes — and every file named in
    /// <paramref name="linkedFileReferences"/> (the feature's associated
    /// information types) to its referenced text
    /// content via <paramref name="resolver"/>, in first-seen order with
    /// duplicate file names collapsed. Returns an empty array when no resolver
    /// is available, the feature carries no file references, or none resolve.
    /// </summary>
    internal static IReadOnlyList<ReferencedText> ResolveReferencedTexts(
        IS100Feature? feature, Func<string, string?>? resolver, IEnumerable<string>? linkedFileReferences = null)
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

        foreach (var value in linkedFileReferences ?? [])
        {
            Add("fileReference", value);
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

    // The Object Information of an AML hit, and its provenance strip (#1372).
    private static (FeatureInfo Info, IdentifyAmlFeature Provenance)? AmlFeatureInfo(Hit hit)
    {
        if (hit.Aml is not { Cell: { } cell } || cell.Find(hit.FeatureId) is not { } feature)
        {
            return null;
        }

        var info = cell.BuildFeatureInfo(feature, hit.DrawingPriority);
        var drawn = hit.DrawingPriority is not null;
        var amlInfo = cell.Info;
        return (info, new IdentifyAmlFeature(
            amlInfo.TagText,
            amlInfo.ProductCode,
            amlInfo.ProductName,
            amlInfo.DataSetName,
            amlInfo.ProtectiveMarking,
            cell.IsAmlClass(feature.FeatureType) ? "aml" : "standard",
            drawn,
            drawn ? null : AmlCellFeatures.NotDrawnNote));
    }

    private record struct Hit
    {
        public AmlDatasetData? Aml { get; init; }
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
        public S101Document? Document { get; init; }
        public int? DrawingPriority { get; init; }
    }

    // The fileReference values of the information types an S-101 feature
    // links to (#1157): a caution area's note sits on a NauticalInformation
    // record, not on the feature.
    private static IEnumerable<string>? LinkedFileReferences(Hit hit)
    {
        if (hit.Document is not { } document
            || !uint.TryParse(hit.FeatureId, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var recordId))
        {
            return null;
        }

        foreach (var record in document.Features)
        {
            if (record.RecordId == recordId)
                return S101PickReport.InformationFileReferences(document, record);
        }

        return null;
    }
}
