using System.Collections;
using System.Diagnostics.CodeAnalysis;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Keeps a point-in-time set of Viewer processor mappings leased for the
/// duration of a synchronous operation.
/// </summary>
internal sealed class DatasetProcessorSnapshot :
    IReadOnlyDictionary<DatasetEntry, IDatasetProcessor>,
    IDisposable
{
    private readonly IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> _processors;
    private IReadOnlyList<IDisposable>? _leases;

    public DatasetProcessorSnapshot(
        IReadOnlyDictionary<DatasetEntry, IDatasetProcessor> processors,
        IReadOnlyList<IDisposable>? leases = null)
    {
        ArgumentNullException.ThrowIfNull(processors);
        _processors = processors;
        _leases = leases;
    }

    public IDatasetProcessor this[DatasetEntry key] => _processors[key];

    public IEnumerable<DatasetEntry> Keys => _processors.Keys;

    public IEnumerable<IDatasetProcessor> Values => _processors.Values;

    public int Count => _processors.Count;

    public bool ContainsKey(DatasetEntry key) => _processors.ContainsKey(key);

    public IEnumerator<KeyValuePair<DatasetEntry, IDatasetProcessor>> GetEnumerator() =>
        _processors.GetEnumerator();

    public bool TryGetValue(
        DatasetEntry key,
        [MaybeNullWhen(false)] out IDatasetProcessor value) =>
        _processors.TryGetValue(key, out value);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Dispose()
    {
        var leases = Interlocked.Exchange(ref _leases, null);
        if (leases is null)
            return;

        foreach (var lease in leases)
            lease.Dispose();
    }
}
