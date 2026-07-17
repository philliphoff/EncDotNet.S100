using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.S101;
using EncDotNet.S100.Datasets.S102;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Datasets.S122;
using EncDotNet.S100.Datasets.S124;
using EncDotNet.S100.Datasets.S125;
using EncDotNet.S100.Datasets.S127;
using EncDotNet.S100.Datasets.S128;
using EncDotNet.S100.Datasets.S129;
using EncDotNet.S100.Datasets.S131;
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
    private IReadOnlyList<LoadedDataset> _snapshot = [];
    private bool _disposed;

    public ViewerDatasetCatalog(IDatasetLoaderService loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        _loader.DatasetLoaded += OnDatasetLoaded;
        _loader.DatasetRemoved += OnDatasetRemoved;
    }

    /// <inheritdoc />
    public IReadOnlyList<LoadedDataset> Datasets => _snapshot;

    /// <inheritdoc />
    public event EventHandler<DatasetCatalogChangedEventArgs>? Changed;

    private void OnDatasetLoaded(DatasetEntry entry)
    {
        if (_disposed) return;

        // Plain on-disk entries: require the file to be present so any
        // downstream consumer that DOES need a path can find one.
        // Exchange-set entries instead carry an IAssetSource +
        // RelativePath and are read via streams further down — no on-disk
        // path is required.
        if (!entry.IsFromExchangeSet
            && (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath)))
        {
            return;
        }

        if (entry.IsFromExchangeSet
            && (entry.Source is null || string.IsNullOrEmpty(entry.RelativePath)))
        {
            return;
        }

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

        IReadOnlyList<LoadedDataset> next;
        lock (_gate)
        {
            _cache[entry] = projected;
            next = _cache.Values.ToArray();
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
            _snapshot = _cache.Values.ToArray();
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

        // DatasetPipelineFactory.DetectProductSpec returns the literal
        // string "S-57" for ENC .000 files that pass the S-57 DSPM
        // discriminator (see DatasetPipelineFactory.cs:94) and "S-101"
        // for everything else with that extension. The MCP surface
        // treats both as S-101 — the S-57 → S-101 adapter is what the
        // viewer's render pipeline ultimately uses for portrayal, so
        // exposing them under a single canonical spec name keeps the
        // tool surface predictable. The catalog itself does not need
        // to differentiate.
        return spec switch
        {
            "S-101" or "S-57" => ProjectS101(id, entry),
            "S-102" => ProjectS102(id, entry),
            "S-104" => ProjectS104(id, entry),
            "S-111" => ProjectS111(id, entry),
            "S-122" => ProjectGml(id, "S-122", entry, stream =>
            {
                var model = S122Dataset.Open(stream);
                return (new S122DatasetData(model), model.ReadMetadata());
            }),
            "S-124" => ProjectGml(id, "S-124", entry, stream =>
            {
                var model = S124Dataset.Open(stream);
                return (new S124DatasetData(model), model.ReadMetadata());
            }),
            "S-125" => ProjectGml(id, "S-125", entry, stream =>
            {
                var model = S125Dataset.Open(stream);
                return (new S125DatasetData(model), model.ReadMetadata());
            }),
            "S-127" => ProjectGml(id, "S-127", entry, stream =>
            {
                var model = S127Dataset.Open(stream);
                return (new S127DatasetData(model), model.ReadMetadata());
            }),
            "S-128" => ProjectGml(id, "S-128", entry, stream =>
            {
                var model = S128Dataset.Open(stream);
                return (new S128DatasetData(model), model.ReadMetadata());
            }),
            "S-129" => ProjectGml(id, "S-129", entry, stream =>
            {
                var model = S129Dataset.Open(stream);
                return (new S129DatasetData(model), model.ReadMetadata());
            }),
            "S-131" => ProjectGml(id, "S-131", entry, stream =>
            {
                var model = S131Dataset.Open(stream);
                return (new S131DatasetData(model), model.ReadMetadata());
            }),
            "S-201" => ProjectGml(id, "S-201", entry, stream =>
            {
                var model = S201Dataset.Open(stream);
                return (new S201DatasetData(model), model.ReadMetadata());
            }),
            "S-411" => ProjectGml(id, "S-411", entry, stream =>
            {
                var model = S411Dataset.Open(stream);
                return (new S411DatasetData(model), model.ReadMetadata());
            }),
            "S-421" => ProjectGml(id, "S-421", entry, stream =>
            {
                var model = S421Dataset.Open(stream);
                return (new S421DatasetData(model), model.ReadMetadata());
            }),
            _ => null,
        };
    }

    /// <summary>
    /// <summary>
    /// Opens the dataset bytes for <paramref name="entry"/> — either from
    /// disk (plain entry) or from its <see cref="DatasetEntry.Source"/>
    /// asset source (exchange-set entry). The returned stream must be
    /// disposed by the caller.
    /// </summary>
    private static Stream OpenEntryStream(DatasetEntry entry)
    {
        if (entry.IsFromExchangeSet)
        {
            // SYNC BRIDGE: IAssetSource.OpenAsync for FileSystem / Zip
            // backings is effectively synchronous (no real I/O latency),
            // and TryProject is called from the synchronous DatasetLoaded
            // event chain. Async-up-the-stack would require flipping the
            // event signature, which is not justified for a one-shot open.
            return entry.Source!.OpenAsync(entry.RelativePath!).GetAwaiter().GetResult();
        }
        return File.OpenRead(entry.FilePath);
    }

    private static LoadedDataset ProjectGml(
        DatasetId id,
        string specName,
        DatasetEntry entry,
        Func<Stream, (LoadedDatasetData Data, DatasetMetadata Metadata)> open)
    {
        using var stream = OpenEntryStream(entry);
        var (data, metadata) = open(stream);
        // Canonical metadata derived from the parsed features
        // (GmlDatasetMetadata via the dataset's ReadMetadata): the declared
        // product edition and the raw WGS-84 envelope, replacing the former
        // hand-rolled bounds walk (issue #467 WS1). The catalog keeps its own
        // canonical spec name (the S-57 → S-101 mapping is applied upstream in
        // TryProject) and only adopts the edition. A geometry-less container
        // feature (e.g. S-131 / S-127 Authority) yields a null extent, so the
        // caller falls back to world bounds.
        return new LoadedDataset(
            id,
            new SpecRef(specName, metadata.Spec.Edition),
            metadata.Extent ?? WorldBounds,
            null,
            data);
    }

    private static LoadedDataset ProjectS101(DatasetId id, DatasetEntry entry)
    {
        using var stream = OpenEntryStream(entry);
        var dataset = S101Dataset.Open(stream);
        // Canonical metadata derived from the already-parsed cell (issue #467
        // WS1): the declared product-specification edition (DSID/PRED subfield,
        // S-100 Part 10a §4.3.1) and the WGS-84 extent recovered from the
        // vector source, which joins feature/spatial/coordinate records and
        // applies the S-100 Part 10a coordinate multiplication factors to
        // yield decimal degrees (the same EPSG:4326 extent the renderer fits).
        // Falls back to world bounds when the cell carries no resolvable
        // coordinates.
        var metadata = dataset.ReadMetadata();
        return new LoadedDataset(
            id,
            metadata.Spec,
            metadata.Extent ?? WorldBounds,
            null,
            new S101DatasetData(dataset, BuildExternalTextResolver(entry)));
    }

    /// <summary>
    /// Builds a file-name → text resolver for an S-101 cell's
    /// <c>fileReference</c> attributes (S-101 Feature Catalogue, aliases
    /// <c>TXTDSC</c> / <c>NTXTDS</c>) when the cell was loaded from an
    /// exchange set, so MCP consumers (<c>identify_features</c> /
    /// <c>pick_features</c>) can surface the referenced text. Returns
    /// <c>null</c> for loose cells with no asset source.
    /// </summary>
    private static Func<string, string?>? BuildExternalTextResolver(DatasetEntry entry)
    {
        if (!entry.IsFromExchangeSet || entry.Source is null)
            return null;

        return new ExternalTextFileResolver(entry.Source, entry.RelativePath).AsDelegate();
    }

    private static LoadedDataset ProjectS102(DatasetId id, DatasetEntry entry)
    {
        // S102DatasetReader.Read fully materialises every coverage's
        // values into managed BathymetryValue[] arrays before
        // returning (see S102DatasetReader.ReadCoverage), so the
        // backing HDF5 file (and its stream) can be closed immediately.
        using var stream = OpenEntryStream(entry);
        using var file = PureHdfFile.Open(stream);
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

    private static LoadedDataset ProjectS104(DatasetId id, DatasetEntry entry)
    {
        // S104DatasetReader.ReadAny materialises every time-step's value
        // grid (or per-station series) into managed arrays before
        // returning, so the file handle can be disposed eagerly.
        using var stream = OpenEntryStream(entry);
        using var file = PureHdfFile.Open(stream);
        var data = S104DatasetReader.ReadAny(file);
        return data switch
        {
            S104DatasetData.GriddedCoverage g => new LoadedDataset(
                id,
                new SpecRef("S-104", default),
                ComputeS104Bounds(g.Dataset) ?? WorldBounds,
                null,
                new S104CoverageData(new S104CoverageSource(g.Dataset))),
            S104DatasetData.StationSeries s => new LoadedDataset(
                id,
                new SpecRef("S-104", default),
                ComputeS104StationSeriesBounds(s.Dataset) ?? WorldBounds,
                ComputeS104StationSeriesTimeRange(s.Dataset),
                new S104StationSeriesData(s.Dataset)),
            _ => throw new InvalidOperationException(
                $"Unexpected S-104 dataset variant {data.GetType().Name}."),
        };
    }

    private static LoadedDataset ProjectS111(DatasetId id, DatasetEntry entry)
    {
        // S111DatasetReader.ReadAny materialises every time-step's value
        // grid (or per-station series) into managed arrays before
        // returning, so the file handle can be disposed eagerly.
        using var stream = OpenEntryStream(entry);
        using var file = PureHdfFile.Open(stream);
        var data = S111DatasetReader.ReadAny(file);
        return data switch
        {
            S111DatasetData.GriddedCoverage g => new LoadedDataset(
                id,
                new SpecRef("S-111", default),
                ComputeS111Bounds(g.Dataset) ?? WorldBounds,
                null,
                new S111CoverageData(new S111CoverageSource(g.Dataset))),
            S111DatasetData.StationSeries s => new LoadedDataset(
                id,
                new SpecRef("S-111", default),
                ComputeS111StationSeriesBounds(s.Dataset) ?? WorldBounds,
                ComputeS111StationSeriesTimeRange(s.Dataset),
                new S111StationSeriesData(s.Dataset)),
            _ => throw new InvalidOperationException(
                $"Unexpected S-111 dataset variant {data.GetType().Name}."),
        };
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

    private static BoundingBox? ComputeS104Bounds(S104Dataset dataset)
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

    /// <summary>
    /// Bounding box covering all stations in an S-104 dcf8 dataset.
    /// Returns <c>null</c> for an empty station set (caller falls back to
    /// <see cref="WorldBounds"/>). See S-104 Edition 2.0.0 §10.2.3.
    /// </summary>
    private static BoundingBox? ComputeS104StationSeriesBounds(S104StationSeriesDataset dataset)
    {
        if (dataset.Stations.Count == 0) return null;
        double south = double.PositiveInfinity, west = double.PositiveInfinity;
        double north = double.NegativeInfinity, east = double.NegativeInfinity;
        foreach (var s in dataset.Stations)
        {
            if (s.Latitude < south) south = s.Latitude;
            if (s.Latitude > north) north = s.Latitude;
            if (s.Longitude < west) west = s.Longitude;
            if (s.Longitude > east) east = s.Longitude;
        }
        // A single station yields a zero-extent box; pad slightly so the
        // viewer can zoom to it.
        if (Math.Abs(north - south) < 1e-9) { south -= 0.01; north += 0.01; }
        if (Math.Abs(east - west) < 1e-9) { west -= 0.01; east += 0.01; }
        return new BoundingBox(south, west, north, east);
    }

    private static TimeRange? ComputeS104StationSeriesTimeRange(S104StationSeriesDataset dataset)
    {
        if (dataset.Stations.Count == 0 || dataset.MinTime is null || dataset.MaxTime is null) return null;
        var start = new DateTimeOffset(DateTime.SpecifyKind(dataset.MinTime.Value, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(dataset.MaxTime.Value, DateTimeKind.Utc));
        return new TimeRange(start, end);
    }

    private static BoundingBox? ComputeS111Bounds(S111Dataset dataset)
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

    /// <summary>
    /// Bounding box covering all stations in an S-111 dcf8 dataset.
    /// Returns <c>null</c> for an empty station set (caller falls back to
    /// <see cref="WorldBounds"/>). See S-111 Edition 2.0.0 §10.2.3.
    /// </summary>
    private static BoundingBox? ComputeS111StationSeriesBounds(S111StationSeriesDataset dataset)
    {
        if (dataset.Stations.Count == 0) return null;
        double south = double.PositiveInfinity, west = double.PositiveInfinity;
        double north = double.NegativeInfinity, east = double.NegativeInfinity;
        foreach (var s in dataset.Stations)
        {
            if (s.Latitude < south) south = s.Latitude;
            if (s.Latitude > north) north = s.Latitude;
            if (s.Longitude < west) west = s.Longitude;
            if (s.Longitude > east) east = s.Longitude;
        }
        if (Math.Abs(north - south) < 1e-9) { south -= 0.01; north += 0.01; }
        if (Math.Abs(east - west) < 1e-9) { west -= 0.01; east += 0.01; }
        return new BoundingBox(south, west, north, east);
    }

    private static TimeRange? ComputeS111StationSeriesTimeRange(S111StationSeriesDataset dataset)
    {
        if (dataset.Stations.Count == 0 || dataset.MinTime is null || dataset.MaxTime is null) return null;
        var start = new DateTimeOffset(DateTime.SpecifyKind(dataset.MinTime.Value, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(dataset.MaxTime.Value, DateTimeKind.Utc));
        return new TimeRange(start, end);
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
            _snapshot = [];
        }
    }
}
