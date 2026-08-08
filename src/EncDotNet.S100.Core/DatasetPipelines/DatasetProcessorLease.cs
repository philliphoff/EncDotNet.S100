namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Keeps one processor owned by a <see cref="DatasetProcessorOwner"/> alive
/// while a caller uses it.
/// </summary>
/// <remarks>
/// Removing the processor from its owner prevents new leases immediately, but
/// disposal is deferred until every existing lease has been released.
/// </remarks>
public sealed class DatasetProcessorLease : IDisposable
{
    private Action? _release;

    internal DatasetProcessorLease(
        MapDatasetId datasetId,
        IDatasetProcessor processor,
        Action release)
    {
        DatasetId = datasetId;
        Processor = processor;
        _release = release;
    }

    /// <summary>Gets the stable identity of the leased processor.</summary>
    public MapDatasetId DatasetId { get; }

    /// <summary>Gets the processor kept alive by this lease.</summary>
    public IDatasetProcessor Processor { get; }

    /// <summary>Releases the lease.</summary>
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
