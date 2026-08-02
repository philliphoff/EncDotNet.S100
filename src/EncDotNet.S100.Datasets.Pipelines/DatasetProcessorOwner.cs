using System.Diagnostics.CodeAnalysis;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Owns loaded dataset processors by their host-stable
/// <see cref="MapDatasetId"/>.
/// </summary>
/// <remarks>
/// <para>
/// This type lives with the aggregate dataset pipelines because it owns
/// <see cref="IDatasetProcessor"/> instances but has no renderer, Mapsui, or UI
/// dependency. It is the processor-lifecycle core intended for a future map
/// session.
/// </para>
/// <para>
/// Registration transfers ownership only when <see cref="TryRegister"/>
/// returns <see langword="true"/>. Duplicate identities are rejected and
/// remain owned by the caller. Removal prevents new acquisition immediately;
/// disposal waits for active <see cref="DatasetProcessorLease"/> instances.
/// Current disposable processors implement <see cref="IDisposable"/>, so this
/// owner follows the same synchronous disposal contract.
/// </para>
/// </remarks>
public sealed class DatasetProcessorOwner : IDisposable
{
    private readonly object _sync = new();
    private readonly Dictionary<MapDatasetId, Entry> _entries = [];
    private bool _disposed;

    /// <summary>Gets the number of processors currently available to acquire.</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// Attempts to transfer ownership of <paramref name="processor"/> to this
    /// owner under <paramref name="datasetId"/>.
    /// </summary>
    /// <param name="datasetId">The stable dataset identity.</param>
    /// <param name="processor">The processor to own.</param>
    /// <returns>
    /// <see langword="true"/> when ownership transferred; otherwise
    /// <see langword="false"/> when the identity was already registered.
    /// </returns>
    /// <exception cref="ObjectDisposedException">The owner is disposed.</exception>
    public bool TryRegister(MapDatasetId datasetId, IDatasetProcessor processor)
    {
        ValidateDatasetId(datasetId);
        ArgumentNullException.ThrowIfNull(processor);

        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_entries.ContainsKey(datasetId))
                return false;

            _entries.Add(datasetId, new Entry(processor));
            return true;
        }
    }

    /// <summary>
    /// Attempts to acquire a lease for the processor identified by
    /// <paramref name="datasetId"/>.
    /// </summary>
    /// <param name="datasetId">The stable dataset identity.</param>
    /// <param name="lease">
    /// The acquired lease, or <see langword="null"/> when no processor is
    /// registered with that identity.
    /// </param>
    /// <returns><see langword="true"/> when a lease was acquired.</returns>
    public bool TryAcquire(
        MapDatasetId datasetId,
        [NotNullWhen(true)] out DatasetProcessorLease? lease)
    {
        ValidateDatasetId(datasetId);

        lock (_sync)
        {
            if (_disposed || !_entries.TryGetValue(datasetId, out var entry))
            {
                lease = null;
                return false;
            }

            entry.LeaseCount++;
            lease = new DatasetProcessorLease(
                datasetId,
                entry.Processor,
                () => Release(entry));
            return true;
        }
    }

    /// <summary>
    /// Returns whether this owner currently maps <paramref name="datasetId"/>
    /// to the exact <paramref name="processor"/> instance.
    /// </summary>
    /// <param name="datasetId">The stable dataset identity.</param>
    /// <param name="processor">The expected processor instance.</param>
    /// <returns><see langword="true"/> when the mapping is current.</returns>
    public bool Owns(MapDatasetId datasetId, IDatasetProcessor processor)
    {
        ValidateDatasetId(datasetId);
        ArgumentNullException.ThrowIfNull(processor);

        lock (_sync)
        {
            return !_disposed
                && _entries.TryGetValue(datasetId, out var entry)
                && ReferenceEquals(entry.Processor, processor);
        }
    }

    /// <summary>
    /// Removes and eventually disposes the processor identified by
    /// <paramref name="datasetId"/>.
    /// </summary>
    /// <param name="datasetId">The stable dataset identity.</param>
    /// <returns><see langword="true"/> when a processor was removed.</returns>
    public bool Remove(MapDatasetId datasetId)
    {
        ValidateDatasetId(datasetId);
        return RemoveCore(datasetId, expectedProcessor: null);
    }

    /// <summary>
    /// Removes and eventually disposes the processor only when the identity
    /// still maps to <paramref name="expectedProcessor"/>.
    /// </summary>
    /// <param name="datasetId">The stable dataset identity.</param>
    /// <param name="expectedProcessor">The processor expected to be current.</param>
    /// <returns><see langword="true"/> when the expected mapping was removed.</returns>
    public bool Remove(MapDatasetId datasetId, IDatasetProcessor expectedProcessor)
    {
        ValidateDatasetId(datasetId);
        ArgumentNullException.ThrowIfNull(expectedProcessor);
        return RemoveCore(datasetId, expectedProcessor);
    }

    /// <summary>
    /// Returns a point-in-time list of identities currently available to
    /// acquire.
    /// </summary>
    /// <returns>A materialized identity snapshot.</returns>
    public IReadOnlyList<MapDatasetId> GetDatasetIds()
    {
        lock (_sync)
        {
            return _entries.Keys.ToArray();
        }
    }

    /// <summary>
    /// Prevents new registration or acquisition and schedules every owned
    /// processor for disposal.
    /// </summary>
    /// <remarks>
    /// Processors without active leases are disposed before this method
    /// returns. Leased processors are disposed by the last lease release.
    /// Repeated calls are no-ops.
    /// </remarks>
    public void Dispose()
    {
        Entry[] entries;
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            entries = _entries.Values.ToArray();
            _entries.Clear();
            foreach (var entry in entries)
                entry.Retired = true;
        }

        foreach (var entry in entries)
            TryDispose(entry);
    }

    private bool RemoveCore(MapDatasetId datasetId, IDatasetProcessor? expectedProcessor)
    {
        Entry? entry;
        lock (_sync)
        {
            if (_disposed || !_entries.TryGetValue(datasetId, out entry))
                return false;
            if (expectedProcessor is not null
                && !ReferenceEquals(entry.Processor, expectedProcessor))
            {
                return false;
            }

            _entries.Remove(datasetId);
            entry.Retired = true;
        }

        TryDispose(entry);
        return true;
    }

    private void Release(Entry entry)
    {
        lock (_sync)
        {
            entry.LeaseCount--;
        }

        TryDispose(entry);
    }

    private void TryDispose(Entry entry)
    {
        IDatasetProcessor? processor = null;
        lock (_sync)
        {
            if (entry.Retired && entry.LeaseCount == 0 && !entry.Disposed)
            {
                entry.Disposed = true;
                processor = entry.Processor;
            }
        }

        if (processor is IDisposable disposable)
            disposable.Dispose();
    }

    private static void ValidateDatasetId(MapDatasetId datasetId)
    {
        if (string.IsNullOrWhiteSpace(datasetId.Value))
        {
            throw new ArgumentException(
                "The dataset identifier must not be the default value.",
                nameof(datasetId));
        }
    }

    private sealed class Entry(IDatasetProcessor processor)
    {
        public IDatasetProcessor Processor { get; } = processor;

        public int LeaseCount { get; set; }

        public bool Retired { get; set; }

        public bool Disposed { get; set; }
    }
}
