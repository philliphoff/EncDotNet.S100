using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.DynamicSources;

/// <summary>
/// A source that does nothing until it's started, such as one that reads a
/// network input or polls a feed. A host starts it once it's shown.
/// </summary>
public interface IStartableDynamicSource : IDynamicFeatureSource
{
    /// <summary>Starts the source's background work. Calling it again does nothing.</summary>
    void Start();
}

/// <summary>
/// A source that can pause its work while it isn't shown, keeping the
/// features it has, such as a polled feed that stops polling.
/// </summary>
public interface IPausableDynamicSource : IDynamicFeatureSource
{
    /// <summary>Whether the source does its work; <see langword="false"/> pauses it.</summary>
    bool IsActive { get; set; }
}

/// <summary>
/// A source that fetches only the area a host shows, such as station
/// observations fetched for the stations in view.
/// </summary>
public interface IAreaFilteredDynamicSource : IDynamicFeatureSource
{
    /// <summary>Sets the area the host shows; <see langword="null"/> when there's no view yet.</summary>
    /// <param name="area">The area.</param>
    void UpdateArea(BoundingBox? area);
}
