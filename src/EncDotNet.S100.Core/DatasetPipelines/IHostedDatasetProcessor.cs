using EncDotNet.S100.Datasets.Pipelines.Portrayal;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A processor over a source that a host runs, such as a live feed built from
/// a <c>SourceRegistration</c>: the host starts it, pauses it while it's
/// hidden or outside its activation range, and tells it the area in view.
/// </summary>
/// <remarks>
/// A file dataset needs none of this. A hosted processor owns its source, so
/// disposing the processor stops the source.
/// </remarks>
public interface IHostedDatasetProcessor : IDatasetProcessor, IAsyncDisposable
{
    /// <summary>Starts the source's background work. Calling it again does nothing.</summary>
    void Start();

    /// <summary>
    /// Whether the source does its work. A host sets it to <see langword="false"/>
    /// while the dataset is hidden or outside its activation range; the
    /// features already loaded stay.
    /// </summary>
    bool IsActive { get; set; }

    /// <summary>Sets the area in view, for sources that fetch only that area.</summary>
    /// <param name="area">The area, or <see langword="null"/> when there's no view yet.</param>
    void UpdateArea(GeographicBounds? area);
}
