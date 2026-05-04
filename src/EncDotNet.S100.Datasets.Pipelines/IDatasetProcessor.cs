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
/// Feature information returned by a pick/identify operation.
/// </summary>
public sealed class FeatureInfo
{
    /// <summary>The feature reference string (dataset-specific ID).</summary>
    public required string FeatureRef { get; init; }

    /// <summary>The feature type name (e.g. "DepthArea", "LateralBuoy", "NavwarnPart").</summary>
    public required string FeatureType { get; init; }

    /// <summary>Feature attribute values keyed by attribute code.</summary>
    public required IReadOnlyDictionary<string, string?> Attributes { get; init; }
}
