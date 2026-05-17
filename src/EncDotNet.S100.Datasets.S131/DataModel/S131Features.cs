using System.Collections.Immutable;
using EncDotNet.S100.DataModel;

namespace EncDotNet.S100.Datasets.S131.DataModel;

/// <summary>
/// Common contract exposed by every typed S-131 feature
/// (<see cref="S131HarbourInfrastructure"/>, <see cref="S131LayoutFeature"/>,
/// <see cref="S131MetadataFeature"/>, <see cref="S131OtherFeature"/>).
/// Allows callers to enumerate <see cref="S131HarbourInfrastructureDataset.Features"/>
/// uniformly and dispatch on <see cref="Family"/> when the high-level
/// classification matters.
/// </summary>
/// <remarks>
/// S-131 features span all three GML geometric primitives plus the
/// geometry-less case (container features such as <c>Authority</c> are
/// modelled as information types in this codebase, but a feature may
/// still legitimately have no geometry — see FC §B.2). Consumers
/// should consult <see cref="S131Geometry.GeometryType"/> before
/// indexing into the coordinate arrays.
/// </remarks>
public interface IS131Feature
{
    /// <summary>The GML identifier of the source feature.</summary>
    string Id { get; }

    /// <summary>The raw S-131 feature type code (e.g. <c>"Bollard"</c>).</summary>
    string FeatureType { get; }

    /// <summary>The high-level family the feature belongs to.</summary>
    S131FeatureFamily Family { get; }

    /// <summary>The typed geometry payload (never <c>null</c>; check
    /// <see cref="S131Geometry.IsEmpty"/> for geometry-less features).</summary>
    S131Geometry Geometry { get; }

    /// <summary>
    /// Cross-references from this feature to other objects in the
    /// dataset, resolved via <c>xlink:href</c>. References whose target
    /// did not resolve carry a <c>null</c>
    /// <see cref="S131ResolvedReference.Target"/> and an associated
    /// <c>xlink.unresolved</c> projection diagnostic.
    /// </summary>
    ImmutableArray<S131ResolvedReference> ResolvedReferences { get; }

    /// <summary>Source attributes that the typed model did not consume.</summary>
    ImmutableDictionary<string, string> ExtraAttributes { get; }

    /// <summary>The originating raw GML feature.</summary>
    S131Feature Source { get; }
}

/// <summary>
/// Typed projection of an S-131 feature derived from
/// <c>HarbourPhysicalInfrastructure</c> in the FC supertype graph —
/// bollards, dolphins, dry docks, mooring buoys, lock basins, ship
/// lifts, and other fixed harbour-side installations
/// (S-131 FC Edition 1.0.0 §B.2).
/// </summary>
public sealed class S131HarbourInfrastructure : IS131Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <inheritdoc/>
    public S131FeatureFamily Family => S131FeatureFamily.HarbourInfrastructure;
    /// <summary>The concrete harbour-infrastructure kind, decoded from <see cref="FeatureType"/>.</summary>
    public required S131HarbourInfrastructureKind Kind { get; init; }
    /// <inheritdoc/>
    public required S131Geometry Geometry { get; init; }
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131Feature Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 feature derived from <c>Layout</c> in
/// the FC supertype graph — area- and berth-style features such as
/// berths, anchorage areas, terminals, harbour basins, pilot boarding
/// places, dumping grounds, and fender lines
/// (S-131 FC Edition 1.0.0 §B.2).
/// </summary>
public sealed class S131LayoutFeature : IS131Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <inheritdoc/>
    public S131FeatureFamily Family => S131FeatureFamily.Layout;
    /// <summary>The concrete layout kind, decoded from <see cref="FeatureType"/>.</summary>
    public required S131LayoutKind Kind { get; init; }
    /// <inheritdoc/>
    public required S131Geometry Geometry { get; init; }
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131Feature Source { get; init; }
}

/// <summary>
/// Typed projection of an S-131 standalone-metadata feature —
/// <c>DataCoverage</c>, <c>QualityOfNonBathymetricData</c>,
/// <c>SoundingDatum</c>, <c>VerticalDatumOfData</c>, or
/// <c>TextPlacement</c> (S-131 FC Edition 1.0.0 §B.2; no shared
/// supertype).
/// </summary>
public sealed class S131MetadataFeature : IS131Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <inheritdoc/>
    public S131FeatureFamily Family => S131FeatureFamily.Metadata;
    /// <summary>The concrete metadata kind, decoded from <see cref="FeatureType"/>.</summary>
    public required S131MetadataKind Kind { get; init; }
    /// <inheritdoc/>
    public required S131Geometry Geometry { get; init; }
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131Feature Source { get; init; }
}

/// <summary>
/// Typed projection fallback for a feature whose
/// <see cref="S131Feature.FeatureType"/> code is not recognised in the
/// S-131 FC enumeration baked into the typed model. The projection
/// also emits an <c>s131.feature.unknown</c> info diagnostic so callers
/// can detect a forward-compatibility gap.
/// </summary>
public sealed class S131OtherFeature : IS131Feature
{
    /// <inheritdoc/>
    public required string Id { get; init; }
    /// <inheritdoc/>
    public required string FeatureType { get; init; }
    /// <inheritdoc/>
    public S131FeatureFamily Family => S131FeatureFamily.Unknown;
    /// <inheritdoc/>
    public required S131Geometry Geometry { get; init; }
    /// <inheritdoc/>
    public ImmutableArray<S131ResolvedReference> ResolvedReferences { get; init; } =
        ImmutableArray<S131ResolvedReference>.Empty;
    /// <inheritdoc/>
    public ImmutableDictionary<string, string> ExtraAttributes { get; init; } =
        ImmutableDictionary<string, string>.Empty;
    /// <inheritdoc/>
    public required S131Feature Source { get; init; }
}
