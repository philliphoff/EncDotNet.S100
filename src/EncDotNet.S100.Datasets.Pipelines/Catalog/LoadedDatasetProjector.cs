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
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Pipelines;

namespace EncDotNet.S100.Datasets.Pipelines.Catalog;

/// <summary>
/// Projects the raw bytes of a single S-100 dataset — already detected as a
/// known product specification — into the typed <see cref="LoadedDataset"/>
/// carried by an <see cref="IDatasetCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is the one place the codebase maps a product-specification name to
/// the per-spec <c>Open</c> reader and the matching
/// <see cref="LoadedDatasetData"/> variant, so every host — the Avalonia
/// viewer (<c>ViewerDatasetCatalog</c>) and the headless
/// <see cref="FileDatasetCatalog"/> that backs the CLI <c>identify</c>
/// command — produces byte-identical catalog entries. See S-100 Edition
/// 5.2.1 Part 1 (product specifications) and the per-spec encoding parts
/// (10a ISO 8211 for S-101, 10b GML for the vector products, 10c HDF5 for
/// S-102 / S-104 / S-111).
/// </para>
/// <para>
/// The caller owns <paramref name="stream"/> and must dispose it; this
/// method reads it fully (coverage readers materialise every value into
/// managed arrays before returning, so the backing HDF5 handle is closed
/// eagerly here). A geometry-less container feature (e.g. an S-131 /
/// S-127 <c>Authority</c>) yields no extent, so the entry falls back to
/// world bounds.
/// </para>
/// </remarks>
public static class LoadedDatasetProjector
{
    /// <summary>Whole-world fallback extent for datasets with no resolvable bounds.</summary>
    public static readonly BoundingBox WorldBounds = new(-90, -180, 90, 180);

    /// <summary>
    /// Projects <paramref name="stream"/> into a <see cref="LoadedDataset"/>.
    /// </summary>
    /// <param name="id">Stable identifier for the dataset within the catalog session.</param>
    /// <param name="spec">
    /// The detected product specification name (e.g. <c>"S-101"</c>,
    /// <c>"S-102"</c>). The S-57 → S-101 mapping is applied by the caller;
    /// pass <c>"S-101"</c> (or <c>"S-57"</c>, treated identically) for a
    /// legacy cell.
    /// </param>
    /// <param name="stream">The dataset bytes; the caller owns and disposes it.</param>
    /// <param name="externalTextResolver">
    /// Optional file-name → text delegate for S-101 <c>fileReference</c> /
    /// <c>TXTDSC</c> / <c>NTXTDS</c> attributes (S-101 Feature Catalogue);
    /// <c>null</c> for loose cells or non-S-101 specs.
    /// </param>
    /// <param name="transforms">
    /// Optional CRS transform factory used to reproject a projected-CRS
    /// coverage extent (e.g. an S-102 tile in a UTM zone) into the WGS-84
    /// bounds that <see cref="LoadedDataset.Bounds"/> contractually carries.
    /// When <c>null</c> the native grid georeferencing is treated as
    /// geographic degrees (correct only for datasets already in EPSG:4326).
    /// </param>
    /// <returns>
    /// The projected dataset, or <c>null</c> when <paramref name="spec"/> is
    /// not a known S-100 product specification.
    /// </returns>
    public static LoadedDataset? Project(
        DatasetId id,
        string spec,
        Stream stream,
        Func<string, string?>? externalTextResolver = null,
        ICrsTransformFactory? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(stream);

        return spec switch
        {
            "S-101" or "S-57" => ProjectS101(id, stream, externalTextResolver),
            "S-102" => ProjectS102(id, stream, transforms),
            "S-104" => ProjectS104(id, stream),
            "S-111" => ProjectS111(id, stream),
            "S-122" => ProjectGml(id, "S-122", stream, s =>
            {
                var model = S122Dataset.Open(s);
                return (new S122DatasetData(model), model.ReadMetadata());
            }),
            "S-124" => ProjectGml(id, "S-124", stream, s =>
            {
                var model = S124Dataset.Open(s);
                return (new S124DatasetData(model), model.ReadMetadata());
            }),
            "S-125" => ProjectGml(id, "S-125", stream, s =>
            {
                var model = S125Dataset.Open(s);
                return (new S125DatasetData(model), model.ReadMetadata());
            }),
            "S-127" => ProjectGml(id, "S-127", stream, s =>
            {
                var model = S127Dataset.Open(s);
                return (new S127DatasetData(model), model.ReadMetadata());
            }),
            "S-128" => ProjectGml(id, "S-128", stream, s =>
            {
                var model = S128Dataset.Open(s);
                return (new S128DatasetData(model), model.ReadMetadata());
            }),
            "S-129" => ProjectGml(id, "S-129", stream, s =>
            {
                var model = S129Dataset.Open(s);
                return (new S129DatasetData(model), model.ReadMetadata());
            }),
            "S-131" => ProjectGml(id, "S-131", stream, s =>
            {
                var model = S131Dataset.Open(s);
                return (new S131DatasetData(model), model.ReadMetadata());
            }),
            "S-201" => ProjectGml(id, "S-201", stream, s =>
            {
                var model = S201Dataset.Open(s);
                return (new S201DatasetData(model), model.ReadMetadata());
            }),
            "S-411" => ProjectGml(id, "S-411", stream, s =>
            {
                var model = S411Dataset.Open(s);
                return (new S411DatasetData(model), model.ReadMetadata());
            }),
            "S-421" => ProjectGml(id, "S-421", stream, s =>
            {
                var model = S421Dataset.Open(s);
                return (new S421DatasetData(model), model.ReadMetadata());
            }),
            _ => null,
        };
    }

