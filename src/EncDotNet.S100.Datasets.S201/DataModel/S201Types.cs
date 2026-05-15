using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S201.DataModel;

/// <summary>
/// The geometry primitive kind of an S-201 feature when surfaced through
/// the typed projection. Mirrors the S-124 typed-model geometry shape:
/// callers get an enum discriminator plus a single
/// <see cref="ImmutableArray{T}"/> of <see cref="GeoPosition"/>s whose
/// semantics depend on the kind.
/// </summary>
public enum S201GeometryKind
{
    /// <summary>No geometry. Abstract supertypes and aggregation containers may carry this.</summary>
    None,

    /// <summary>A single point (one coordinate).</summary>
    Point,

    /// <summary>A curve — an ordered, open sequence of coordinates.</summary>
    Curve,

    /// <summary>A surface — a closed exterior ring (interior rings are accessible on <c>Source</c>).</summary>
    Surface,
}

/// <summary>
/// AIS-AtoN discriminator for <see cref="S201ElectronicAtoN"/>
/// (S-201 Edition 2.0.0 Annex C — <c>VirtualAISAidToNavigation</c>,
/// <c>PhysicalAISAidToNavigation</c>, <c>SyntheticAISAidToNavigation</c>).
/// </summary>
public enum AisAtonKind
{
    /// <summary>Forward-compatibility fallback when the feature class is not one of the three FC-declared AIS AtoN types.</summary>
    Unknown,

    /// <summary>Virtual AIS AtoN: no physical structure; the AIS message describes a notional aid (e.g. a virtual cardinal mark on a wreck).</summary>
    Virtual,

    /// <summary>Physical AIS AtoN: a real AtoN broadcasting its own AIS message.</summary>
    Physical,

    /// <summary>Synthetic AIS AtoN: AIS messages transmitted from another station on behalf of an unmonitored AtoN.</summary>
    Synthetic,
}

/// <summary>
/// Discriminator for <see cref="S201Light"/> (S-201 Edition 2.0.0
/// Annex C — concrete <c>GenericLight</c> subclasses).
/// </summary>
public enum LightKind
{
    /// <summary>Forward-compatibility fallback when the feature class is not a recognised <c>GenericLight</c> subclass.</summary>
    Unknown,

    /// <summary>A light with one or more sectors (<c>LightSectored</c>).</summary>
    Sectored,

    /// <summary>An all-round light (<c>LightAllAround</c>).</summary>
    AllAround,

    /// <summary>An air-obstruction light (<c>LightAirObstruction</c>).</summary>
    AirObstruction,

    /// <summary>A fog-detector light (<c>LightFogDetector</c>).</summary>
    FogDetector,
}

/// <summary>
/// A date range carried by AtoN lifecycle attributes such as
/// <c>fixedDateRange</c> and <c>periodicDateRange</c>
/// (S-201 Edition 2.0.0 §4 / Annex C). Either bound may be absent.
/// </summary>
public sealed record S201DateRange
{
    /// <summary>Start of the range, when present.</summary>
    public DateTimeOffset? Start { get; init; }

    /// <summary>End of the range, when present.</summary>
    public DateTimeOffset? End { get; init; }

    /// <summary>True when at least one bound is present.</summary>
    public bool HasValue => Start.HasValue || End.HasValue;
}

/// <summary>
/// The typed projection of the <c>featureName</c> complex attribute
/// (S-201 Edition 2.0.0 Annex C) — a single language-tagged name plus
/// a "display name" flag. Multiplicity in the FC is 0..* so a feature
/// may carry several of these.
/// </summary>
public sealed record S201FeatureNameRecord
{
    /// <summary>The name text.</summary>
    public string? Name { get; init; }

    /// <summary>The language code (ISO 639), when supplied.</summary>
    public string? Language { get; init; }

    /// <summary>True when the encoder flagged this as the display name.</summary>
    public bool? DisplayName { get; init; }
}

