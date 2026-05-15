using System.Collections.Immutable;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Gml;

namespace EncDotNet.S100.Datasets.S122.DataModel;

/// <summary>The geometry primitive kind of an S-122 feature.</summary>
/// <remarks>
/// Geometry-less feature instances are allowed by the S-122 Feature
/// Catalogue (e.g. an area that delegates its boundary to an associated
/// <c>InformationArea</c>); renderers and consumers must tolerate
/// <see cref="None"/>.
/// </remarks>
public enum S122GeometryKind
{
    /// <summary>No geometry.</summary>
    None,
    /// <summary>Single point (<see cref="GeoPosition"/>).</summary>
    Point,
    /// <summary>Curve (ordered sequence of <see cref="GeoPosition"/>).</summary>
    Curve,
    /// <summary>Surface exterior ring (closed sequence of <see cref="GeoPosition"/>).</summary>
    Surface,
}

/// <summary>
/// Common shape implemented by every typed S-122 feature projection
/// (S-122 FC 2.0.0 § Feature Types). Allows generic consumers to walk
/// <see cref="S122MarineProtectedAreaDataset.Features"/> without
/// switching on concrete type.
/// </summary>
public interface IS122Feature
{
    /// <summary>The GML identifier of the source feature.</summary>
    string Id { get; }

    /// <summary>The S-122 feature type code (the GML element local name).</summary>
    string FeatureType { get; }

    /// <summary>Geometry primitive kind of the source feature.</summary>
    S122GeometryKind GeometryKind { get; }

    /// <summary>Coordinates whose semantics depend on <see cref="GeometryKind"/>.</summary>
    ImmutableArray<GeoPosition> Coordinates { get; }

    /// <summary>The raw <c>xlink:href</c> cross-references from the source feature.</summary>
    ImmutableArray<GmlReference> References { get; }

    /// <summary>Information-type bindings resolved from <see cref="References"/>.</summary>
    ImmutableArray<S122InformationReference> InformationReferences { get; }

    /// <summary>Feature-to-feature bindings resolved from <see cref="References"/>.</summary>
    ImmutableArray<S122FeatureReference> FeatureReferences { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    ImmutableDictionary<string, string> ExtraAttributes { get; }
}

/// <summary>
/// Common shape implemented by every typed S-122 information-type
/// projection (S-122 FC 2.0.0 § Information Types).
/// </summary>
public interface IS122InformationType
{
    /// <summary>The GML identifier.</summary>
    string Id { get; }

    /// <summary>The S-122 information-type code (the GML element local name).</summary>
    string TypeCode { get; }

    /// <summary>The raw <c>xlink:href</c> cross-references.</summary>
    ImmutableArray<GmlReference> References { get; }

    /// <summary>Information-type bindings resolved from <see cref="References"/>.</summary>
    ImmutableArray<S122InformationReference> InformationReferences { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    ImmutableDictionary<string, string> ExtraAttributes { get; }
}

/// <summary>
/// A typed binding from a referring object to an information-type
/// target, resolved from an <c>xlink:href</c> by
/// <see cref="S122MarineProtectedAreaDataset.From"/>.
/// </summary>
/// <param name="Role">The GML role (containing element local name, e.g. <c>theAuthority</c>).</param>
/// <param name="ArcRole">The optional <c>xlink:arcrole</c>.</param>
/// <param name="Target">The resolved typed information type.</param>
public sealed record S122InformationReference(
    string Role,
    string? ArcRole,
    IS122InformationType Target);

/// <summary>
/// A typed binding from a referring object to a feature target,
/// resolved from an <c>xlink:href</c> by
/// <see cref="S122MarineProtectedAreaDataset.From"/>.
/// </summary>
/// <param name="Role">The GML role (containing element local name, e.g. <c>theCartographicText</c>).</param>
/// <param name="ArcRole">The optional <c>xlink:arcrole</c>.</param>
/// <param name="Target">The resolved typed feature.</param>
public sealed record S122FeatureReference(
    string Role,
    string? ArcRole,
    IS122Feature Target);

/// <summary>
/// Common base for the three S-122 RxN (Recommendations / Regulations /
/// Restrictions) concrete information-type projections, which all
/// derive from the FC-abstract <c>AbstractRxN</c> (S-122 FC 2.0.0
/// §AbstractRxN).
/// </summary>
public abstract class S122AbstractRxN : IS122InformationType
{
    /// <inheritdoc/>
    public required string Id { get; init; }

    /// <inheritdoc/>
    public abstract string TypeCode { get; }

    /// <summary>The category-of-authority code (S-122 FC §categoryOfAuthority), when present.</summary>
    public int? CategoryOfAuthority { get; init; }

    /// <summary>The RxN code (S-122 FC §rxNCode), when present.</summary>
    public string? RxNCode { get; init; }

    /// <summary>The text content payload (S-122 FC §textContent), when present.</summary>
    public string? TextContent { get; init; }

    /// <inheritdoc/>
    public ImmutableArray<GmlReference> References { get; init; } = ImmutableArray<GmlReference>.Empty;

    /// <inheritdoc/>
    public ImmutableArray<S122InformationReference> InformationReferences { get; internal set; } =
        ImmutableArray<S122InformationReference>.Empty;

    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
}