    private static LoadedDataset ProjectGml(
        DatasetId id,
        string specName,
        Stream stream,
        Func<Stream, (LoadedDatasetData Data, DatasetMetadata Metadata)> open)
    {
        var (data, metadata) = open(stream);
        return new LoadedDataset(
            id,
            new SpecRef(specName, metadata.Spec.Edition),
            metadata.Extent ?? WorldBounds,
            null,
            data);
    }

    private static LoadedDataset ProjectS101(
        DatasetId id,
        Stream stream,
        Func<string, string?>? externalTextResolver)
    {
        var dataset = S101Dataset.Open(stream);
        var metadata = dataset.ReadMetadata();
        return new LoadedDataset(
            id,
            metadata.Spec,
            metadata.Extent ?? WorldBounds,
            null,
            new S101DatasetData(dataset, externalTextResolver));
    }

    private static LoadedDataset ProjectS102(DatasetId id, Stream stream, ICrsTransformFactory? transforms)
    {
        using var file = PureHdfFile.Open(stream);
        var dataset = S102DatasetReader.Read(file);
        var source = new S102CoverageSource(dataset);
        // LoadedDataset.Bounds is contractually WGS-84; an S-102 tile may be
        // in a projected CRS (e.g. UTM zone 31N) whose grid georeferencing is
        // native metres, so reproject through CoverageExtent when a transform
        // factory is available rather than treating native origin/spacing as
        // degrees. Without a factory, fall back to the naive geographic box.
        var bounds = transforms is not null
            ? CoverageExtent.ToWgs84Bounds(source.Metadata, transforms) ?? WorldBounds
            : ComputeS102Bounds(dataset) ?? WorldBounds;
        return new LoadedDataset(
            id,
            new SpecRef("S-102", default),
            bounds,
            null,
            new S102CoverageData(source));
    }

    private static LoadedDataset ProjectS104(DatasetId id, Stream stream)
    {
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
                ComputeStationBounds(s.Dataset.Stations, x => x.Latitude, x => x.Longitude) ?? WorldBounds,
                ComputeTimeRange(s.Dataset.MinTime, s.Dataset.MaxTime, s.Dataset.Stations.Count),
                new S104StationSeriesData(s.Dataset)),
            _ => throw new InvalidOperationException(
                $"Unexpected S-104 dataset variant {data.GetType().Name}."),
        };
    }

    private static LoadedDataset ProjectS111(DatasetId id, Stream stream)
    {
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
                ComputeStationBounds(s.Dataset.Stations, x => x.Latitude, x => x.Longitude) ?? WorldBounds,
                ComputeTimeRange(s.Dataset.MinTime, s.Dataset.MaxTime, s.Dataset.Stations.Count),
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

    private static BoundingBox? ComputeStationBounds<TStation>(
        IReadOnlyList<TStation> stations,
        Func<TStation, double> latitude,
        Func<TStation, double> longitude)
    {
        if (stations.Count == 0) return null;
        double south = double.PositiveInfinity, west = double.PositiveInfinity;
        double north = double.NegativeInfinity, east = double.NegativeInfinity;
        foreach (var s in stations)
        {
            var lat = latitude(s);
            var lon = longitude(s);
            if (lat < south) south = lat;
            if (lat > north) north = lat;
            if (lon < west) west = lon;
            if (lon > east) east = lon;
        }

        // A single station yields a zero-extent box; pad slightly so a host
        // can frame it.
        if (Math.Abs(north - south) < 1e-9) { south -= 0.01; north += 0.01; }
        if (Math.Abs(east - west) < 1e-9) { west -= 0.01; east += 0.01; }
        return new BoundingBox(south, west, north, east);
    }

    private static TimeRange? ComputeTimeRange(DateTime? min, DateTime? max, int count)
    {
        if (count == 0 || min is null || max is null) return null;
        var start = new DateTimeOffset(DateTime.SpecifyKind(min.Value, DateTimeKind.Utc));
        var end = new DateTimeOffset(DateTime.SpecifyKind(max.Value, DateTimeKind.Utc));
        return new TimeRange(start, end);
    }
}
