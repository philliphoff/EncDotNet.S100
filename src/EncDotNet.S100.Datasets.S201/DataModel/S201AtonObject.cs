using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S201.DataModel;

/// <summary>
/// Abstract base for every Aid to Navigation object surfaced by the
/// S-201 typed projection — mirrors the FC <c>AidsToNavigation</c>
/// abstract supertype (S-201 Edition 2.0.0 Annex C).
/// </summary>
/// <remarks>
/// <para>
/// Concrete FC leaf identity is preserved on <see cref="FeatureClass"/>
/// rather than producing one C# class per FC code. Callers that need
/// to switch on the concrete type should compare
/// <see cref="FeatureClass"/> to the FC code (e.g. <c>"LateralBuoy"</c>,
/// <c>"Lighthouse"</c>, <c>"PowerSource"</c>).
/// </para>
/// <para>
/// Common AtoN attributes (identifier, lifecycle dates,
/// inspection requirements, source metadata) are typed at this level;
/// subclass-specific attributes appear on
/// <see cref="S201StructureObject"/>, <see cref="S201Equipment"/>,
/// <see cref="S201Light"/>, and <see cref="S201ElectronicAtoN"/>.
/// </para>
/// </remarks>
public abstract class S201AtonObject
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The concrete FC feature class code (e.g. <c>"LateralBuoy"</c>,
    /// <c>"Lighthouse"</c>, <c>"VirtualAISAidToNavigation"</c>).
    /// </summary>
    public required string FeatureClass { get; init; }

    /// <summary>The originating feature record from the feature-bag dataset.</summary>
    public required S201Feature Source { get; init; }

    /// <summary>The geometry primitive kind of <see cref="Coordinates"/>.</summary>
    public S201GeometryKind GeometryKind { get; init; }

    /// <summary>
    /// Flat coordinate list whose semantics depend on
    /// <see cref="GeometryKind"/>. Multi-curve or multi-ring source
    /// geometry is flattened in source order — full structure is
    /// available on <see cref="Source"/>.
    /// </summary>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <summary>The unique AtoN identifier (FC: <c>iDCode</c>), when supplied.</summary>
    public string? IdCode { get; init; }

    /// <summary>Free-form information text (FC: <c>information</c>), when supplied.</summary>
    public string? Information { get; init; }

    /// <summary>Typed feature names (FC: <c>featureName</c>, multiplicity 0..*).</summary>
    public ImmutableArray<S201FeatureNameRecord> FeatureNames { get; init; } =
        ImmutableArray<S201FeatureNameRecord>.Empty;

    /// <summary>Minimum display scale (FC: <c>scaleMinimum</c>), when supplied.</summary>
    public int? ScaleMinimum { get; init; }

    /// <summary>Source date (FC: <c>sourceDate</c>), when supplied.</summary>
    public DateTimeOffset? SourceDate { get; init; }

    /// <summary>Source description (FC: <c>source</c>), when supplied.</summary>
    public string? SourceText { get; init; }

    /// <summary>Pictorial representation reference (FC: <c>pictorialRepresentation</c>), when supplied.</summary>
    public string? PictorialRepresentation { get; init; }

    /// <summary>Inspection frequency (FC: <c>inspectionFrequency</c>), when supplied.</summary>
    public string? InspectionFrequency { get; init; }

    /// <summary>Inspection requirements (FC: <c>inspectionRequirements</c>), when supplied.</summary>
    public string? InspectionRequirements { get; init; }

    /// <summary>AtoN maintenance record reference (FC: <c>aToNMaintenanceRecord</c>), when supplied.</summary>
    public string? AtoNMaintenanceRecord { get; init; }

    /// <summary>Date the aid was installed (FC: <c>installationDate</c>), when supplied.</summary>
    public DateTimeOffset? InstallationDate { get; init; }

    /// <summary>Validity range for a fixed (non-recurring) deployment (FC: <c>fixedDateRange</c>).</summary>
    public S201DateRange? FixedDateRange { get; init; }

    /// <summary>Validity range for a periodic deployment (FC: <c>periodicDateRange</c>).</summary>
    public S201DateRange? PeriodicDateRange { get; init; }

    /// <summary>Seasonal action codes (FC: <c>SeasonalActionRequired</c>, multiplicity 0..*).</summary>
    public ImmutableArray<string> SeasonalActionRequired { get; init; } = ImmutableArray<string>.Empty;

    /// <summary>
    /// AtoN status information resolved through the
    /// <c>AtoNStatus</c> information binding (S-201 Edition 2.0.0
    /// Annex C — <c>Atonstatus</c> association). Multiple bindings
    /// project as a timeline of status records.
    /// </summary>
    public ImmutableArray<S201AtonStatusInformation> StatusInformation { get; init; } =
        ImmutableArray<S201AtonStatusInformation>.Empty;

    /// <summary>Aggregations this AtoN participates in (back-resolved).</summary>
    public ImmutableArray<S201AtonAggregation> Aggregations { get; internal set; } =
        ImmutableArray<S201AtonAggregation>.Empty;

    /// <summary>Associations this AtoN participates in (back-resolved).</summary>
    public ImmutableArray<S201AtonAssociation> Associations { get; internal set; } =
        ImmutableArray<S201AtonAssociation>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>StructureObject</c> (S-201 Edition 2.0.0
/// Annex C) — beacons, buoys, landmarks, lighthouses, light vessels,
/// offshore platforms, piles, buildings, bridges, etc.
/// </summary>
/// <remarks>
/// Structures act as the physical host for <see cref="S201Equipment"/>
/// instances. The two-way back-reference (<see cref="MountedEquipment"/>
/// here, <see cref="S201Equipment.HostStructure"/> there) is resolved
/// from <c>theParentFeature</c> / <c>parent</c> xlinks emitted by the
/// FC <c>StructureEquipment</c> association.
/// </remarks>
public class S201StructureObject : S201AtonObject
{
    /// <summary>The AtoN number (FC: <c>AtoNNumber</c>), when supplied.</summary>
    public string? AtoNNumber { get; init; }

    /// <summary>Aid availability category code (FC: <c>aidAvailabilityCategory</c>, codelist 1–3).</summary>
    public int? AidAvailabilityCategory { get; init; }

    /// <summary>Condition code (FC: <c>condition</c>, codelist 1–5).</summary>
    public int? Condition { get; init; }

    /// <summary>Contact address (FC: <c>contactAddress</c>), when supplied.</summary>
    public string? ContactAddress { get; init; }

    /// <summary>Equipment mounted on this structure (resolved from <c>StructureEquipment</c> xlinks).</summary>
    public ImmutableArray<S201Equipment> MountedEquipment { get; internal set; } =
        ImmutableArray<S201Equipment>.Empty;

    /// <summary>Positioning information records resolved through the <c>AtonPositioningInformationAssociation</c> binding.</summary>
    public ImmutableArray<S201PositioningInformationRecord> PositioningInformation { get; init; } =
        ImmutableArray<S201PositioningInformationRecord>.Empty;

    /// <summary>Fixing method records resolved through the <c>AtonFixingMethodAssociation</c> binding.</summary>
    public ImmutableArray<S201AtoNFixingMethodRecord> FixingMethods { get; init; } =
        ImmutableArray<S201AtoNFixingMethodRecord>.Empty;
}

/// <summary>
/// Typed projection of an <c>Equipment</c> (S-201 Edition 2.0.0
/// Annex C) — daymarks, fog signals, radar reflectors, racons,
/// retroreflectors, environment-observation gear, power sources,
/// radio stations.
/// </summary>
public class S201Equipment : S201AtonObject
{
    /// <summary>Remote monitoring system reference (FC: <c>remoteMonitoringSystem</c>), when supplied.</summary>
    public string? RemoteMonitoringSystem { get; init; }

    /// <summary>
    /// The host structure this equipment is mounted on, resolved from
    /// <c>theParentFeature</c> / <c>parent</c> xlinks emitted by the
    /// FC <c>StructureEquipment</c> association. <c>null</c> for
    /// orphaned equipment, free-standing equipment, or when the xlink
    /// could not be resolved (the latter raises an
    /// <c>xlink.unresolved</c> diagnostic).
    /// </summary>
    public S201StructureObject? HostStructure { get; internal set; }
}

/// <summary>
/// Typed projection of a <c>GenericLight</c> concrete subclass
/// (S-201 Edition 2.0.0 Annex C — <c>LightSectored</c>,
/// <c>LightAllAround</c>, <c>LightAirObstruction</c>, or
/// <c>LightFogDetector</c>).
/// </summary>
/// <remarks>
/// The four FC subclasses share an identical attribute schema, so the
/// typed model collapses them into a single class discriminated by
/// <see cref="Kind"/>. Use <see cref="S201AtonObject.FeatureClass"/>
/// to get the original FC code if needed.
/// </remarks>
public sealed class S201Light : S201Equipment
{
    /// <summary>The light kind discriminator (derived from <see cref="S201AtonObject.FeatureClass"/>).</summary>
    public required LightKind Kind { get; init; }

    /// <summary>Height of the light (FC: <c>height</c>, in metres), when supplied.</summary>
    public double? Height { get; init; }

    /// <summary>Status codes (FC: <c>status</c>, multiplicity 0..*).</summary>
    public ImmutableArray<int> Status { get; init; } = ImmutableArray<int>.Empty;

    /// <summary>Vertical datum code (FC: <c>verticalDatum</c>, codelist), when supplied.</summary>
    public int? VerticalDatum { get; init; }

    /// <summary>Vertical length (FC: <c>verticalLength</c>, in metres), when supplied.</summary>
    public double? VerticalLength { get; init; }

    /// <summary>Effective intensity (FC: <c>effectiveIntensity</c>, candela), when supplied.</summary>
    public double? EffectiveIntensity { get; init; }

    /// <summary>Peak intensity (FC: <c>peakIntensity</c>, candela), when supplied.</summary>
    public double? PeakIntensity { get; init; }
}

/// <summary>
/// Typed projection of an <c>ElectronicAton</c> concrete subclass
/// (S-201 Edition 2.0.0 Annex C — <c>VirtualAISAidToNavigation</c>,
/// <c>PhysicalAISAidToNavigation</c>, <c>SyntheticAISAidToNavigation</c>).
/// </summary>
/// <remarks>
/// The three FC subclasses share an identical attribute schema, so the
/// typed model collapses them into a single class discriminated by
/// <see cref="Kind"/>. Physical and Synthetic variants may carry a
/// <see cref="HostStructure"/>; virtual AIS AtoNs typically do not.
/// The typed model does not enforce this convention beyond resolving
/// the xlink when present.
/// </remarks>
public sealed class S201ElectronicAtoN : S201AtonObject
{
    /// <summary>The AIS AtoN kind discriminator (derived from <see cref="S201AtonObject.FeatureClass"/>).</summary>
    public required AisAtonKind Kind { get; init; }

    /// <summary>The AtoN number (FC: <c>AtoNNumber</c>), when supplied.</summary>
    public string? AtoNNumber { get; init; }

    /// <summary>The MMSI code (FC: <c>mMSICode</c>) — required by the FC for Physical/Synthetic AIS-AtoNs.</summary>
    public string? MmsiCode { get; init; }

    /// <summary>AIS operational status codes (FC: <c>status</c>, multiplicity 0..*).</summary>
    public ImmutableArray<int> Status { get; init; } = ImmutableArray<int>.Empty;

    /// <summary>
    /// The host structure this AIS AtoN is bound to, when supplied by
    /// the encoder (typically only for Physical / Synthetic variants).
    /// </summary>
    public S201StructureObject? HostStructure { get; internal set; }
}

/// <summary>
/// A concrete fallback for AtoN features the typed model does not have
/// a dedicated subclass for (e.g. <c>NavigationLine</c>,
/// <c>RecommendedTrack</c>, <c>DataCoverage</c>,
/// <c>DangerousFeature</c>, dataset-metadata features). Carries the
/// common <see cref="S201AtonObject"/> attributes plus geometry and
/// extras; callers needing specific attributes should read them from
/// <see cref="S201AtonObject.Source"/> or
/// <see cref="S201AtonObject.ExtraAttributes"/>.
/// </summary>
public sealed class S201GenericAtonObject : S201AtonObject
{
}
