using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S411.DataModel;

/// <summary>
/// Abstract base class for every typed S-411 ice feature
/// (S-411 Edition 1.2.1 Annex A — Feature Catalogue).
/// </summary>
/// <remarks>
/// Subclasses surface the attributes that the typed model knows how to
/// interpret; everything else round-trips through
/// <see cref="ExtraAttributes"/>.
/// </remarks>
public abstract class S411IceFeature : IS411IceFeature
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public required string NormalizedFeatureType { get; init; }

    /// <inheritdoc/>
    public required string SourceFeatureType { get; init; }

    /// <inheritdoc/>
    public S411GeometryKind GeometryKind { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;

    /// <inheritdoc/>
    public required S411Feature Source { get; init; }
}

/// <summary>
/// Typed projection of an S-411 <c>SeaIce</c> feature
/// (JCOMM <c>seaice</c>) — S-411 Edition 1.2.1 Annex A.
/// </summary>
/// <remarks>
/// Surface feature carrying the WMO egg code (concentration, stages of
/// development, forms of ice).
/// </remarks>
public sealed class S411SeaIce : S411IceFeature
{
    /// <summary>The WMO egg-code attribute bundle, or <c>null</c> if the feature carried no recognised egg-code attributes.</summary>
    public S411EggCode? EggCode { get; init; }
}

/// <summary>
/// Typed projection of an S-411 <c>LakeIce</c> feature
/// (JCOMM <c>lacice</c>) — S-411 Edition 1.2.1 Annex A.
/// </summary>
/// <remarks>Surface feature carrying the WMO egg code for inland ice.</remarks>
public sealed class S411LakeIce : S411IceFeature
{
    /// <summary>The WMO egg-code attribute bundle, or <c>null</c> if absent.</summary>
    public S411EggCode? EggCode { get; init; }
}

/// <summary>
/// Typed projection of an S-411 <c>Iceberg</c> feature
/// (JCOMM <c>icebrg</c>) — S-411 Edition 1.2.1 Annex A.
/// </summary>
public sealed class S411Iceberg : S411IceFeature
{
    /// <summary>
    /// The iceberg-size code parsed from S-411 § <c>icebergSize</c>
    /// (Annex A enumeration). <c>null</c> if absent or unparseable.
    /// </summary>
    public int? IcebergSizeCode { get; init; }
}

/// <summary>
/// Typed projection of an S-411 <c>IceEdge</c> feature
/// (JCOMM <c>icelne</c>) — S-411 Edition 1.2.1 Annex A.
/// </summary>
/// <remarks>Curve feature delineating an ice edge.</remarks>
public sealed class S411IceEdge : S411IceFeature
{
}

/// <summary>
/// Typed projection of an S-411 <c>IceLead</c> feature
/// (JCOMM <c>icelea</c>) — S-411 Edition 1.2.1 Annex A.
/// </summary>
public sealed class S411IceLead : S411IceFeature
{
    /// <summary>
    /// The ice-lead status code parsed from S-411 § <c>iceLeadStatus</c>.
    /// </summary>
    public int? IceLeadStatusCode { get; init; }
}

/// <summary>
/// Typed projection of an S-411 <c>IceThickness</c> feature
/// (JCOMM <c>icethk</c>) — S-411 Edition 1.2.1 Annex A.
/// </summary>
public sealed class S411IceThickness : S411IceFeature
{
    /// <summary>
    /// Average ice thickness parsed from S-411 § <c>iceAverageThickness</c>.
    /// </summary>
    public double? IceAverageThickness { get; init; }
}

/// <summary>
/// Typed projection of an S-411 <c>SnowCover</c> feature
/// (JCOMM <c>snwcvr</c>) — S-411 Edition 1.2.1 Annex A.
/// </summary>
public sealed class S411SnowCover : S411IceFeature
{
    /// <summary>
    /// Snow-cover concentration code parsed from
    /// S-411 § <c>snowCoverConcentration</c>.
    /// </summary>
    public int? SnowCoverConcentrationCode { get; init; }
}

/// <summary>
/// Typed projection of an S-411 <c>StageOfMelt</c> feature
/// (JCOMM <c>stgmlt</c>) — S-411 Edition 1.2.1 Annex A.
/// </summary>
public sealed class S411StageOfMelt : S411IceFeature
{
    /// <summary>Melt-stage code parsed from S-411 § <c>meltStage</c>.</summary>
    public int? MeltStageCode { get; init; }
}

/// <summary>
/// Typed projection of an S-411 <c>DataCoverage</c> feature — S-411
/// Edition 1.2.1 Annex A. Reports the area for which the dataset carries
/// ice information plus optional minimum/maximum display-scale bounds
/// (S-100 Part 9 viewing scale).
/// </summary>
/// <remarks>
/// <c>DataCoverage</c> is treated separately from the ice classes so
/// renderers can surface it as a frame / mask rather than mixing it with
/// the ice picture.
/// </remarks>
public sealed class S411DataCoverage : S411IceFeature
{
    /// <summary>Minimum (smallest-scale) display-scale denominator, when present.</summary>
    public int? MinimumDisplayScale { get; init; }

    /// <summary>Maximum (largest-scale) display-scale denominator, when present.</summary>
    public int? MaximumDisplayScale { get; init; }
}

/// <summary>
/// Catch-all typed projection for S-411 feature classes that the typed model
/// does not currently break out into a dedicated subclass — the less-common
/// dynamic and metadata classes (<c>IceCompacting</c>, <c>IceDivergence</c>,
/// <c>IceDrift</c>, <c>IceFracture</c>, <c>IceKeelBummock</c>, <c>IceRafting</c>,
/// <c>IceRidgeHummock</c>, <c>IceShear</c>, <c>IcebergArea</c>, <c>IcebergLimit</c>,
/// <c>Floeberg</c>, <c>GroundedHummock</c>, <c>JammedBrashBarrier</c>,
/// <c>LimitOfAllKnownIce</c>, <c>LimitOfOpenWater</c>, <c>LineOfIceCrack</c>,
/// <c>LineOfIceFracture</c>, <c>LineOfIceLead</c>, <c>LineOfIceRidge</c>,
/// <c>StripsAndPatches</c>).
/// </summary>
/// <remarks>
/// All source attributes survive round-trip via <see cref="S411IceFeature.ExtraAttributes"/>
/// so future passes can break individual classes out into dedicated typed
/// shapes without breaking existing callers.
/// </remarks>
public sealed class S411OtherFeature : S411IceFeature
{
}
