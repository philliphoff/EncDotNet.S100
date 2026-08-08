namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Renderer-neutral display state for one portrayal sub-layer of a loaded
/// dataset.
/// </summary>
public sealed class MapDatasetSubLayer
{
    /// <summary>
    /// Creates a sub-layer state snapshot.
    /// </summary>
    /// <param name="key">
    /// Stable processor-supplied key used to reconcile the sub-layer across
    /// re-portrayals.
    /// </param>
    /// <param name="name">Human-readable, non-localized sub-layer name.</param>
    /// <param name="isVisible">Whether the sub-layer is enabled.</param>
    /// <param name="opacity">Sub-layer opacity in the inclusive range 0..1.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="key"/> or <paramref name="name"/> is empty or consists
    /// only of white-space characters.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="opacity"/> is not finite or lies outside 0..1.
    /// </exception>
    public MapDatasetSubLayer(
        string key,
        string name,
        bool isVisible = true,
        double opacity = 1.0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNotEqual(double.IsFinite(opacity), true, nameof(opacity));
        ArgumentOutOfRangeException.ThrowIfLessThan(opacity, 0.0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(opacity, 1.0);

        Key = key;
        Name = name;
        IsVisible = isVisible;
        Opacity = opacity;
    }

    /// <summary>
    /// Stable processor-supplied key used to reconcile this sub-layer across
    /// re-portrayals.
    /// </summary>
    public string Key { get; }

    /// <summary>Human-readable, non-localized sub-layer name.</summary>
    public string Name { get; }

    /// <summary>Whether this sub-layer is enabled.</summary>
    public bool IsVisible { get; }

    /// <summary>Sub-layer opacity in the inclusive range 0..1.</summary>
    public double Opacity { get; }
}
