using EncDotNet.S100.Core;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.Services;

namespace EncDotNet.S100.Viewer.Tests;

/// <summary>
/// Test-only <see cref="LoadedDatasetData"/> payload so tests can build
/// <see cref="LoadedDataset"/> snapshots without real dataset bytes. The
/// open / close tools only read <c>Id</c>, <c>Spec</c>, and <c>Bounds</c>,
/// never the payload.
/// </summary>
internal sealed record FakeLoadedData : LoadedDatasetData;

/// <summary>
/// Mutable in-memory <see cref="IDatasetCatalog"/> for tool tests. Adds /
/// removes publish a fresh immutable snapshot and raise
/// <see cref="Changed"/>, mirroring the production catalog contract.
/// </summary>
internal sealed class FakeDatasetCatalog : IDatasetCatalog
{
    private readonly object _gate = new();
    private IReadOnlyList<LoadedDataset> _datasets = [];

    public IReadOnlyList<LoadedDataset> Datasets
    {
        get { lock (_gate) { return _datasets; } }
    }

    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    public LoadedDataset Add(string id, string spec = "S-101", BoundingBox? bounds = null)
    {
        var dataset = new LoadedDataset(
            new DatasetId(id),
            new SpecRef(spec, new SpecVersion(1, 0, 0)),
            bounds ?? new BoundingBox(50.0, -1.5, 50.5, -1.0),
            TimeRange: null,
            new FakeLoadedData());
        lock (_gate) { _datasets = [.. _datasets, dataset]; }
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
        {
            Kind = DatasetCatalogChangeKind.Added,
            DatasetId = dataset.Id,
        });
        return dataset;
    }

    public int Remove(string id)
    {
        var removed = 0;
        lock (_gate)
        {
            var next = new List<LoadedDataset>();
            foreach (var d in _datasets)
            {
                if (string.Equals(d.Id.Value, id, StringComparison.Ordinal))
                {
                    removed++;
                }
                else
                {
                    next.Add(d);
                }
            }
            _datasets = next.ToArray();
        }
        if (removed > 0)
        {
            Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
            {
                Kind = DatasetCatalogChangeKind.Removed,
                DatasetId = new DatasetId(id),
            });
        }
        return removed;
    }
}

/// <summary>
/// Scriptable <see cref="IDatasetLoadGateway"/> so tool-logic tests can
/// drive load / unload outcomes deterministically without the UI thread.
/// </summary>
internal sealed class FakeDatasetLoadGateway : IDatasetLoadGateway
{
    public bool IsReady { get; set; } = true;
    public DatasetPathKind Kind { get; set; } = DatasetPathKind.File;
    public Func<string, string?, Task<bool>>? OnLoadFile { get; set; }
    public Func<string, Task<int>>? OnTriggerExchangeSet { get; set; }
    public Func<string, Task<int>>? OnRemove { get; set; }

    public DatasetPathKind Classify(string path) => Kind;

    public Task<bool> LoadFileAsync(string path, string? specHint, CancellationToken cancellationToken = default)
        => OnLoadFile?.Invoke(path, specHint) ?? Task.FromResult(false);

    public Task<int> TriggerExchangeSetAsync(string path, CancellationToken cancellationToken = default)
        => OnTriggerExchangeSet?.Invoke(path) ?? Task.FromResult(0);

    public Task<int> RemoveAsync(string datasetId, CancellationToken cancellationToken = default)
        => OnRemove?.Invoke(datasetId) ?? Task.FromResult(0);

    public Task<IDisposable> LockAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IDisposable>(new NoopScope());

    private sealed class NoopScope : IDisposable
    {
        public void Dispose() { }
    }
}
