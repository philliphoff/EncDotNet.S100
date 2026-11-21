using EncDotNet.S100.Datasets.Pipelines.Portrayal;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// A processor whose data changes after it's loaded, such as a feed polled on
/// a timer (NDBC observations, a CAP alert feed, an ERDDAP dataset). Raising
/// <see cref="Changed"/> asks every host to draw the dataset again; picks and
/// MCP already re-query the processor, so they see the new data with no more
/// work.
/// </summary>
/// <remarks>
/// <para>
/// Raise <see cref="Changed"/> after the processor's state is updated, from any
/// thread. The map session waits briefly and coalesces events that arrive
/// close together, then re-applies the dataset (so new available times reach
/// the timeline) and renders it again, superseding a render in flight.
/// </para>
/// <para>
/// A processor that changes 1 Hz feature by feature, such as AIS, is better
/// served by an <c>IDynamicFeatureSource</c>, whose overlay updates one
/// feature at a time.
/// </para>
/// </remarks>
public interface ILiveDatasetProcessor
{
    /// <summary>Raised after the processor's data has changed.</summary>
    event EventHandler<DatasetChangedEventArgs>? Changed;
}

/// <summary>What changed in an <see cref="ILiveDatasetProcessor"/>'s data.</summary>
/// <remarks>
/// Both properties are hints; a host may redraw the whole dataset whatever
/// they say, and today's map session does.
/// </remarks>
public sealed class DatasetChangedEventArgs : EventArgs
{
    /// <summary>A change that may affect any feature, anywhere.</summary>
    public static new DatasetChangedEventArgs Empty { get; } = new();

    /// <summary>
    /// The references of the features that changed, or <see langword="null"/>
    /// when any feature may have changed.
    /// </summary>
    public IReadOnlyList<string>? FeatureRefs { get; init; }

    /// <summary>
    /// The area that changed (WGS-84), or <see langword="null"/> when the
    /// change may be anywhere.
    /// </summary>
    public GeographicBounds? Extent { get; init; }
}
