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
}
