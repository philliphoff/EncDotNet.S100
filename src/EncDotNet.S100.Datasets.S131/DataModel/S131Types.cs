using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S131.DataModel;

/// <summary>
/// Family of an S-131 feature, statically derived from the FC supertype
/// chain (S-131 FC Edition 1.0.0). The family enum lets callers dispatch
/// on the high-level kind of harbour infrastructure without enumerating
/// 36 concrete feature codes.
/// </summary>
public enum S131FeatureFamily
{
    /// <summary>The feature code is not present in the S-131 FC.</summary>
    Unknown,

    /// <summary>
    /// Derived from <c>HarbourPhysicalInfrastructure</c> (FC §B.2) —
    /// fixed installations such as bollards, dolphins, dry docks,
    /// mooring buoys, lock basins, and ship-handling equipment.
    /// </summary>
    HarbourInfrastructure,

    /// <summary>
    /// Derived from <c>Layout</c> (FC §B.2) — the area- and
    /// berth-style features that describe the spatial layout of a
    /// harbour: berths, anchorage areas, terminals, harbour basins,
    /// pilot boarding places, dumping grounds, fender lines, etc.
    /// </summary>
    Layout,

    /// <summary>
    /// Standalone feature types that do not derive from a harbour
    /// supertype: <c>DataCoverage</c>, <c>QualityOfNonBathymetricData</c>,
    /// <c>SoundingDatum</c>, <c>VerticalDatumOfData</c>,
    /// <c>TextPlacement</c>.
    /// </summary>
    Metadata,
}

/// <summary>
/// Concrete S-131 harbour-physical-infrastructure feature kind
/// (S-131 FC Edition 1.0.0 §B.2). One entry per feature type whose
/// supertype chain rises to <c>HarbourPhysicalInfrastructure</c>.
/// </summary>
public enum S131HarbourInfrastructureKind
{
    /// <summary>Unrecognised feature code.</summary>
    Unknown,
    /// <summary>§AutomatedGuidedVehicle.</summary>
    AutomatedGuidedVehicle,
    /// <summary>§Bollard.</summary>
    Bollard,
    /// <summary>§Dolphin.</summary>
    Dolphin,
    /// <summary>§DryDock.</summary>
    DryDock,
    /// <summary>§FloatingDock.</summary>
    FloatingDock,
    /// <summary>§Gridiron.</summary>
    Gridiron,
    /// <summary>§HarbourFacility.</summary>
    HarbourFacility,
    /// <summary>§LockBasin.</summary>
    LockBasin,
    /// <summary>§LockBasinPart.</summary>
    LockBasinPart,
    /// <summary>§MooringBuoy.</summary>
    MooringBuoy,
    /// <summary>§OnshorePowerFacility.</summary>
    OnshorePowerFacility,
    /// <summary>§ShipLift.</summary>
    ShipLift,
    /// <summary>§StraddleCarrier.</summary>
    StraddleCarrier,
}

/// <summary>
/// Concrete S-131 layout feature kind (S-131 FC Edition 1.0.0 §B.2).
/// One entry per feature type whose supertype chain rises to
/// <c>Layout</c>.
/// </summary>
public enum S131LayoutKind
{
    /// <summary>Unrecognised feature code.</summary>
    Unknown,
    /// <summary>§AnchorBerth.</summary>
    AnchorBerth,
    /// <summary>§AnchorageArea.</summary>
    AnchorageArea,
    /// <summary>§Berth.</summary>
    Berth,
    /// <summary>§BerthPosition.</summary>
    BerthPosition,
    /// <summary>§DockArea.</summary>
    DockArea,
    /// <summary>§DumpingGround.</summary>
    DumpingGround,
    /// <summary>§FenderLine.</summary>
    FenderLine,
    /// <summary>§HarbourAreaAdministrative.</summary>
    HarbourAreaAdministrative,
    /// <summary>§HarbourAreaSection.</summary>
    HarbourAreaSection,
    /// <summary>§HarbourBasin.</summary>
    HarbourBasin,
    /// <summary>§MooringWarpingFacility.</summary>
    MooringWarpingFacility,
    /// <summary>§OuterLimit.</summary>
    OuterLimit,
    /// <summary>§PilotBoardingPlace.</summary>
    PilotBoardingPlace,
    /// <summary>§SeaplaneLandingArea.</summary>
    SeaplaneLandingArea,
    /// <summary>§Terminal.</summary>
    Terminal,
    /// <summary>§TurningBasin.</summary>
    TurningBasin,
    /// <summary>§WaterwayArea.</summary>
    WaterwayArea,
}

