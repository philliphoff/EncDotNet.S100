using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S125.DataModel;

/// <summary>The geometry primitive kind of an S-125 feature.</summary>
public enum S125GeometryKind
{
    /// <summary>No geometry (e.g. an <c>AtonAggregation</c>).</summary>
    None,
    /// <summary>Single point.</summary>
    Point,
    /// <summary>Curve (ordered sequence of coordinates).</summary>
    Curve,
    /// <summary>Surface exterior ring (closed sequence of coordinates).</summary>
    Surface,
}

/// <summary>
/// The flavour of an S-125 aggregation / association feature
/// (S-125 Edition 1.0.0 §AtonAggregation, §AtonAssociation).
/// </summary>
public enum S125AggregationKind
{
    /// <summary>Unrecognised aggregation type.</summary>
    Unknown,
    /// <summary>§AtonAggregation.</summary>
    Aggregation,
    /// <summary>§AtonAssociation.</summary>
    Association,
}

/// <summary>
/// Typed projection of an S-125 aggregation or association feature
/// (S-125 Edition 1.0.0 §AtonAggregation, §AtonAssociation). These
/// features are geometry-less containers that bind one or more
/// aids to navigation by xlink.
/// </summary>
public sealed class S125Aggregation
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>Whether this is an aggregation or an association.</summary>
    public S125AggregationKind Kind { get; init; }

    /// <summary>The category code, when supplied (§categoryOfAggregation / §categoryOfAssociation).</summary>
    public int? CategoryCode { get; init; }

    /// <summary>The aids to navigation bound by this aggregation, resolved from feature xlinks.</summary>
    public ImmutableArray<IS125Aid> Members { get; init; } = ImmutableArray<IS125Aid>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Typed projection of a <c>SpatialQuality</c> information type
/// (S-125 Edition 1.0.0 §SpatialQuality).
/// </summary>
public sealed class S125SpatialQuality
{
    /// <summary>The GML identifier.</summary>
    public required string Id { get; init; }

    /// <summary>The quality-of-position code, when supplied (§qualityOfPosition).</summary>
    public int? QualityOfPosition { get; init; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}

/// <summary>
/// Catch-all typed projection for S-125 feature classes that the typed
/// model does not break out individually — the line/area/metadata
/// features that fall outside the AtoN-status spotlight
/// (S-125 Edition 1.0.0 §NavigationLine, §RecommendedTrack,
/// §LocalDirectionOfBuoyage, §NavigationalSystemOfMarks,
/// §DangerousFeature, §DataCoverage, §QualityOfBathymetricData,
/// §SoundingDatum, §VerticalDatumOfData).
/// </summary>
/// <remarks>
/// All source attributes survive round-trip via <see cref="ExtraAttributes"/>
/// so future passes can break individual classes out into dedicated typed
/// shapes without breaking callers.
/// </remarks>
public sealed class S125OtherFeature
{
    /// <summary>The GML identifier of the source feature.</summary>
    public required string Id { get; init; }

    /// <summary>The raw S-125 feature type code.</summary>
    public required string FeatureType { get; init; }

    /// <summary>The geometry primitive kind.</summary>
    public S125GeometryKind GeometryKind { get; init; }

    /// <summary>Coordinates whose semantics depend on <see cref="GeometryKind"/>.</summary>
    public ImmutableArray<GeoPosition> Coordinates { get; init; } = ImmutableArray<GeoPosition>.Empty;

    /// <summary>Source attributes that the typed model did not consume.</summary>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
