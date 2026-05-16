using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S201;
using EncDotNet.S100.Datasets.S411;
using EncDotNet.S100.Datasets.S421;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Mcp.Tools.Catalog;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Viewer.ViewModels;
using IDatasetCatalog = EncDotNet.S100.Mcp.Tools.Catalog.IDatasetCatalog;

namespace EncDotNet.S100.Viewer.Services;

/// <summary>
/// Adapts the viewer's <see cref="IDatasetLoaderService"/> to the MCP
/// <see cref="IDatasetCatalog"/> contract, exposing each successfully
/// loaded dataset as a typed <see cref="LoadedDataset"/> for the
/// MCP tool surface.
/// </summary>
/// <remarks>
/// <para>
/// The viewer's loader keeps spec-specific <c>IDatasetProcessor</c>s
/// privately. Rather than widen that contract, this adapter re-opens
/// each loaded dataset via the per-spec <c>Open(string)</c> helpers
/// so the catalog snapshot is fully self-contained — read-only and
/// independent of the loader's rendering state. Re-opening doubles
/// memory for in-process datasets, which is acceptable for an
/// off-by-default tool surface.
/// </para>
/// <para>
/// Entries are cached by <see cref="DatasetEntry"/> identity so each
/// file is parsed only once per dataset lifetime. Cache entries are
/// evicted on <see cref="IDatasetLoaderService.DatasetRemoved"/>.
/// </para>
/// <para>
/// Exchange-set entries (where <see cref="DatasetEntry.Source"/> is
/// non-null) and specs without a path-based <c>Open</c> helper are
/// silently skipped — the MCP surface only ever sees datasets it can
/// fully model.
/// </para>
/// </remarks>
internal sealed class ViewerDatasetCatalog : IDatasetCatalog, IDisposable
{
    private static readonly BoundingBox WorldBounds = new(-90, -180, 90, 180);

    private readonly IDatasetLoaderService _loader;
    private readonly Dictionary<DatasetEntry, LoadedDataset> _cache = new();
    private readonly object _gate = new();
    private ImmutableArray<LoadedDataset> _snapshot = ImmutableArray<LoadedDataset>.Empty;
    private bool _disposed;

    public ViewerDatasetCatalog(IDatasetLoaderService loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        _loader.DatasetLoaded += OnDatasetLoaded;
        _loader.DatasetRemoved += OnDatasetRemoved;
    }

    /// <inheritdoc />
    public ImmutableArray<LoadedDataset> Datasets => _snapshot;

    /// <inheritdoc />
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    private void OnDatasetLoaded(DatasetEntry entry)
    {
        if (_disposed) return;

        // Only ingest plain on-disk entries; exchange-set bytes live
        // inside an IAssetSource the catalog has no contract for.
        if (entry.IsFromExchangeSet) return;
        if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath)) return;

        LoadedDataset? projected;
        try
        {
            projected = TryProject(entry);
        }
        catch
        {
            // A malformed dataset shouldn't poison the catalog. The
            // viewer will already have surfaced the load error via its
            // own diagnostics; we just skip it here.
            return;
        }

        if (projected is null) return;

        ImmutableArray<LoadedDataset> next;
        lock (_gate)
        {
            _cache[entry] = projected;
            next = ImmutableArray.CreateRange(_cache.Values);
            _snapshot = next;
        }
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
        {
            Kind = DatasetCatalogChangeKind.Added,
            DatasetId = projected.Id,
        });
    }

    private void OnDatasetRemoved(DatasetEntry entry)
    {
        if (_disposed) return;

        DatasetId? removedId = null;
        lock (_gate)
        {
            if (!_cache.TryGetValue(entry, out var prev)) return;
            removedId = prev.Id;
            _cache.Remove(entry);
            _snapshot = ImmutableArray.CreateRange(_cache.Values);
        }
        Changed?.Invoke(this, new DatasetCatalogChangedEventArgs
        {
            Kind = DatasetCatalogChangeKind.Removed,
            DatasetId = removedId,
        });
    }

    private static LoadedDataset? TryProject(DatasetEntry entry)
    {
        var id = new DatasetId(entry.DisplayName);
        var spec = entry.ProductSpec;
        var path = entry.FilePath;

        return spec switch
        {
            "S-102" => ProjectS102(id, path),
            "S-122" => Project(id, "S-122", path, p => new S122DatasetData(S122Dataset.Open(p))),
            "S-124" => Project(id, "S-124", path, p => new S124DatasetData(S124Dataset.Open(p))),
            "S-125" => Project(id, "S-125", path, p => new S125DatasetData(S125Dataset.Open(p))),
            "S-127" => Project(id, "S-127", path, p => new S127DatasetData(S127Dataset.Open(p))),
            "S-128" => Project(id, "S-128", path, p => new S128DatasetData(S128Dataset.Open(p))),
            "S-129" => Project(id, "S-129", path, p => new S129DatasetData(S129Dataset.Open(p))),
            "S-201" => Project(id, "S-201", path, p => new S201DatasetData(S201Dataset.Open(p))),
            "S-411" => Project(id, "S-411", path, p => new S411DatasetData(S411Dataset.Open(p))),
            "S-421" => Project(id, "S-421", path, p => new S421DatasetData(S421Dataset.Open(p))),
            _ => null,
        };
    }

    private static LoadedDataset Project(
        DatasetId id,
        string specName,
        string path,
        Func<string, LoadedDatasetData> openData)
    {
        var data = openData(path);
        return new LoadedDataset(
            id,
            new SpecRef(specName, default),
            WorldBounds,
            null,
            data);
    }

    private static LoadedDataset? ProjectS102(DatasetId id, string path)
    {
        using var file = PureHdfFile.Open(path);
        var dataset = S102DatasetReader.Read(file);
        var source = new S102CoverageSource(dataset);
        var bounds = ComputeS102Bounds(dataset) ?? WorldBounds;
        return new LoadedDataset(
            id,
            new SpecRef("S-102", default),
            bounds,
            null,
            new S102CoverageData(source));
    }

    private static BoundingBox? ComputeS102Bounds(S102Dataset dataset)
    {
        if (dataset.Coverages is null || dataset.Coverages.Count == 0) return null;
        var cov = dataset.Coverages[0];
        if (cov.NumPointsLatitudinal <= 0 || cov.NumPointsLongitudinal <= 0) return null;

        var south = cov.OriginLatitude;
        var west = cov.OriginLongitude;
        var north = cov.OriginLatitude + (cov.NumPointsLatitudinal - 1) * cov.SpacingLatitudinal;
        var east = cov.OriginLongitude + (cov.NumPointsLongitudinal - 1) * cov.SpacingLongitudinal;
        return new BoundingBox(south, west, north, east);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loader.DatasetLoaded -= OnDatasetLoaded;
        _loader.DatasetRemoved -= OnDatasetRemoved;
        lock (_gate)
        {
            _cache.Clear();
            _snapshot = ImmutableArray<LoadedDataset>.Empty;
        }
    }
}
