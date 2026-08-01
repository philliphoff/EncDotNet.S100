using EncDotNet.S100.Core;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Validation;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Immutable, renderer- and UI-neutral snapshot of one loaded dataset's map
/// state.
/// </summary>
/// <remarks>
/// <para>
/// The snapshot combines the processor's domain metadata with host-owned
/// display and lifecycle choices. It intentionally carries no rendered layer,
/// framework event, localized string, or UI command.
/// </para>
/// <para>
/// <see cref="IsVisible"/> and <see cref="IsActive"/> are independent:
/// visibility is a visual toggle, while active state controls participation in
/// cross-product composition and queries.
/// </para>
/// </remarks>
public sealed class MapDataset
{
    /// <summary>
    /// Creates a loaded dataset state snapshot.
    /// </summary>
    /// <param name="id">Stable identity assigned by the host.</param>
    /// <param name="name">Human-readable, non-localized dataset name.</param>
    /// <param name="metadata">Product, extent, scale, CRS, and time metadata.</param>
    /// <param name="isVisible">Whether the dataset is visually enabled.</param>
    /// <param name="isActive">
    /// Whether the dataset participates in cross-product composition and
    /// queries.
    /// </param>
    /// <param name="opacity">Dataset opacity in the inclusive range 0..1.</param>
    /// <param name="availableTimes">Materialized time samples, in source order.</param>
    /// <param name="currentTime">Time sample currently portrayed, if any.</param>
    /// <param name="subLayers">Renderer-neutral sub-layer display state.</param>
    /// <param name="validation">
    /// Validation report, or <c>null</c> when no rule pack exists.
    /// </param>
    /// <param name="versionAssessment">
    /// Assessment of the declared product edition against implemented editions.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="id"/> is the default value, or <paramref name="name"/>
    /// is empty or consists only of white-space characters.
    /// </exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="metadata"/> is <c>null</c>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="opacity"/> is not finite or lies outside 0..1.
    /// </exception>
    public MapDataset(
        MapDatasetId id,
        string name,
        DatasetMetadata metadata,
        bool isVisible = true,
        bool isActive = true,
        double opacity = 1.0,
        IReadOnlyList<DateTime>? availableTimes = null,
        DateTime? currentTime = null,
        IReadOnlyList<MapDatasetSubLayer>? subLayers = null,
        ValidationReport? validation = null,
        SpecVersionAssessment? versionAssessment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id.Value, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(opacity), true, nameof(opacity));
        ArgumentOutOfRangeException.ThrowIfLessThan(opacity, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opacity, 1.0);

        Id = id;
        Name = name;
        Metadata = metadata;
        IsVisible = isVisible;
        IsActive = isActive;
        Opacity = opacity;
        AvailableTimes = availableTimes?.ToArray() ?? [];
        CurrentTime = currentTime;
        SubLayers = subLayers?.ToArray() ?? [];
        Validation = validation;
        VersionAssessment = versionAssessment;
    }

    /// <summary>Stable identity assigned by the host.</summary>
    public MapDatasetId Id { get; }

    /// <summary>Human-readable, non-localized dataset name.</summary>
    public string Name { get; }

    /// <summary>Product, extent, scale, CRS, and time metadata.</summary>
    public DatasetMetadata Metadata { get; }

    /// <summary>
    /// Dataset extent in the coordinate reference system identified by
    /// <see cref="DatasetMetadata.HorizontalCrsEpsg"/>, or <c>null</c>.
    /// </summary>
    public BoundingBox? Extent => Metadata.Extent;

    /// <summary>Whether the dataset is visually enabled.</summary>
    public bool IsVisible { get; }

    /// <summary>
    /// Whether the dataset participates in cross-product composition and
    /// queries.
    /// </summary>
    public bool IsActive { get; }

    /// <summary>Dataset opacity in the inclusive range 0..1.</summary>
    public double Opacity { get; }

    /// <summary>Materialized time samples, in source order.</summary>
    public IReadOnlyList<DateTime> AvailableTimes { get; }

    /// <summary>Time sample currently portrayed, or <c>null</c>.</summary>
    public DateTime? CurrentTime { get; }

    /// <summary>Whether at least one time sample is available.</summary>
    public bool HasTimeSteps => AvailableTimes.Count > 0;

    /// <summary>Renderer-neutral sub-layer display state.</summary>
    public IReadOnlyList<MapDatasetSubLayer> SubLayers { get; }

    /// <summary>
    /// Validation report, or <c>null</c> when no rule pack exists for the
    /// product.
    /// </summary>
    public ValidationReport? Validation { get; }

    /// <summary>
    /// Assessment of the declared product edition against the editions
    /// implemented by the application, or <c>null</c>.
    /// </summary>
    public SpecVersionAssessment? VersionAssessment { get; }
}
