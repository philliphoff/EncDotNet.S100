using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S129.DataModel;

/// <summary>
/// A time range (S-129 Edition 2.0.0 §<c>fixedTimeRange</c>) — the validity
/// window during which a UKC plan or control-point measurement applies.
/// </summary>
/// <remarks>
/// Either bound may be <c>null</c>: a missing <see cref="Start"/> indicates
/// an open-start interval, a missing <see cref="End"/> indicates an
/// open-end interval. Per S-100 Part 5 §10, both values are normalised to
/// UTC by <see cref="AttributeParser.TryParseDateTimeOffset"/>.
/// </remarks>
public sealed record S129TimeRange
{
    /// <summary>Start of the interval (inclusive), or <c>null</c> if open-start.</summary>
    public DateTimeOffset? Start { get; init; }

    /// <summary>End of the interval (inclusive), or <c>null</c> if open-end.</summary>
    public DateTimeOffset? End { get; init; }
}

/// <summary>
/// A multilingual feature name as carried by S-129 Edition 2.0.0
/// §<c>featureName</c> — a complex attribute with a language code, a
/// display name, and a name-usage classifier.
/// </summary>
public sealed record S129FeatureName
{
    /// <summary>ISO 639 language code (e.g. <c>"en"</c>).</summary>
    public string? Language { get; init; }

    /// <summary>The display name.</summary>
    public string? Name { get; init; }

    /// <summary>The name-usage text label (S-129 listed value <c>nameUsage</c>; e.g. <c>"Default Name Display"</c>).</summary>
    public string? NameUsage { get; init; }
}

/// <summary>
/// A cross-product reference carried by an S-129 dataset to another S-100
/// product instance (typically S-421 route, S-102 bathymetry, or S-104
/// water level).
/// </summary>
/// <remarks>
/// <para>
/// In S-129 Edition 2.0.0, these links are usually <em>textual</em>: a
/// vessel MMSI / IMO number, a route name plus version, etc. — i.e. the
/// referenced dataset is identified by its producer-assigned identifier
/// rather than by a GML <c>xlink:href</c> URL. This type preserves that
/// information verbatim so downstream code can resolve it against a
/// catalogue (e.g. an S-421 route library) at its own discretion.
/// </para>
/// <para>
/// <see cref="Identifier"/> is the producer identifier (e.g. the route
/// name); <see cref="Version"/> is the optional version stamp (e.g. the
/// S-421 <c>routeInfoEditionNumber</c>). Neither field is resolved
/// eagerly — the typed projection never requires the referenced dataset
/// to be present.
/// </para>
/// </remarks>
public sealed record S129ExternalReference
{
    /// <summary>The kind of referenced object (e.g. <c>"S-421 route"</c>, <c>"vessel"</c>).</summary>
    public required string Kind { get; init; }

    /// <summary>The producer-assigned identifier of the referenced object.</summary>
    public required string Identifier { get; init; }

    /// <summary>Optional version / edition stamp (e.g. an S-421 route version number).</summary>
    public string? Version { get; init; }
}

/// <summary>
/// The geometry primitive kind of an S-129 feature.
/// </summary>
public enum S129GeometryKind
{
    /// <summary>No geometry (e.g. the metadata-only <c>UnderKeelClearancePlan</c>).</summary>
    None,
    /// <summary>Single point (<see cref="GeoPosition"/>).</summary>
    Point,
    /// <summary>Surface exterior ring (closed sequence of <see cref="GeoPosition"/>).</summary>
    Surface,
}

