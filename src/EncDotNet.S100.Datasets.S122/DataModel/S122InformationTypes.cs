using System.Collections.Immutable;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S122.DataModel;

/// <summary>
/// Common base for every concrete S-122 information-type projection.
/// Carries the FC-abstract <c>InformationType</c> inherited attributes
/// (S-122 FC 2.0.0 §InformationType) plus the shared identity /
/// reference fields required by <see cref="IS122InformationType"/>.
/// </summary>
public abstract class S122InformationTypeBase : IS122InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public abstract string TypeCode { get; }

    /// <summary>The feature name (S-122 FC §featureName), when present.</summary>
    public string? FeatureName { get; init; }

    /// <summary>The fixed date range (S-122 FC §fixedDateRange), when present (raw text).</summary>
    public string? FixedDateRange { get; init; }

    /// <summary>The periodic date range (S-122 FC §periodicDateRange), when present (raw text).</summary>
    public string? PeriodicDateRange { get; init; }

    /// <summary>The graphic reference (S-122 FC §graphic), when present.</summary>
    public string? Graphic { get; init; }

    /// <summary>The source indication (S-122 FC §sourceIndication), when present.</summary>
    public string? SourceIndication { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GmlReference> References { get; init; } = ImmutableArray<GmlReference>.Empty;

    /// <inheritdoc/>
    public ImmutableArray<S122InformationReference> InformationReferences { get; internal set; } =
        ImmutableArray<S122InformationReference>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of the S-122 <c>Authority</c> information type
/// (S-122 FC 2.0.0 §Authority): the protected-area-managing /
/// regulatory body for the related feature.
/// </summary>
public sealed class S122Authority : S122InformationTypeBase
{
    /// <inheritdoc/>
    public override string TypeCode => "Authority";

    /// <summary>The category-of-authority code (S-122 FC §categoryOfAuthority), when present.</summary>
    public int? CategoryOfAuthority { get; init; }

    /// <summary>The text content payload (S-122 FC §textContent), when present.</summary>
    public string? TextContent { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>ContactDetails</c> information
/// type (S-122 FC 2.0.0 §ContactDetails).
/// </summary>
public sealed class S122ContactDetails : S122InformationTypeBase
{
    /// <inheritdoc/>
    public override string TypeCode => "ContactDetails";

    /// <summary>The call name (S-122 FC §callName), when present.</summary>
    public string? CallName { get; init; }

    /// <summary>The radio call sign (S-122 FC §callSign), when present.</summary>
    public string? CallSign { get; init; }

    /// <summary>The category-of-communication-preference code (S-122 FC §categoryOfCommunicationPreference), when present.</summary>
    public int? CategoryOfCommunicationPreference { get; init; }

    /// <summary>The communication channel (S-122 FC §communicationChannel), when present.</summary>
    public string? CommunicationChannel { get; init; }

    /// <summary>The contact instructions (S-122 FC §contactInstructions), when present.</summary>
    public string? ContactInstructions { get; init; }

    /// <summary>The contact language (S-122 FC §language), when present.</summary>
    public string? Language { get; init; }

    /// <summary>The MMSI code (S-122 FC §mMSICode), when present.</summary>
    public string? MMSICode { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>Applicability</c> information type
/// (S-122 FC 2.0.0 §Applicability): a vessel / cargo / route /
/// performance filter that scopes a restriction or recommendation.
/// </summary>
public sealed class S122Applicability : S122InformationTypeBase
{
    /// <inheritdoc/>
    public override string TypeCode => "Applicability";

    /// <summary>The in-ballast flag (S-122 FC §inBallast), when present.</summary>
    public bool? InBallast { get; init; }

    /// <summary>The category-of-cargo code (S-122 FC §categoryOfCargo), when present.</summary>
    public int? CategoryOfCargo { get; init; }

    /// <summary>The category-of-dangerous-or-hazardous-cargo code (S-122 FC §categoryOfDangerousOrHazardousCargo), when present.</summary>
    public int? CategoryOfDangerousOrHazardousCargo { get; init; }

    /// <summary>The category-of-vessel code (S-122 FC §categoryOfVessel), when present.</summary>
    public int? CategoryOfVessel { get; init; }

    /// <summary>The category-of-vessel-registry code (S-122 FC §categoryOfVesselRegistry), when present.</summary>
    public int? CategoryOfVesselRegistry { get; init; }

    /// <summary>The logical connective (S-122 FC §logicalConnectives), when present.</summary>
    public string? LogicalConnectives { get; init; }

    /// <summary>The thickness-of-ice capability (S-122 FC §thicknessOfIceCapability), when present.</summary>
    public double? ThicknessOfIceCapability { get; init; }

    /// <summary>The vessel performance (S-122 FC §vesselPerformance), when present.</summary>
    public string? VesselPerformance { get; init; }

    /// <summary>The destination (S-122 FC §destination), when present.</summary>
    public string? Destination { get; init; }

    /// <summary>The information text (S-122 FC §information), when present.</summary>
    public string? Information { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>NauticalInformation</c> information
/// type (S-122 FC 2.0.0 §NauticalInformation): inherits all attributes
/// from the FC-abstract base.
/// </summary>
public sealed class S122NauticalInformation : S122InformationTypeBase
{
    /// <inheritdoc/>
    public override string TypeCode => "NauticalInformation";
}

/// <summary>
/// Typed projection of the S-122 <c>NonStandardWorkingDay</c>
/// information type (S-122 FC 2.0.0 §NonStandardWorkingDay).
/// </summary>
public sealed class S122NonStandardWorkingDay : S122InformationTypeBase
{
    /// <inheritdoc/>
    public override string TypeCode => "NonStandardWorkingDay";

    /// <summary>The fixed date (S-122 FC §dateFixed), when present (raw text).</summary>
    public string? DateFixed { get; init; }

    /// <summary>The variable date (S-122 FC §dateVariable), when present.</summary>
    public string? DateVariable { get; init; }

    /// <summary>The information text (S-122 FC §information), when present.</summary>
    public string? Information { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>Recommendations</c> information
/// type (S-122 FC 2.0.0 §Recommendations).
/// </summary>
public sealed class S122Recommendations : S122AbstractRxN
{
    /// <inheritdoc/>
    public override string TypeCode => "Recommendations";
}

/// <summary>
/// Typed projection of the S-122 <c>Regulations</c> information type
/// (S-122 FC 2.0.0 §Regulations).
/// </summary>
public sealed class S122Regulations : S122AbstractRxN
{
    /// <inheritdoc/>
    public override string TypeCode => "Regulations";
}

/// <summary>
/// Typed projection of the S-122 <c>Restrictions</c> information type
/// (S-122 FC 2.0.0 §Restrictions).
/// </summary>
public sealed class S122Restrictions : S122AbstractRxN
{
    /// <inheritdoc/>
    public override string TypeCode => "Restrictions";
}

/// <summary>
/// Typed projection of the S-122 <c>ServiceHours</c> information type
/// (S-122 FC 2.0.0 §ServiceHours).
/// </summary>
public sealed class S122ServiceHours : S122InformationTypeBase
{
    /// <inheritdoc/>
    public override string TypeCode => "ServiceHours";

    /// <summary>The information text (S-122 FC §information), when present.</summary>
    public string? Information { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>SpatialQuality</c> information
/// type (S-122 FC 2.0.0 §SpatialQuality).
/// </summary>
public sealed class S122SpatialQuality : S122InformationTypeBase
{
    /// <inheritdoc/>
    public override string TypeCode => "SpatialQuality";

    /// <summary>The quality-of-horizontal-measurement code (S-122 FC §qualityOfHorizontalMeasurement), when present.</summary>
    public int? QualityOfHorizontalMeasurement { get; init; }

    /// <summary>The spatial accuracy (S-122 FC §spatialAccuracy), when present.</summary>
    public double? SpatialAccuracy { get; init; }
}

/// <summary>
/// Catch-all typed projection for any future S-122 information-type
/// class not individually broken out above. All source attributes
/// survive on <see cref="S122InformationTypeBase.ExtraAttributes"/>.
/// </summary>
public sealed class S122OtherInformationType : S122InformationTypeBase
{
    private readonly string _typeCode;

    /// <summary>Creates an <see cref="S122OtherInformationType"/> tagged with the supplied type code.</summary>
    /// <param name="typeCode">The S-122 information-type code (GML element local name).</param>
    public S122OtherInformationType(string typeCode) => _typeCode = typeCode;

    /// <inheritdoc/>
    public override string TypeCode => _typeCode;
}
