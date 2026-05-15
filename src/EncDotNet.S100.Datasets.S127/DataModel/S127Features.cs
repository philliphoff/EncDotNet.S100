using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S127.DataModel;

/// <summary>
/// Typed projection of an S-127 <c>PilotBoardingPlace</c> feature
/// (S-127 Edition 2.0.0 §12). The boarding place may be a point or a
/// surface; the administering <see cref="Authority"/> is resolved from
/// the <c>theAuthority</c> xlink when present.
/// </summary>
public sealed class S127PilotBoardingPlace : IS127Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public string FeatureType => "PilotBoardingPlace";

    /// <inheritdoc/>
    public S127GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <inheritdoc/>
    public required S127Feature Source { get; init; }

    /// <summary>
    /// The <c>categoryOfPilotBoardingPlace</c> code (S-127 Edition 2.0.0
    /// §12), when supplied. Refer to the S-127 Feature Catalogue for the
    /// enumeration values.
    /// </summary>
    public int? CategoryOfPilotBoardingPlace { get; init; }

    /// <summary>The administering authority, resolved from the <c>theAuthority</c> xlink when present.</summary>
    public IS127Feature? Authority { get; init; }

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-127 <c>RouteingMeasure</c> feature
/// (S-127 Edition 2.0.0 §12). Routeing measures are curve features
/// describing IMO-style routeing arrangements (traffic separation
/// schemes, deep-water routes, etc.).
/// </summary>
public sealed class S127RouteingMeasure : IS127Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public string FeatureType => "RouteingMeasure";

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

/// <summary>
/// Typed projection of an S-127 <c>VesselTrafficServiceArea</c> feature
/// (S-127 Edition 2.0.0 §12). The administering <see cref="Authority"/>
/// is resolved from the <c>theAuthority</c> xlink when present.
/// </summary>
public sealed class S127VesselTrafficServiceArea : IS127Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public string FeatureType => "VesselTrafficServiceArea";

    /// <inheritdoc/>
    public S127GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <inheritdoc/>
    public required S127Feature Source { get; init; }

    /// <summary>The administering authority, resolved from the <c>theAuthority</c> xlink when present.</summary>
    public IS127Feature? Authority { get; init; }

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-127 <c>ShipReportingService</c> feature
/// (S-127 Edition 2.0.0 §12). The administering <see cref="Authority"/>
/// is resolved from the <c>theAuthority</c> xlink when present.
/// </summary>
public sealed class S127ShipReportingService : IS127Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public string FeatureType => "ShipReportingService";

    /// <inheritdoc/>
    public S127GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <inheritdoc/>
    public required S127Feature Source { get; init; }

    /// <summary>The administering authority, resolved from the <c>theAuthority</c> xlink when present.</summary>
    public IS127Feature? Authority { get; init; }

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-127 signal-station feature
/// (S-127 Edition 2.0.0 §12 — <c>SignalStationTraffic</c>,
/// <c>SignalStationWarning</c>). The discriminator is exposed on
/// <see cref="Kind"/>; the source feature class code remains on
/// <see cref="FeatureType"/>.
/// </summary>
public sealed class S127SignalStation : IS127Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public required string FeatureType { get; init; }

    /// <summary>The flavour of signal station, decoded from <see cref="FeatureType"/>.</summary>
    public S127SignalStationKind Kind { get; init; }

    /// <inheritdoc/>
    public S127GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <inheritdoc/>
    public required S127Feature Source { get; init; }

    /// <summary>The administering authority, resolved from the <c>theAuthority</c> xlink when present.</summary>
    public IS127Feature? Authority { get; init; }

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of the family of S-127 regulated / restricted /
/// service area features (S-127 Edition 2.0.0 §12 — <c>RestrictedArea</c>,
/// <c>RestrictedAreaNavigational</c>, <c>MilitaryPracticeArea</c>,
/// <c>CautionArea</c>, <c>PiracyRiskArea</c>,
/// <c>ConcentrationOfShippingHazardArea</c>, <c>SupervisedArea</c>,
/// <c>LocalPortBroadcastServiceArea</c>,
/// <c>UnderKeelClearanceManagementArea</c>,
/// <c>UnderKeelClearanceAllowanceArea</c>). The discriminator is on
/// <see cref="Kind"/>; the source FC code remains on
/// <see cref="FeatureType"/>.
/// </summary>
public sealed class S127RegulatedArea : IS127Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public required string FeatureType { get; init; }

    /// <summary>The flavour of regulated area, decoded from <see cref="FeatureType"/>.</summary>
    public S127RegulatedAreaKind Kind { get; init; }

    /// <inheritdoc/>
    public S127GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <inheritdoc/>
    public required S127Feature Source { get; init; }

    /// <summary>
    /// The primary category code carried by the area (e.g.
    /// <c>categoryOfRestrictedArea</c> for §RestrictedArea,
    /// <c>categoryOfMilitaryPracticeArea</c> for §MilitaryPracticeArea),
    /// when supplied.
    /// </summary>
    public int? CategoryCode { get; init; }

    /// <summary>The administering authority, resolved from the <c>theAuthority</c> xlink when present.</summary>
    public IS127Feature? Authority { get; init; }

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-127 <c>Authority</c> feature
/// (S-127 Edition 2.0.0 §12). Authorities are container-style features
/// that may have no geometry; service-area features bind to them via
/// the <c>theAuthority</c> xlink.
/// </summary>
public sealed class S127Authority : IS127Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public string FeatureType => "Authority";

    /// <inheritdoc/>
    public S127GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <inheritdoc/>
    public required S127Feature Source { get; init; }

    /// <summary>The authority name (FC: <c>authorityName</c>), when supplied.</summary>
    public string? AuthorityName { get; init; }

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
