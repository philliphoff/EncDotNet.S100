using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Mcp.Tools.Mutable;

namespace EncDotNet.S100.Mcp.Tools.Tests.Fakes;

/// <summary>
/// In-memory <see cref="IMutableDatasetCatalog"/> for unit-testing the mutating
/// catalog tools. <see cref="LoadAsync"/> adds the datasets staged in
/// <see cref="NextLoad"/> (letting a test drive success / empty-load paths)
/// and publishes <see cref="Changed"/> like a real catalog.
/// </summary>
internal sealed class FakeMutableDatasetCatalog : IMutableDatasetCatalog
{
    private readonly List<LoadedDataset> _datasets = [];

    public IReadOnlyList<LoadedDataset> Datasets => _datasets.ToList();

    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    /// <summary>Datasets the next <see cref="LoadAsync"/> call will add.</summary>
    public IReadOnlyList<LoadedDataset> NextLoad { get; set; } = [];

    /// <summary>The kind the next load reports.</summary>
    public DatasetSourceKind NextKind { get; set; } = DatasetSourceKind.File;

    /// <summary>The timed-out flag the next load reports.</summary>
    public bool NextTimedOut { get; set; }

    /// <summary>Number of <see cref="LoadAsync"/> calls observed.</summary>
    public int LoadCount { get; private set; }

    /// <summary>When set, the next <see cref="LoadAsync"/> throws it instead of loading.</summary>
    public Exception? NextLoadException { get; set; }

    public Task<DatasetLoadOutcome> LoadAsync(
        string path, string? specHint = null, CancellationToken cancellationToken = default)
    {
        LoadCount++;
        if (NextLoadException is { } ex)
        {
            throw ex;
        }
        _datasets.AddRange(NextLoad);
        var added = NextLoad.Select(d => d.Id).ToList();
        if (added.Count > 0)
        {
            Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
            {
                Kind = DatasetCatalogChangeKind.Batch,
            });
        }
        return Task.FromResult(new DatasetLoadOutcome(path, NextKind, added, NextTimedOut));
    }

    public bool Remove(DatasetId id)
    {
        var index = _datasets.FindIndex(d => d.Id.Equals(id));
        if (index < 0) return false;
        _datasets.RemoveAt(index);
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
        {
            Kind = DatasetCatalogChangeKind.Removed,
            DatasetId = id,
        });
        return true;
    }

    public int RemoveAll()
    {
        var count = _datasets.Count;
        _datasets.Clear();
        if (count > 0)
        {
            Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
            {
                Kind = DatasetCatalogChangeKind.Batch,
            });
        }
        return count;
    }

    /// <summary>Builds a minimal <see cref="LoadedDataset"/> for staging.</summary>
    public static LoadedDataset MakeDataset(string id, string spec = "S-101") =>
        new(
            new DatasetId(id),
            new SpecRef(spec, new SpecVersion(1, 0, 0)),
            LoadedDatasetFactory.Box(0, 0, 10, 10),
            null,
            new StubData());

    private sealed record StubData : LoadedDatasetData;
}
