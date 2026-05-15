using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S125.DataModel;

/// <summary>
/// Common contract exposed by every typed S-125 aid to navigation
/// (buoys, beacons, lights, AIS aids, structures, equipment). Allows
/// callers to enumerate <see cref="S125AtonDataset.Aids"/> uniformly and
/// dispatch on <see cref="FeatureType"/> when the discriminator matters.
/// </summary>
/// <remarks>
/// S-125 Edition 1.0.0 declares all concrete AtoN feature types as
/// point primitives (§AidsToNavigation <c>permittedPrimitives=point</c>).
/// Callers may therefore treat <see cref="Position"/> as the canonical
/// location of the aid.
/// </remarks>
public interface IS125Aid
{
    /// <summary>The GML identifier of the source feature.</summary>
    string Id { get; }

    /// <summary>The raw S-125 feature type code (e.g. <c>"LateralBuoy"</c>).</summary>
    string FeatureType { get; }

    /// <summary>The position of the aid, when supplied.</summary>
    GeoPosition? Position { get; }

    /// <summary>The resolved AtoN status payload, when the source feature carries an <c>AtoNStatus</c> binding.</summary>
    S125AtonStatusInformation? Status { get; }

    /// <summary>
    /// The host structure feature this aid sits on, when resolved from a
    /// feature-to-feature xlink (typically the <c>parent</c> role of the
    /// <c>StructureEquipment</c> association — S-125 Edition 1.0.0
    /// §StructureEquipment).
    /// </summary>
    IS125Aid? HostStructure { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    ImmutableDictionary<string, string> ExtraAttributes { get; }
}

/// <summary>The flavour of an S-125 buoy.</summary>
public enum S125BuoyKind
{
    /// <summary>Unrecognised buoy type.</summary>
    Unknown,
    /// <summary>§LateralBuoy.</summary>
    Lateral,
    /// <summary>§CardinalBuoy.</summary>
    Cardinal,
    /// <summary>§IsolatedDangerBuoy.</summary>
    IsolatedDanger,
    /// <summary>§SafeWaterBuoy.</summary>
    SafeWater,
    /// <summary>§SpecialPurposeGeneralBuoy.</summary>
    SpecialPurposeGeneral,
    /// <summary>§EmergencyWreckMarkingBuoy.</summary>
    EmergencyWreckMarking,
    /// <summary>§MooringBuoy.</summary>
    Mooring,
    /// <summary>§InstallationBuoy.</summary>
    Installation,
}

/// <summary>The flavour of an S-125 beacon.</summary>
public enum S125BeaconKind
{
    /// <summary>Unrecognised beacon type.</summary>
    Unknown,
    /// <summary>§LateralBeacon.</summary>
    Lateral,
    /// <summary>§CardinalBeacon.</summary>
    Cardinal,
    /// <summary>§IsolatedDangerBeacon.</summary>
    IsolatedDanger,
    /// <summary>§SafeWaterBeacon.</summary>
    SafeWater,
    /// <summary>§SpecialPurposeGeneralBeacon.</summary>
    SpecialPurposeGeneral,
}

/// <summary>The flavour of an S-125 light.</summary>
public enum S125LightKind
{
    /// <summary>Unrecognised light type.</summary>
    Unknown,
    /// <summary>§LightAllAround.</summary>
    AllAround,
    /// <summary>§LightSectored.</summary>
    Sectored,
    /// <summary>§LightAirObstruction.</summary>
    AirObstruction,
    /// <summary>§LightFloat — a floating, anchored light platform.</summary>
    Float,
    /// <summary>§LightVessel — a moored vessel acting as a light.</summary>
    Vessel,
    /// <summary>§LightFogDetector.</summary>
    FogDetector,
}

/// <summary>The flavour of an S-125 AIS aid to navigation.</summary>
public enum S125AisKind
{
    /// <summary>Unrecognised AIS aid type.</summary>
    Unknown,
    /// <summary>§PhysicalAISAidToNavigation — a real AtoN that broadcasts AIS.</summary>
    Physical,
    /// <summary>§SyntheticAISAidToNavigation — a real AtoN whose AIS signal is generated remotely.</summary>
    Synthetic,
    /// <summary>§VirtualAISAidToNavigation — an AIS signal with no physical AtoN behind it.</summary>
    Virtual,
}

/// <summary>The flavour of an S-125 structure feature.</summary>
public enum S125StructureKind
{
    /// <summary>Unrecognised structure type.</summary>
    Unknown,
    /// <summary>§Landmark.</summary>
    Landmark,
    /// <summary>§Daymark.</summary>
    Daymark,
    /// <summary>§OffshorePlatform.</summary>
    OffshorePlatform,
    /// <summary>§Pile.</summary>
    Pile,
    /// <summary>§SiloTank.</summary>
    SiloTank,
    /// <summary>§WindTurbine.</summary>
    WindTurbine,
    /// <summary>§Topmark.</summary>
    Topmark,
}

/// <summary>The flavour of an S-125 equipment feature.</summary>
public enum S125EquipmentKind
{
    /// <summary>Unrecognised equipment type.</summary>
    Unknown,
    /// <summary>§FogSignal.</summary>
    FogSignal,
    /// <summary>§RadarReflector.</summary>
    RadarReflector,
    /// <summary>§Retroreflector.</summary>
    Retroreflector,
    /// <summary>§RadarTransponderBeacon — a Racon.</summary>
    RadarTransponderBeacon,
    /// <summary>§RadioStation.</summary>
    RadioStation,
}

/// <summary>
/// Typed projection of an S-125 buoy feature
/// (S-125 Edition 1.0.0, all <c>*Buoy</c> feature classes).
/// </summary>
public sealed class S125Buoy : IS125Aid
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <summary>The buoy flavour, decoded from <see cref="FeatureType"/>.</summary>
    public S125BuoyKind Kind { get; init; }
    /// <inheritdoc/>
    public GeoPosition? Position { get; init; }
    /// <inheritdoc/>
    public S125AtonStatusInformation? Status { get; init; }
    /// <inheritdoc/>
    public IS125Aid? HostStructure { get; init; }
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-125 beacon feature
/// (S-125 Edition 1.0.0, all <c>*Beacon</c> feature classes).
/// </summary>
public sealed class S125Beacon : IS125Aid
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <summary>The beacon flavour, decoded from <see cref="FeatureType"/>.</summary>
    public S125BeaconKind Kind { get; init; }
    /// <inheritdoc/>
    public GeoPosition? Position { get; init; }
    /// <inheritdoc/>
    public S125AtonStatusInformation? Status { get; init; }
    /// <inheritdoc/>
    public IS125Aid? HostStructure { get; init; }
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-125 light feature
/// (S-125 Edition 1.0.0, all <c>Light*</c> feature classes).
/// </summary>
public sealed class S125Light : IS125Aid
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <summary>The light flavour, decoded from <see cref="FeatureType"/>.</summary>
    public S125LightKind Kind { get; init; }
    /// <inheritdoc/>
    public GeoPosition? Position { get; init; }
    /// <inheritdoc/>
    public S125AtonStatusInformation? Status { get; init; }
    /// <inheritdoc/>
    public IS125Aid? HostStructure { get; init; }
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-125 AIS aid to navigation
/// (S-125 Edition 1.0.0 §PhysicalAISAidToNavigation,
/// §SyntheticAISAidToNavigation, §VirtualAISAidToNavigation).
/// </summary>
public sealed class S125AisAton : IS125Aid
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <summary>The AIS-aid flavour, decoded from <see cref="FeatureType"/>.</summary>
    public S125AisKind Kind { get; init; }