/// <summary>
/// Typed projection of the <c>UnderKeelClearancePlan</c> feature
/// (S-129 Edition 2.0.0): the metadata header that describes the
/// computed UKC plan as a whole.
/// </summary>
/// <remarks>
/// This is the "plan" feature itself — a metadata record with no
/// geometry. The plan's spatial extent is carried by a separate
/// <see cref="S129UkcPlanArea"/> feature; the per-waypoint UKC
/// measurements are carried by <see cref="S129ControlPoint"/> features.
/// </remarks>
public sealed class S129UkcPlanMetadata
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>The fixed time range during which the plan applies (S-129 §<c>fixedTimeRange</c>).</summary>
    public S129TimeRange? FixedTimeRange { get; init; }

    /// <summary>The instant at which the producer generated the plan (S-129 §<c>generationTime</c>).</summary>
    public DateTimeOffset? GenerationTime { get; init; }

    /// <summary>Vessel identifier (MMSI / IMO) the plan applies to (S-129 §<c>vesselID</c>).</summary>
    public string? VesselId { get; init; }

    /// <summary>
    /// Typed reference to the source S-421 route the plan was computed
    /// against. <c>null</c> when <c>sourceRouteName</c> is absent on the
    /// source feature.
    /// </summary>
    public S129ExternalReference? SourceRoute { get; init; }

    /// <summary>The maximum draught used as input to the UKC calculation (S-129 §<c>maximumDraught</c>).</summary>
    public double? MaximumDraught { get; init; }

    /// <summary>The UKC purpose text label (S-129 listed value <c>underKeelClearancePurpose</c>).</summary>
    public string? UnderKeelClearancePurpose { get; init; }

    /// <summary>The UKC calculation-requested text label (S-129 listed value <c>underKeelClearanceCalculationRequested</c>).</summary>
    public string? UnderKeelClearanceCalculationRequested { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of the <c>UnderKeelClearancePlanArea</c> feature
/// (S-129 Edition 2.0.0): the spatial extent of a UKC plan as a single
/// surface.
/// </summary>
public sealed class S129UkcPlanArea
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>Geometry primitive kind. Expected to be <see cref="S129GeometryKind.Surface"/>.</summary>
    public S129GeometryKind GeometryKind { get; init; }

    /// <summary>The exterior-ring coordinates of the plan area.</summary>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } =
        ImmutableArray<GeoPosition>.Empty;

    /// <summary>Interior-ring coordinates (holes), if any.</summary>
    public ImmutableArray<ImmutableArray<GeoPosition>> InteriorRings { get; init; } =
        ImmutableArray<ImmutableArray<GeoPosition>>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an <c>UnderKeelClearanceNonNavigableArea</c>
/// feature (S-129 Edition 2.0.0): a surface inside the plan area that the
/// producer has classified as non-navigable for the given draught and
/// time window.
/// </summary>
public sealed class S129NonNavigableArea
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The minimum display scale (S-129 §<c>scaleMinimum</c>).</summary>
    public int? ScaleMinimum { get; init; }

    /// <summary>Geometry primitive kind. Expected to be <see cref="S129GeometryKind.Surface"/>.</summary>
    public S129GeometryKind GeometryKind { get; init; }

    /// <summary>Exterior-ring coordinates.</summary>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } =
        ImmutableArray<GeoPosition>.Empty;

    /// <summary>Interior-ring coordinates (holes), if any.</summary>
    public ImmutableArray<ImmutableArray<GeoPosition>> InteriorRings { get; init; } =
        ImmutableArray<ImmutableArray<GeoPosition>>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an <c>UnderKeelClearanceAlmostNonNavigableArea</c>
/// feature (S-129 Edition 2.0.0): a surface with marginal under-keel
/// clearance — navigable but with limited margin.
/// </summary>
public sealed class S129AlmostNonNavigableArea
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The minimum display scale (S-129 §<c>scaleMinimum</c>).</summary>
    public int? ScaleMinimum { get; init; }

    /// <summary>Geometry primitive kind. Expected to be <see cref="S129GeometryKind.Surface"/>.</summary>
    public S129GeometryKind GeometryKind { get; init; }

    /// <summary>Exterior-ring coordinates.</summary>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } =
        ImmutableArray<GeoPosition>.Empty;

    /// <summary>Interior-ring coordinates (holes), if any.</summary>
    public ImmutableArray<ImmutableArray<GeoPosition>> InteriorRings { get; init; } =
        ImmutableArray<ImmutableArray<GeoPosition>>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an <c>UnderKeelClearanceControlPoint</c> feature
/// (S-129 Edition 2.0.0): a per-waypoint UKC time-step record — the
/// producer-computed UKC value at a specific point along the route at
/// the expected passing time.
/// </summary>
/// <remarks>
/// <para>
/// The typed projection's root <see cref="S129UnderKeelClearancePlan"/>
/// orders these by <see cref="ExpectedPassingTime"/> (stable, with
/// gaps preserved per S-129 skill checklist #5).
/// </para>
/// <para>
/// <see cref="DistanceAboveUkcLimit"/> is the headline UKC value: the
/// vertical margin between the actual under-keel clearance and the
/// configured limit at this point, in metres.
/// </para>
/// </remarks>
public sealed class S129ControlPoint
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The control-point name (S-129 §<c>featureName</c>), when supplied.</summary>
    public S129FeatureName? FeatureName { get; init; }

    /// <summary>The instant the vessel is expected to pass this control point (S-129 §<c>expectedPassingTime</c>).</summary>
    public DateTimeOffset? ExpectedPassingTime { get; init; }

    /// <summary>The vessel speed expected at this control point, in knots (S-129 §<c>expectedPassingSpeed</c>).</summary>
    public double? ExpectedPassingSpeed { get; init; }

    /// <summary>The margin between actual UKC and the configured UKC limit, in metres (S-129 §<c>distanceAboveUKCLimit</c>).</summary>
    public double? DistanceAboveUkcLimit { get; init; }

    /// <summary>An optional per-CP fixed time range (S-129 §<c>fixedTimeRange</c>); usually empty when the plan itself carries one.</summary>
    public S129TimeRange? FixedTimeRange { get; init; }

    /// <summary>The point position (lat / lon, WGS-84).</summary>
    public GeoPosition? Position { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
