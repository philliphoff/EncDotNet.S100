using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S127.DataModel;

/// <summary>
/// The geometry primitive kind of an S-127 feature when surfaced through
/// the typed projection. Mirrors the S-124 / S-125 / S-201 typed-model
/// shape: callers get a discriminator plus a single
/// <see cref="ImmutableArray{T}"/> of <see cref="GeoPosition"/>s whose
/// semantics depend on the kind.
/// </summary>
public enum S127GeometryKind
{
    /// <summary>No geometry. Container features such as <c>Authority</c> carry this (S-127 Edition 2.0.0 §12).</summary>
    None,

    /// <summary>A single point (one coordinate).</summary>
    Point,

    /// <summary>A curve — an ordered, open sequence of coordinates.</summary>
    Curve,

    /// <summary>A surface — a closed exterior ring (interior rings remain on <c>Source</c>).</summary>
    Surface,
}

/// <summary>
/// Discriminator for <see cref="S127SignalStation"/>
/// (S-127 Edition 2.0.0 §12 — <c>SignalStationTraffic</c>,
/// <c>SignalStationWarning</c>).
/// </summary>
public enum S127SignalStationKind
{
    /// <summary>Forward-compatibility fallback when the source feature class is unrecognised.</summary>
    Unknown,

    /// <summary>§SignalStationTraffic.</summary>
    Traffic,

    /// <summary>§SignalStationWarning.</summary>
    Warning,
}

/// <summary>
/// Discriminator for <see cref="S127RegulatedArea"/>
/// (S-127 Edition 2.0.0 §12) — the family of S-127 area features that
/// describe a regulated, restricted, or service area.
/// </summary>
public enum S127RegulatedAreaKind
{
    /// <summary>Forward-compatibility fallback for an unrecognised area code.</summary>
    Unknown,

    /// <summary>§RestrictedArea.</summary>
    RestrictedArea,

    /// <summary>§RestrictedAreaNavigational.</summary>
    RestrictedAreaNavigational,

    /// <summary>§MilitaryPracticeArea.</summary>
    MilitaryPracticeArea,

    /// <summary>§CautionArea.</summary>
    CautionArea,

    /// <summary>§PiracyRiskArea.</summary>
    PiracyRiskArea,

    /// <summary>§ConcentrationOfShippingHazardArea.</summary>
    ConcentrationOfShippingHazardArea,

    /// <summary>§SupervisedArea.</summary>
    SupervisedArea,

    /// <summary>§LocalPortBroadcastServiceArea.</summary>
    LocalPortBroadcastServiceArea,

    /// <summary>§UnderKeelClearanceManagementArea.</summary>
    UnderKeelClearanceManagementArea,

    /// <summary>§UnderKeelClearanceAllowanceArea.</summary>
    UnderKeelClearanceAllowanceArea,
}

/// <summary>
/// Common contract exposed by every typed S-127 feature surfaced by
/// <see cref="S127MarineServicesDataset"/>. Allows callers to enumerate
/// <see cref="S127MarineServicesDataset.Features"/> uniformly and dispatch
/// on <see cref="FeatureType"/> when the FC discriminator matters.
/// </summary>
public interface IS127Feature
{
    /// <summary>The GML identifier of the source feature.</summary>
    string Id { get; }

    /// <summary>
    /// The raw S-127 feature type code as it appears in the source GML
    /// (e.g. <c>"PilotBoardingPlace"</c>). Drives FC-specific dispatch
    /// when a typed property is not enough.
    /// </summary>
    string FeatureType { get; }

    /// <summary>The geometry primitive kind of <see cref="Coordinates"/>.</summary>
    S127GeometryKind GeometryKind { get; }

    /// <summary>
    /// Flat coordinate list whose semantics depend on
    /// <see cref="GeometryKind"/>. Multi-curve or multi-ring source
    /// geometry is flattened in source order — full structure is
    /// available on <see cref="Source"/>.
    /// </summary>
    ImmutableArray<GeoPosition> Coordinates { get; }

    /// <summary>The originating feature record from the feature-bag dataset.</summary>
    S127Feature Source { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    ImmutableDictionary<string, string> ExtraAttributes { get; }
}

/// <summary>
/// Catch-all typed projection for S-127 feature classes the typed model
/// does not break out individually (e.g. <c>RadarRange</c>,
/// <c>RadioCallingInPoint</c>, <c>PilotService</c>,
/// <c>PilotageDistrict</c>, <c>PlaceOfRefuge</c>, <c>WaterwayArea</c>,
/// <c>DataCoverage</c>, <c>QualityOfNonBathymetricData</c>,
/// <c>TextPlacement</c>, <c>ISPSCodeSecurityLevel</c>,
/// <c>ReportableServiceArea</c>, <c>OrganizationContactArea</c>,
/// <c>PilotageDistrictAssociation</c>,
/// <c>TrafficControlServiceAggregation</c> — S-127 Edition 2.0.0 §12).
/// All source attributes survive round-trip via
/// <see cref="ExtraAttributes"/> so future passes can promote individual
/// classes without breaking callers.
/// </summary>
public sealed class S127OtherFeature : IS127Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public required string FeatureType { get; init; }

    /// <inheritdoc/>
    public S127GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <inheritdoc/>
    public required S127Feature Source { get; init; }

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
