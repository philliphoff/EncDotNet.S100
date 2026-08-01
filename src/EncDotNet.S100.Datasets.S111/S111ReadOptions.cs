namespace EncDotNet.S100.Datasets.S111;

/// <summary>
/// Options controlling how <see cref="S111DatasetReader.ReadAny(EncDotNet.S100.Hdf5.IHdf5File, S111ReadOptions?)"/>
/// materializes a dataset.
/// </summary>
public sealed class S111ReadOptions
{
    /// <summary>
    /// When <see langword="true"/>, dcf2 (regular-grid) per-time-step
    /// <c>values</c> datasets are read lazily on first access rather than
    /// decoded up front. Per-step time points are derived arithmetically
    /// from <c>dateTimeOfFirstRecord</c> + i × <c>timeRecordInterval</c>
    /// (S-111 Edition 2.0.0 §10.2.6), so the reader does not open every
    /// <c>Group_NNN</c> at load time.
    /// </summary>
    /// <remarks>
    /// The caller must keep the underlying HDF5 file open for the lifetime
    /// of the returned dataset. dcf1/dcf3/dcf8 station-series datasets always
    /// materialize fully and ignore this flag.
    /// </remarks>
    public bool DeferValueReads { get; init; }

    /// <summary>
    /// Optional synchronization object guarding access to the underlying
    /// HDF5 file. PureHDF reads from a single shared stream are not
    /// concurrency-safe, so lazy value factories lock this object before
    /// reading. When <see langword="null"/>, each instance group uses its
    /// own private lock. Callers that keep the file open and read from it
    /// elsewhere should pass the same object and lock it around their own
    /// reads.
    /// </summary>
    public object? HdfSyncRoot { get; init; }
}
