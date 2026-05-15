using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S122.DataModel;

/// <summary>
/// Common base for every concrete S-122 feature projection. Carries
/// the FC-abstract <c>FeatureType</c> inherited attributes (S-122 FC
/// 2.0.0 §FeatureType) plus the shared identity / geometry / reference
/// fields required by <see cref="IS122Feature"/>.
/// </summary>
public abstract class S122FeatureBase : IS122Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public abstract string FeatureType { get; }

    /// <inheritdoc/>
    public S122GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <summary>The interoperability identifier (S-122 FC §interoperabilityIdentifier), when present.</summary>
    public string? InteroperabilityIdentifier { get; init; }

    /// <summary>The feature name (S-122 FC §featureName), when present.</summary>
    public string? FeatureName { get; init; }

    /// <summary>The minimum display scale (S-122 FC §scaleMinimum), when present.</summary>
    public int? ScaleMinimum { get; init; }

    /// <summary>The graphic reference (S-122 FC §graphic), when present.</summary>
    public string? Graphic { get; init; }

    /// <summary>The source indication (S-122 FC §sourceIndication), when present.</summary>
    public string? SourceIndication { get; init; }

    /// <summary>The text content payload (S-122 FC §textContent), when present.</summary>
    public string? TextContent { get; init; }

    /// <summary>The fixed date range (S-122 FC §fixedDateRange), when present (raw text).</summary>
    public string? FixedDateRange { get; init; }

    /// <summary>The periodic date range (S-122 FC §periodicDateRange), when present (raw text).</summary>
    public string? PeriodicDateRange { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GmlReference> References { get; init; } = ImmutableArray<GmlReference>.Empty;

    /// <inheritdoc/>
    public ImmutableArray<S122InformationReference> InformationReferences { get; internal set; } =
        ImmutableArray<S122InformationReference>.Empty;

    /// <inheritdoc/>
    public ImmutableArray<S122FeatureReference> FeatureReferences { get; internal set; } =
        ImmutableArray<S122FeatureReference>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of the S-122 <c>MarineProtectedArea</c> feature
/// (S-122 FC 2.0.0 §MarineProtectedArea): a zoned area carrying
/// protection / restriction categorisation and designation metadata.
/// </summary>
public sealed class S122MarineProtectedArea : S122FeatureBase
{
    /// <inheritdoc/>
    public override string FeatureType => "MarineProtectedArea";

    /// <summary>The category-of-marine-protected-area code (S-122 FC §categoryOfMarineProtectedArea), when present.</summary>
    public int? CategoryOfMarineProtectedArea { get; init; }

    /// <summary>The category-of-restricted-area code (S-122 FC §categoryOfRestrictedArea), when present.</summary>
    public int? CategoryOfRestrictedArea { get; init; }

    /// <summary>The jurisdiction (S-122 FC §jurisdiction), when present.</summary>
    public string? Jurisdiction { get; init; }

    /// <summary>The restriction code (S-122 FC §restriction), when present.</summary>
    public int? Restriction { get; init; }

    /// <summary>The status code (S-122 FC §status), when present.</summary>
    public int? Status { get; init; }

    /// <summary>The designation (S-122 FC §designation), when present.</summary>
    public string? Designation { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>RestrictedArea</c> feature (S-122
/// FC 2.0.0 §RestrictedArea).
/// </summary>
public sealed class S122RestrictedArea : S122FeatureBase
{
    /// <inheritdoc/>
    public override string FeatureType => "RestrictedArea";

    /// <summary>The category-of-restricted-area code (S-122 FC §categoryOfRestrictedArea), when present.</summary>
    public int? CategoryOfRestrictedArea { get; init; }

    /// <summary>The restriction code (S-122 FC §restriction), when present.</summary>
    public int? Restriction { get; init; }

    /// <summary>The status code (S-122 FC §status), when present.</summary>
    public int? Status { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>VesselTrafficServiceArea</c>
/// feature (S-122 FC 2.0.0 §VesselTrafficServiceArea).
/// </summary>
public sealed class S122VesselTrafficServiceArea : S122FeatureBase
{
    /// <inheritdoc/>
    public override string FeatureType => "VesselTrafficServiceArea";
}

/// <summary>
/// Typed projection of the S-122 <c>InformationArea</c> feature (S-122
/// FC 2.0.0 §InformationArea).
/// </summary>
public sealed class S122InformationArea : S122FeatureBase
{
    /// <inheritdoc/>
    public override string FeatureType => "InformationArea";

    /// <summary>The category-of-relationship code (S-122 FC §categoryOfRelationship), when present.</summary>
    public int? CategoryOfRelationship { get; init; }

    /// <summary>The action-or-activity descriptor (S-122 FC §actionOrActivity), when present.</summary>
    public string? ActionOrActivity { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>DataCoverage</c> feature (S-122 FC
/// 2.0.0 §DataCoverage): dataset-extent / scale-band metadata polygon.
/// </summary>
public sealed class S122DataCoverage : S122FeatureBase
{
    /// <inheritdoc/>
    public override string FeatureType => "DataCoverage";

    /// <summary>The maximum display scale (S-122 FC §maximumDisplayScale), when present.</summary>
    public int? MaximumDisplayScale { get; init; }

    /// <summary>The minimum display scale (S-122 FC §minimumDisplayScale), when present.</summary>
    public int? MinimumDisplayScale { get; init; }

    /// <summary>The optimum display scale (S-122 FC §optimumDisplayScale), when present.</summary>
    public int? OptimumDisplayScale { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>QualityOfNonBathymetricData</c>
/// feature (S-122 FC 2.0.0 §QualityOfNonBathymetricData).
/// </summary>
public sealed class S122QualityOfNonBathymetricData : S122FeatureBase
{
    /// <inheritdoc/>
    public override string FeatureType => "QualityOfNonBathymetricData";

    /// <summary>The category-of-temporal-variation code (S-122 FC §categoryOfTemporalVariation), when present.</summary>
    public int? CategoryOfTemporalVariation { get; init; }

    /// <summary>The horizontal distance uncertainty (S-122 FC §horizontalDistanceUncertainty), when present.</summary>
    public double? HorizontalDistanceUncertainty { get; init; }

    /// <summary>The horizontal position uncertainty (S-122 FC §horizontalPositionUncertainty), when present.</summary>
    public double? HorizontalPositionUncertainty { get; init; }

    /// <summary>The orientation uncertainty (S-122 FC §orientationUncertainty), when present.</summary>
    public double? OrientationUncertainty { get; init; }

    /// <summary>The survey date range (S-122 FC §surveyDateRange), when present (raw text).</summary>
    public string? SurveyDateRange { get; init; }

    /// <summary>The information text (S-122 FC §information), when present.</summary>
    public string? Information { get; init; }
}

/// <summary>
/// Typed projection of the S-122 <c>TextPlacement</c> feature (S-122
/// FC 2.0.0 §TextPlacement): a cartographic-text positioning anchor.
/// </summary>
public sealed class S122TextPlacement : S122FeatureBase
{
    /// <inheritdoc/>
    public override string FeatureType => "TextPlacement";

    /// <summary>The text offset bearing in degrees (S-122 FC §textOffsetBearing), when present.</summary>
    public double? TextOffsetBearing { get; init; }

    /// <summary>The text offset distance (S-122 FC §textOffsetDistance), when present.</summary>
    public double? TextOffsetDistance { get; init; }

    /// <summary>The text rotation in degrees (S-122 FC §textRotation), when present.</summary>
    public double? TextRotation { get; init; }

    /// <summary>The text type code (S-122 FC §textType), when present.</summary>
    public int? TextType { get; init; }
}

/// <summary>
/// Catch-all typed projection for any future S-122 feature class not
/// individually broken out above. All source attributes survive on
/// <see cref="S122FeatureBase.ExtraAttributes"/>.
/// </summary>
public sealed class S122OtherFeature : S122FeatureBase
{
    private readonly string _featureType;

    /// <summary>Creates an <see cref="S122OtherFeature"/> tagged with the supplied feature-type code.</summary>
    /// <param name="featureType">The S-122 feature type code (GML element local name).</param>
    public S122OtherFeature(string featureType) => _featureType = featureType;

    /// <inheritdoc/>
    public override string FeatureType => _featureType;
}
