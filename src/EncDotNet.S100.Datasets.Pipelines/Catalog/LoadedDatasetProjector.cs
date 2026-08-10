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
/// Projects a single S-100 dataset into the typed <see cref="LoadedDataset"/>
/// carried by an <see cref="IDatasetCatalog"/>.
/// </summary>
/// <remarks>
/// <para>
/// There are two ways in: from the raw <see cref="Stream"/> of a dataset already
/// detected as a known product specification
/// (<see cref="Project(DatasetId, string, Stream, System.Func{string, string?}?, ICrsTransformFactory?)"/>),
/// used by the loose-file / exchange-set catalogues; and from an
/// <em>already-parsed</em> <see cref="IDatasetProcessor"/>
/// (<see cref="Project(DatasetId, IDatasetProcessor, System.Func{string, string?}?, ICrsTransformFactory?)"/>),
/// used by hosts that keep a resident render processor and want to avoid parsing
/// the dataset a second time (see issue #566). Both funnel through the single
/// <c>BuildFromData</c> switch below, so a processor-projected
/// <see cref="LoadedDataset"/> is byte-for-byte identical to a stream-projected
/// one: the two entry points differ only in how they obtain the
/// <see cref="LoadedDatasetData"/> payload — by opening a stream, or by asking
/// the processor for the model it already holds.
/// </para>
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
/// The caller owns the <see cref="Stream"/> overload's stream and must dispose
/// it; this method reads it fully (coverage readers materialise every value into
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

        var data = OpenData(spec, stream, externalTextResolver);
        return data is null ? null : BuildFromData(id, data, transforms);
    }

    /// <summary>
    /// Projects the <em>already-parsed</em> model held by
    /// <paramref name="processor"/> into a <see cref="LoadedDataset"/>, without
    /// re-reading the dataset bytes. Lets a host that keeps a resident render
    /// processor share one parse between the query and render paths (issue #566).
    /// </summary>
    /// <param name="id">Stable identifier for the dataset within the catalog session.</param>
    /// <param name="processor">The resident processor to project from.</param>
    /// <param name="externalTextResolver">
    /// Optional S-101 external-text resolver to weave into the payload when the
    /// processor did not already carry one (e.g. an exchange-set support-file
    /// resolver the host knows but the loose-file processor does not).
    /// </param>
    /// <param name="transforms">CRS transform factory for projected coverage extents; see the stream overload.</param>
    /// <returns>
    /// The projected dataset, or <c>null</c> when the processor cannot yield a
    /// catalog payload (does not implement <see cref="ILoadedDatasetProjection"/>).
    /// </returns>
    public static LoadedDataset? Project(
        DatasetId id,
        IDatasetProcessor processor,
        Func<string, string?>? externalTextResolver = null,
        ICrsTransformFactory? transforms = null)
    {
        ArgumentNullException.ThrowIfNull(processor);

        if (processor is not ILoadedDatasetProjection projectable)
            return null;

        var data = projectable.CreateLoadedData();

        // The processor may not know an exchange-set external-text resolver; weave
        // it into the S-101 payload here, where the host supplies it, mirroring the
        // stream projection's resolver argument.
        if (externalTextResolver is not null
            && data is S101DatasetData { ExternalTextResolver: null } s101)
        {
            data = s101 with { ExternalTextResolver = externalTextResolver };
        }

        return BuildFromData(id, data, transforms);
    }

    /// <summary>
    /// Opens <paramref name="stream"/> into the <see cref="LoadedDatasetData"/>
    /// payload for <paramref name="spec"/>, or <c>null</c> for an unknown spec.
    /// This is the only per-spec <c>Open</c> switch; bounds, temporal coverage,
    /// and the declared specification are derived from the payload by
    /// <see cref="BuildFromData"/>.
    /// </summary>
    private static LoadedDatasetData? OpenData(
        string spec, Stream stream, Func<string, string?>? externalTextResolver)
        => spec switch
        {
            "S-101" or "S-57" => new S101DatasetData(S101Dataset.Open(stream), externalTextResolver),
            "S-102" => OpenS102(stream),
            "S-104" => OpenS104(stream),
            "S-111" => OpenS111(stream),
            "S-122" => new S122DatasetData(S122Dataset.Open(stream)),
            "S-124" => new S124DatasetData(S124Dataset.Open(stream)),
            "S-125" => new S125DatasetData(S125Dataset.Open(stream)),
            "S-127" => new S127DatasetData(S127Dataset.Open(stream)),
            "S-128" => new S128DatasetData(S128Dataset.Open(stream)),
            "S-129" => new S129DatasetData(S129Dataset.Open(stream)),
            "S-131" => new S131DatasetData(S131Dataset.Open(stream)),
            "S-201" => new S201DatasetData(S201Dataset.Open(stream)),
            "S-411" => new S411DatasetData(S411Dataset.Open(stream)),
            "S-421" => new S421DatasetData(S421Dataset.Open(stream)),
            _ => null,
        };

    private static LoadedDatasetData OpenS102(Stream stream)
    {
        using var file = PureHdfFile.Open(stream);
        var dataset = S102DatasetReader.Read(file);
        return new S102CoverageData(new S102CoverageSource(dataset));
    }

    private static LoadedDatasetData OpenS104(Stream stream)
    {
        using var file = PureHdfFile.Open(stream);
        var data = S104DatasetReader.ReadAny(file);
        return data switch
        {
            S104DatasetData.GriddedCoverage g => new S104CoverageData(new S104CoverageSource(g.Dataset)),
            S104DatasetData.StationSeries s => new S104StationSeriesData(s.Dataset),
            _ => throw new InvalidOperationException(
                $"Unexpected S-104 dataset variant {data.GetType().Name}."),
        };
    }

    private static LoadedDatasetData OpenS111(Stream stream)
    {
        using var file = PureHdfFile.Open(stream);
        var data = S111DatasetReader.ReadAny(file);
        return data switch
        {
            S111DatasetData.GriddedCoverage g => new S111CoverageData(new S111CoverageSource(g.Dataset)),
            S111DatasetData.StationSeries s => new S111StationSeriesData(s.Dataset),
            _ => throw new InvalidOperationException(
                $"Unexpected S-111 dataset variant {data.GetType().Name}."),
        };
    }

    /// <summary>
    /// Builds the <see cref="LoadedDataset"/> from an opened
    /// <see cref="LoadedDatasetData"/> payload — the single place that derives
    /// the declared specification, geographic bounds, and temporal coverage. Both
    /// projection entry points funnel through here so their output is identical.
    /// </summary>
    private static LoadedDataset BuildFromData(
        DatasetId id, LoadedDatasetData data, ICrsTransformFactory? transforms)
        => data switch
        {
            S101DatasetData d => Vector(id, data, d.Dataset.ReadMetadata()),
            S122DatasetData d => Gml(id, "S-122", d.Model.ReadMetadata(), data),
            S124DatasetData d => Gml(id, "S-124", d.Model.ReadMetadata(), data),
            S125DatasetData d => Gml(id, "S-125", d.Model.ReadMetadata(), data),
            S127DatasetData d => Gml(id, "S-127", d.Model.ReadMetadata(), data),
            S128DatasetData d => Gml(id, "S-128", d.Model.ReadMetadata(), data),
            S129DatasetData d => Gml(id, "S-129", d.Model.ReadMetadata(), data),
            S131DatasetData d => Gml(id, "S-131", d.Model.ReadMetadata(), data),
            S201DatasetData d => Gml(id, "S-201", d.Model.ReadMetadata(), data),
            S411DatasetData d => Gml(id, "S-411", d.Model.ReadMetadata(), data),
            S421DatasetData d => Gml(id, "S-421", d.Model.ReadMetadata(), data),
            S102CoverageData d => new LoadedDataset(
                id, new SpecRef("S-102", default), ResolveS102Bounds(d.Source, transforms), null, data),
            S104CoverageData d => new LoadedDataset(
                id, new SpecRef("S-104", default), ComputeS104Bounds(d.Source.Dataset) ?? WorldBounds, null, data),
            S104StationSeriesData d => new LoadedDataset(
                id,
                new SpecRef("S-104", default),
                ComputeStationBounds(d.Dataset.Stations, x => x.Latitude, x => x.Longitude) ?? WorldBounds,
                ComputeTimeRange(d.Dataset.MinTime, d.Dataset.MaxTime, d.Dataset.Stations.Count),
                data),
            S111CoverageData d => new LoadedDataset(
                id, new SpecRef("S-111", default), ComputeS111Bounds(d.Source.Dataset) ?? WorldBounds, null, data),
            S111StationSeriesData d => new LoadedDataset(
                id,
                new SpecRef("S-111", default),
                ComputeStationBounds(d.Dataset.Stations, x => x.Latitude, x => x.Longitude) ?? WorldBounds,
                ComputeTimeRange(d.Dataset.MinTime, d.Dataset.MaxTime, d.Dataset.Stations.Count),
                data),
            _ => throw new InvalidOperationException(
                $"No catalog projection for payload {data.GetType().Name}."),
        };

    private static LoadedDataset Vector(
        DatasetId id, LoadedDatasetData data, DatasetMetadata metadata)
        => new(id, metadata.Spec, metadata.Extent ?? WorldBounds, null, data);

    private static LoadedDataset Gml(
        DatasetId id, string specName, DatasetMetadata metadata, LoadedDatasetData data)
        => new(id, new SpecRef(specName, metadata.Spec.Edition), metadata.Extent ?? WorldBounds, null, data);

    private static BoundingBox ResolveS102Bounds(S102CoverageSource source, ICrsTransformFactory? transforms)
    {
        // LoadedDataset.Bounds is contractually WGS-84; an S-102 tile may be
        // in a projected CRS (e.g. UTM zone 31N) whose grid georeferencing is
        // native metres, so reproject through CoverageExtent when a transform
        // factory is available rather than treating native origin/spacing as
        // degrees. Without a factory, fall back to the naive geographic box.
        if (transforms is not null)
        {
            try
            {
                return CoverageExtent.ToWgs84Bounds(source.Metadata, transforms) ?? WorldBounds;
            }
            catch (Exception ex) when (ex is NotSupportedException or FormatException or OverflowException)
            {
                // Unsupported or malformed horizontal CRS: don't fail the whole
                // dataset load — fall back to a safe world extent so the tile
                // still loads (and simply won't match precise picks).
                return WorldBounds;
            }
        }

        return ComputeS102Bounds(source.Dataset) ?? WorldBounds;
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
