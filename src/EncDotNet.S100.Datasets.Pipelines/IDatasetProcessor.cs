using System.Collections.Generic;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Processes a dataset file and renders it into Mapsui layers.
/// Constructed once per file; <see cref="Render"/> may be called
/// multiple times with different spec-specific contexts.
/// </summary>
public interface IDatasetProcessor
{
    string ProductSpec { get; }

    DatasetResult Render(RenderContext? context = null);

    /// <summary>
    /// Returns information about a feature identified by its reference string
    /// (as stored in the Mapsui feature via <c>FeatureRefKey</c>), or <c>null</c>
    /// if the feature cannot be found.
    /// </summary>
    FeatureInfo? GetFeatureInfo(string featureRef);
}

/// <summary>
/// Feature information returned by a pick / object-info operation.
/// </summary>
public sealed class FeatureInfo
{
    /// <summary>The feature reference string (dataset-specific ID).</summary>
    public required string FeatureRef { get; init; }

    /// <summary>
    /// The feature type code as it appears in the dataset (typically the
    /// GML element local name or the feature catalogue code, e.g.
    /// <c>"DepthArea"</c>, <c>"LateralBuoy"</c>, <c>"NavwarnPart"</c>).
    /// </summary>
    public required string FeatureType { get; init; }

    /// <summary>
    /// Human-readable feature type name resolved through the dataset's
    /// Feature Catalogue, when one is available; otherwise <c>null</c>.
    /// Viewers should prefer this over <see cref="FeatureType"/> when set.
    /// </summary>
    public string? FeatureTypeName { get; init; }

    /// <summary>
    /// Feature attributes, optionally decoded against the dataset's
    /// Feature Catalogue (see <see cref="PickAttribute"/>). Complex
    /// attributes nest their sub-attributes via
    /// <see cref="PickAttribute.Children"/>.
    /// </summary>
    public required IReadOnlyList<PickAttribute> Attributes { get; init; }

    /// <summary>
    /// xlink-style references this feature points to (e.g. S-125
    /// information bindings, S-421 route topology). Each reference's
    /// <see cref="FeatureReference.TargetRef"/> resolves against the
    /// same dataset's feature collection via
    /// <see cref="IDatasetProcessor.GetFeatureInfo"/>. Empty for
    /// products that do not model cross-references.
    /// </summary>
    public IReadOnlyList<FeatureReference> References { get; init; }
        = System.Array.Empty<FeatureReference>();
}

/// <summary>
/// An xlink-style reference from one feature to another within the same
/// dataset. Promoted from per-spec reference types (e.g.
/// <c>S125InformationReference</c>, <c>S421Reference</c>) so that the
/// pick UI can offer uniform "follow reference" navigation across all
/// GML-encoded products.
/// </summary>
public sealed class FeatureReference
{
    /// <summary>
    /// The role / association name as it appears in the dataset (e.g.
    /// <c>"AtonStatus"</c> for an S-125 information binding, or
    /// <c>"routeWaypoints"</c> for an S-421 route → waypoints link).
    /// </summary>
    public required string Role { get; init; }

    /// <summary>
    /// The referenced feature's identifier (matches
    /// <see cref="FeatureInfo.FeatureRef"/> on the target). Already
    /// trimmed of any leading <c>#</c> from the underlying
    /// <c>xlink:href</c>.
    /// </summary>
    public required string TargetRef { get; init; }

    /// <summary>
    /// The <c>xlink:arcrole</c> value when present; conveys the semantic
    /// of the link (e.g. an S-421 segment role) and may be null.
    /// </summary>
    public string? ArcRole { get; init; }
}