/// <summary>
/// Typed projection of the <c>AtonStatusInformation</c> information
/// type (S-201 Edition 2.0.0 Annex C2).
/// </summary>
public sealed class S201AtonStatusInformation
{
    /// <summary>The GML identifier of the source information type.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The change-type code (S-201 Edition 2.0.0 Annex C2 —
    /// <c>ChangeTypes</c> codelist, values 1–4).
    /// </summary>
    public int? ChangeTypes { get; init; }

    /// <summary>The verbatim sub-attributes of the <c>ChangeDetails</c> complex attribute, when present.</summary>
    public ImmutableDictionary<string, string> ChangeDetails { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of the <c>PositioningInformation</c> information
/// type (S-201 Edition 2.0.0 Annex C2 — "Information about how a
/// position was obtained").
/// </summary>
public sealed class S201PositioningInformationRecord
{
    /// <summary>The GML identifier of the source information type.</summary>
    public required string Id { get; init; }

    /// <summary>The positioning device (FC: <c>positioningDevice</c>), when supplied.</summary>
    public string? PositioningDevice { get; init; }

    /// <summary>The positioning method (FC: <c>positioningMethod</c>), when supplied.</summary>
    public string? PositioningMethod { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of the <c>AtoNFixingMethod</c> information type
/// (S-201 Edition 2.0.0 Annex C2).
/// </summary>
public sealed class S201AtoNFixingMethodRecord
{
    /// <summary>The GML identifier of the source information type.</summary>
    public required string Id { get; init; }

    /// <summary>The reference point used (FC: <c>referencePoint</c>), when supplied.</summary>
    public string? ReferencePoint { get; init; }

    /// <summary>The horizontal datum code (FC: <c>horizontalDatum</c>, S-100 enumeration), when supplied.</summary>
    public int? HorizontalDatum { get; init; }

    /// <summary>The source date of the fix (FC: <c>sourceDate</c>), when supplied.</summary>
    public DateTimeOffset? SourceDate { get; init; }

    /// <summary>The positioning procedure (FC: <c>positioningProcedure</c>), when supplied.</summary>
    public string? PositioningProcedure { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of the <c>SpatialQuality</c> information type
/// (S-201 Edition 2.0.0 Annex C2).
/// </summary>
public sealed class S201SpatialQuality
{
    /// <summary>The GML identifier of the source information type.</summary>
    public required string Id { get; init; }

    /// <summary>
    /// The quality-of-horizontal-measurement code (FC:
    /// <c>qualityOfHorizontalMeasurement</c>, codelist values 1–11).
    /// </summary>
    public int? QualityOfHorizontalMeasurement { get; init; }

    /// <summary>The spatial accuracy value (FC: <c>spatialAccuracy</c>), when supplied.</summary>
    public double? SpatialAccuracy { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an <c>AtonAggregation</c> feature
/// (S-201 Edition 2.0.0 Annex C). An aggregation gathers two or more
/// AtoNs that act together as a single navigational aid (e.g. a leading
/// line of two beacons). <see cref="Peers"/> holds the resolved
/// typed members; unresolved xlinks surface as
/// <c>xlink.unresolved</c> diagnostics on the projection context.
/// </summary>
public sealed class S201AtonAggregation
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>The aggregation-category code (FC: <c>CategoryOfAssociation</c>), when supplied.</summary>
    public int? CategoryOfAssociation { get; init; }

    /// <summary>The resolved peer AtoNs (order preserved from the source GML).</summary>
    public ImmutableArray<S201AtonObject> Peers { get; init; } = ImmutableArray<S201AtonObject>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of an <c>AtonAssociation</c> feature
/// (S-201 Edition 2.0.0 Annex C). An association links AtoNs that share
/// a navigational meaning without acting as a single aid.
/// </summary>
public sealed class S201AtonAssociation
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>The association-category code (FC: <c>CategoryOfAssociation</c>), when supplied.</summary>
    public int? CategoryOfAssociation { get; init; }

    /// <summary>The resolved peer AtoNs (order preserved from the source GML).</summary>
    public ImmutableArray<S201AtonObject> Peers { get; init; } = ImmutableArray<S201AtonObject>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
