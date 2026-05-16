using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using EncDotNet.S100.Core;
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
using EncDotNet.S100.Gml;
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
                return (new S122DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-124" => ProjectGml(id, "S-124", entry, stream =>
            {
                var model = S124Dataset.Open(stream);
                return (new S124DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-125" => ProjectGml(id, "S-125", entry, stream =>
            {
                var model = S125Dataset.Open(stream);
                return (new S125DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-127" => ProjectGml(id, "S-127", entry, stream =>
            {
                var model = S127Dataset.Open(stream);
                return (new S127DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-128" => ProjectGml(id, "S-128", entry, stream =>
            {
                var model = S128Dataset.Open(stream);
                return (new S128DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-129" => ProjectGml(id, "S-129", entry, stream =>
            {
                var model = S129Dataset.Open(stream);
                return (new S129DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-131" => ProjectGml(id, "S-131", entry, stream =>
            {
                var model = S131Dataset.Open(stream);
                return (new S131DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-201" => ProjectGml(id, "S-201", entry, stream =>
            {
                var model = S201Dataset.Open(stream);
                return (new S201DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-411" => ProjectGml(id, "S-411", entry, stream =>
            {
                var model = S411Dataset.Open(stream);
                return (new S411DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            "S-421" => ProjectGml(id, "S-421", entry, stream =>
            {
                var model = S421Dataset.Open(stream);
                return (new S421DatasetData(model), ComputeGmlBounds(model.Features));
            }),
            _ => null,
        };
    }

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
            // IAssetSource.OpenAsync is effectively synchronous for the
            // FileSystem / Zip backings used by the viewer (see
            // ZipAssetSource.OpenAsync), so blocking here is benign and
            // keeps TryProject synchronous like the rest of the catalog
            // event chain.
            return entry.Source!.OpenAsync(entry.RelativePath!).GetAwaiter().GetResult();
        }
        return File.OpenRead(entry.FilePath);
    }

    private static LoadedDataset ProjectGml(
        DatasetId id,
        string specName,
        DatasetEntry entry,
        Func<Stream, (LoadedDatasetData Data, BoundingBox? Bounds)> open)
    {
        using var stream = OpenEntryStream(entry);
        var (data, bounds) = open(stream);
        return new LoadedDataset(
            id,
            new SpecRef(specName, default),
            bounds ?? WorldBounds,
            null,
            data);
    }

    private static LoadedDataset ProjectS101(DatasetId id, DatasetEntry entry)
    {
        using var stream = OpenEntryStream(entry);
        var dataset = S101Dataset.Open(stream);
        // S-101 features carry packed spatial coordinates that require
        // the coordinate multiplication factors plus a join across the
        // feature / spatial / coordinate records to recover lat/lon —
        // see S-100 Part 10a §3. That work belongs with the S-101
        // describe implementation, so we fall back to world bounds for
        // now; the MCP tool surface still lists the dataset and other
        // tools can dispatch on it.
        return new LoadedDataset(
            id,
            new SpecRef("S-101", default),
            WorldBounds,
            null,
            new S101DatasetData(dataset));
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

    /// <summary>
    /// Computes a lat/lon bounding box covering every coordinate
    /// referenced by the supplied GML features (points, curves, ring
    /// vertices). Returns <c>null</c> when no feature carries any
    /// geometry — container-style features such as S-131
    /// <c>Authority</c> or S-127 <c>Authority</c> are valid in their
    /// respective product specs but produce no bounds, in which case
    /// callers fall back to <see cref="WorldBounds"/>.
    /// </summary>
    private static BoundingBox? ComputeGmlBounds<TFeature>(IEnumerable<TFeature> features)
        where TFeature : IGmlFeature
    {
        if (features is null) return null;

        double minLat = double.PositiveInfinity, maxLat = double.NegativeInfinity;
        double minLon = double.PositiveInfinity, maxLon = double.NegativeInfinity;
        bool any = false;

        void Expand(double lat, double lon)
        {
            any = true;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;
            if (lon < minLon) minLon = lon;
            if (lon > maxLon) maxLon = lon;
        }

        foreach (var feature in features)
        {
            if (feature is null) continue;
            if (!feature.Points.IsDefaultOrEmpty)
            {
                foreach (var (lat, lon) in feature.Points) Expand(lat, lon);
            }
            if (!feature.Curves.IsDefaultOrEmpty)
            {
                foreach (var curve in feature.Curves)
                {
                    if (curve.IsDefaultOrEmpty) continue;
                    foreach (var (lat, lon) in curve) Expand(lat, lon);
                }
            }
            if (!feature.ExteriorRing.IsDefaultOrEmpty)
            {
                foreach (var (lat, lon) in feature.ExteriorRing) Expand(lat, lon);
            }
        }

        if (!any) return null;
        return new BoundingBox(minLat, minLon, maxLat, maxLon);
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