    /// <summary>
    /// <c>true</c> when <see cref="Kind"/> is <see cref="S125AisKind.Virtual"/>.
    /// Virtual AIS aids have no physical presence — the broadcast position
    /// is purely synthetic. Distinguishing them is the most common
    /// predicate placed on AIS aids.
    /// </summary>
    public bool IsVirtual => Kind == S125AisKind.Virtual;

    /// <inheritdoc/>
    public GeoPosition? Position { get; init; }
    /// <inheritdoc/>
    public S125AtonStatusInformation? Status { get; init; }
    /// <inheritdoc/>
    public IS125Aid? HostStructure { get; init; }
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-125 structure feature (a fixed object that may
/// host equipment) — S-125 Edition 1.0.0 §Landmark, §Daymark,
/// §OffshorePlatform, §Pile, §SiloTank, §WindTurbine, §Topmark.
/// </summary>
public sealed class S125Structure : IS125Aid
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <summary>The structure flavour, decoded from <see cref="FeatureType"/>.</summary>
    public S125StructureKind Kind { get; init; }
    /// <inheritdoc/>
    public GeoPosition? Position { get; init; }
    /// <inheritdoc/>
    public S125AtonStatusInformation? Status { get; init; }
    /// <inheritdoc/>
    public IS125Aid? HostStructure { get; init; }
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an S-125 equipment feature (a piece of AtoN
/// equipment typically hosted by a <see cref="S125Structure"/>) —
/// S-125 Edition 1.0.0 §FogSignal, §RadarReflector, §Retroreflector,
/// §RadarTransponderBeacon, §RadioStation.
/// </summary>
public sealed class S125Equipment : IS125Aid
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <summary>The equipment flavour, decoded from <see cref="FeatureType"/>.</summary>
    public S125EquipmentKind Kind { get; init; }
    /// <inheritdoc/>
    public GeoPosition? Position { get; init; }
    /// <inheritdoc/>
    public S125AtonStatusInformation? Status { get; init; }
    /// <inheritdoc/>
    public IS125Aid? HostStructure { get; init; }
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