/// <summary>
/// Concrete S-131 standalone-metadata feature kind
/// (S-131 FC Edition 1.0.0 §B.2). One entry per feature type that
/// has no shared supertype in the FC.
/// </summary>
public enum S131MetadataKind
{
    /// <summary>Unrecognised feature code.</summary>
    Unknown,
    /// <summary>§DataCoverage.</summary>
    DataCoverage,
    /// <summary>§QualityOfNonBathymetricData.</summary>
    QualityOfNonBathymetricData,
    /// <summary>§SoundingDatum.</summary>
    SoundingDatum,
    /// <summary>§TextPlacement.</summary>
    TextPlacement,
    /// <summary>§VerticalDatumOfData.</summary>
    VerticalDatumOfData,
}

/// <summary>
/// Concrete kind of an <c>AbstractRxN</c>-derived S-131 information
/// type (S-131 FC Edition 1.0.0 §B.1).
/// </summary>
public enum S131RxNKind
{
    /// <summary>Unrecognised information-type code.</summary>
    Unknown,
    /// <summary>§NauticalInformation — descriptive nautical text.</summary>
    NauticalInformation,
    /// <summary>§Recommendations — advisory text.</summary>
    Recommendations,
    /// <summary>§Regulations — mandatory regulatory text.</summary>
    Regulations,
    /// <summary>§Restrictions — restriction-style regulatory text.</summary>
    Restrictions,
}

/// <summary>
/// Geometric primitive of an <see cref="S131Geometry"/> instance.
/// Mirrors <see cref="GmlGeometryType"/> but is re-exported here so
/// callers can stay within the S-131 typed namespace.
/// </summary>
public enum S131GeometryType
{
    /// <summary>The feature carries no geometry (information type or
    /// container feature).</summary>
    None = 0,
    /// <summary>One or more point geometries.</summary>
    Point = 1,
    /// <summary>One or more curve / line string geometries.</summary>
    Curve = 2,
    /// <summary>A single surface with optional interior rings.</summary>
    Surface = 3,
}

/// <summary>
/// Strongly-typed geometry payload of an <see cref="IS131Feature"/>.
/// Wraps the four parallel coordinate collections on
/// <see cref="S131Feature"/> into a single record so typed consumers do
/// not have to inspect <see cref="GmlGeometryType"/> separately.
/// </summary>
/// <remarks>
/// Coordinates are <c>(lat, lon)</c> in decimal degrees per
/// S-100 Part 10b §6.2.
/// </remarks>
public sealed class S131Geometry
{
    /// <summary>The geometric primitive type.</summary>
    public required S131GeometryType GeometryType { get; init; }

    /// <summary>Point coordinates (non-empty when <see cref="GeometryType"/> is <see cref="S131GeometryType.Point"/>).</summary>
    public ImmutableArray<GeoPosition> Points { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <summary>Curve coordinate sequences (one inner array per curve segment chain).</summary>
    public ImmutableArray<ImmutableArray<GeoPosition>> Curves { get; init; } =
        ImmutableArray<ImmutableArray<GeoPosition>>.Empty;

    /// <summary>Exterior ring of a surface (empty for non-surface geometries).</summary>
    public ImmutableArray<GeoPosition> ExteriorRing { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <summary>Interior rings (holes) of a surface (empty for non-surface geometries).</summary>
    public ImmutableArray<ImmutableArray<GeoPosition>> InteriorRings { get; init; } =
        ImmutableArray<ImmutableArray<GeoPosition>>.Empty;

    /// <summary>Convenience: <c>true</c> when the feature has no geometry.</summary>
    public bool IsEmpty => GeometryType == S131GeometryType.None;

    internal static readonly S131Geometry Empty = new()
    {
        GeometryType = S131GeometryType.None,
    };
}

/// <summary>
/// A typed cross-reference from an S-131 object to another object in the
/// same dataset, recovered from an <c>xlink:href</c> attribute on a
/// role-bearing child element.
/// </summary>
/// <remarks>
/// <para>
/// The role is preserved verbatim from the source GML (e.g.
/// <c>"theContactDetails"</c>, <c>"applicability"</c>). <see cref="Target"/>
/// is the resolved typed peer; it is <c>null</c> when the
/// <c>xlink:href</c> did not resolve (a <c>xlink.unresolved</c> diagnostic
/// is also emitted) or when the source feature has been projected before
/// its target was constructed (this latter case does not occur in the
/// current two-pass projection — see <see cref="S131HarbourInfrastructureDataset.From"/>).
/// </para>
/// <para>
/// <see cref="TargetRef"/> exposes the original <c>gml:id</c> of the
/// target so that consumers can record a stable identifier even when
/// resolution failed.
/// </para>
/// </remarks>
public sealed class S131ResolvedReference
{
    /// <summary>The role / association name on the source GML element.</summary>
    public required string Role { get; init; }

    /// <summary>The target <c>gml:id</c> (without leading <c>#</c>).</summary>
    public required string TargetRef { get; init; }

    /// <summary>
    /// The resolved typed peer — an <see cref="IS131Feature"/> or
    /// <see cref="IS131InformationType"/> — or <c>null</c> when the
    /// reference did not resolve.
    /// </summary>
    public object? Target { get; init; }
}
