using System.Collections.Concurrent;
using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Catalog;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.Pipelines.Query;
using EncDotNet.S100.Datasets.Pipelines.Sampling;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S104.Validation;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for S-104 water-level datasets. Branches between
/// dcf2 (regular grid → coverage layer) and dcf1/dcf8 positioned station
/// series (station-glyph point layer; S-100 Part 10c §10.2.1).
/// </summary>
public sealed class S104DatasetProcessor : IDatasetProcessor, ICoveragePortrayalSource, IHeadlessImageRenderer, ITimeAwareDatasetProcessor, ILoadedDatasetProjection, ICoverageSampler
{
    /// <summary>The sub-layer key of the gridded surface's isolines.</summary>
    public const string IsolineLayerKey = "s104.isolines";

    /// <summary>The sub-layer key of the station gauges.</summary>
    public const string StationLayerKey = "s104.stations";

    // dcf2 only
    private readonly S104CoverageSource? _source;
    private readonly ConcurrentDictionary<int, IReadOnlyList<Isoline>> _isolinesByStep = new();
    private readonly Lazy<(double Minimum, double Maximum, double Interval)>? _isolineRange;

    // Station-series formats only
    private readonly S104StationSeriesDataset? _stationSeries;
    private readonly IReadOnlyList<DateTime> _stationTimes = Array.Empty<DateTime>();
    private readonly Dictionary<string, WaterLevelStation> _stationsById = new(StringComparer.Ordinal);

    /// <summary>
    /// Last time-step selected via <see cref="RenderHeadlessAsync"/> for a station
    /// series. Cached so <see cref="GetFeatureInfo"/> reports the sample
    /// at the same time the rendered glyph is showing. <c>null</c> until
    /// the first render.
    /// </summary>
    private DateTime? _stationSelectedTime;

    /// <summary>
    /// Prefix used on the renderer's feature-ref tag for station-series
    /// point features. The remainder is the station identifier.
    /// <see cref="GetFeatureInfo"/> recognises this prefix to route station
    /// picks back through this processor.
    /// </summary>
    internal const string StationFeatureRefPrefix = "station:";

    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly S104DatasetData _data;
    private readonly string _fileName;

    // The dataset's content hash plus the S-104 style's scope (its version and
    // the toolkit's), keying the station gauges' warm tile disk cache.
    private readonly string _contentScope;

    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private ValidationReport? _validationReport;
    private bool _validationCached;

    /// <inheritdoc/>
    public SpecRef Spec { get; }

    /// <inheritdoc/>
    public SpecVersionAssessment? VersionAssessment { get; }

    /// <inheritdoc/>
    public EncDotNet.S100.DataModel.AxisOrderAssessment? AxisOrder => _data switch
    {
        S104DatasetData.GriddedCoverage g => g.Dataset.AxisOrder,
        S104DatasetData.StationSeries s => s.Dataset.AxisOrder,
        _ => null,
    };

    private DatasetMetadata? _metadata;

    /// <inheritdoc/>
    /// <remarks>
    /// Derived from the already-parsed dataset: for gridded (dcf2) surfaces
    /// from the coverage source's georeferencing metadata; for fixed-station
    /// (dcf8) series from the union of station coordinates. No HDF5 payload is
    /// re-read (issue #467, WS1).
    /// </remarks>
    public DatasetMetadata Metadata => _metadata ??= BuildMetadata();

    /// <inheritdoc />
    public LoadedDatasetData CreateLoadedData() =>
        _source is not null ? new S104CoverageData(_source) : new S104StationSeriesData(_stationSeries!);

    /// <inheritdoc />
    /// <remarks>
    /// Water level height and trend: at the grid node nearest the point for a
    /// regular grid (dcf2), or at every station of a station series (dcf8),
    /// at the step nearest the requested time.
    /// </remarks>
    public CoverageSiteSearch FindSites(CoverageSampleQuery query) => _source is not null
        ? new S104GridSampler(_source).FindSites(query)
        : new S104StationSampler(_stationSeries!).FindSites(query);

    private DatasetMetadata BuildMetadata()
    {
        if (_source is not null && _data is S104DatasetData.GriddedCoverage gridded)
        {
            // Native CRS units, labelled by HorizontalCrsEpsg (matches the
            // reader's ReadMetadata probe); see DatasetMetadata.GetGeographicExtent.
            var extent = _source.Metadata.NativeExtent;
            return new DatasetMetadata
            {
                Spec = Spec,
                Extent = new BoundingBox(
                    extent.SouthLatitude,
                    extent.WestLongitude,
                    extent.NorthLatitude,
                    extent.EastLongitude),
                HorizontalCrsEpsg = gridded.Dataset.HorizontalCRS,
            };
        }

        if (_stationSeries is { Stations.Count: > 0 } series)
        {
            double minLat = double.MaxValue, minLon = double.MaxValue;
            double maxLat = double.MinValue, maxLon = double.MinValue;
            foreach (var station in series.Stations)
            {
                if (station.Latitude < minLat) minLat = station.Latitude;
                if (station.Latitude > maxLat) maxLat = station.Latitude;
                if (station.Longitude < minLon) minLon = station.Longitude;
                if (station.Longitude > maxLon) maxLon = station.Longitude;
            }

            return new DatasetMetadata
            {
                Spec = Spec,
                Extent = new BoundingBox(minLat, minLon, maxLat, maxLon),
                HorizontalCrsEpsg = series.HorizontalCRS,
            };
        }

        return new DatasetMetadata { Spec = Spec };
    }

    /// <summary>Available forecast time steps in this dataset.</summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _source?.AvailableTimes ?? _stationTimes;

    /// <inheritdoc />
    public DateTime? IssueTime => _data switch
    {
        S104DatasetData.GriddedCoverage g => S100IssueTime.Parse(g.Dataset.IssueDate, g.Dataset.IssueTime),
        S104DatasetData.StationSeries s => S100IssueTime.Parse(s.Dataset.IssueDate, s.Dataset.IssueTime),
        _ => null,
    };

    /// <summary>
    /// S-104 water level steps: forecasts, each held until the next step for
    /// one typical step. Treated as forecasts until the reader exposes
    /// <c>dataDynamicity</c>; an observation would hold its last value for
    /// 30 minutes.
    /// </summary>
    public static TimeSemantics WaterLevelTimeSemantics { get; } =
        new(TimeStepKind.Forecast, TimeStepSelection.AtOrBefore);

    /// <inheritdoc />
    /// <remarks>Always <see cref="WaterLevelTimeSemantics"/>.</remarks>
    public TimeSemantics TimeSemantics => WaterLevelTimeSemantics;

    /// <summary>
    /// <see langword="true"/> when this dataset is a regularly-gridded (dcf2)
    /// water-level <em>surface</em>, drawn as isolines, rather than a
    /// fixed-station (dcf1 / dcf8) point series. S-104 Edition 2.0.0 defines no
    /// official portrayal catalogue and treats water level primarily as input to
    /// ECDIS vertical adjustment (see <see cref="S104CoverageStyle"/>), so
    /// the surface is hidden by default in interactive viewers and shown on
    /// demand (issue #483). Fixed-station gauges are discrete symbols at genuine
    /// tide-station locations and remain visible by default.
    /// </summary>
    public bool IsGriddedSurface => _source is not null;

    /// <summary>
    /// Initializes a new <see cref="S104DatasetProcessor"/> by reading and
    /// parsing the HDF5 dataset file at <paramref name="path"/>. The file is
    /// read in full and closed before the constructor returns.
    /// </summary>
    /// <param name="path">Path to the S-104 HDF5 dataset file.</param>
    /// <param name="crsTransformFactory">
    /// Creates the WGS84-to-native CRS transforms used when sampling a gridded
    /// dataset for <see cref="GetCoverageInfo"/>.
    /// </param>
    /// <exception cref="S100DatasetSchemaException">
    /// The file does not match the S-104 HDF5 schema. The exception carries the file name.
    /// </exception>
    /// <exception cref="S100DatasetNotSupportedException">
    /// The file uses an S-104 feature this reader does not support. The exception carries the file name.
    /// </exception>
    public S104DatasetProcessor(
        string path,
        ICrsTransformFactory crsTransformFactory)
        : this(File.OpenRead(path), Path.GetFileName(path), crsTransformFactory)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S104DatasetProcessor"/> by reading the
    /// HDF5 dataset <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    /// <param name="source">Asset source holding the dataset (e.g. a folder or ZIP archive).</param>
    /// <param name="relativePath">Path of the dataset file within <paramref name="source"/>.</param>
    /// <param name="crsTransformFactory">
    /// Creates the WGS84-to-native CRS transforms used when sampling a gridded
    /// dataset for <see cref="GetCoverageInfo"/>.
    /// </param>
    /// <exception cref="S100DatasetSchemaException">
    /// The file does not match the S-104 HDF5 schema. The exception carries the file name.
    /// </exception>
    /// <exception cref="S100DatasetNotSupportedException">
    /// The file uses an S-104 feature this reader does not support. The exception carries the file name.
    /// </exception>
    public S104DatasetProcessor(
        IAssetSource source,
        string relativePath,
        ICrsTransformFactory crsTransformFactory)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            crsTransformFactory)
    {
    }

    private S104DatasetProcessor(
        Stream datasetStream,
        string fileName,
        ICrsTransformFactory crsTransformFactory)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _crsTransformFactory = crsTransformFactory;
        _contentScope = $"ds:{DatasetContentHash.HashAndRewind(ref datasetStream)}|{S104CoverageStyle.ContentScope}";

        using (datasetStream)
        using (var hdf5 = PureHdfFile.Open(datasetStream))
        {
            try
            {
                _data = S104DatasetReader.ReadAny(hdf5);
            }
            catch (S100DatasetSchemaException ex) when (ex.File is null)
            {
                throw ex.WithFile(_fileName);
            }
            catch (S100DatasetNotSupportedException ex) when (ex.File is null)
            {
                throw ex.WithFile(_fileName);
            }
        }

        switch (_data)
        {
            case S104DatasetData.GriddedCoverage g:
                _source = new S104CoverageSource(g.Dataset);
                _isolineRange = new(() => IsolineRange(g.Dataset));
                break;
            case S104DatasetData.StationSeries s:
                _stationSeries = s.Dataset;
                _stationTimes = ComputeStationUnionTimes(s.Dataset);
                foreach (var station in s.Dataset.Stations)
                {
                    _stationsById[station.Identifier] = station;
                }
                break;
        }

        Spec = HdfDeclaredSpec.Resolve(_data.DeclaredProductSpecification, "S-104");
        VersionAssessment = SupportedSpecEditions.Assess(Spec);
    }

    /// <inheritdoc/>
    public async Task<CoveragePortrayalResult> BuildCoveragePortrayalAsync(RenderContext? context = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await _renderGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_stationSeries is not null)
            {
                return BuildStationSeries(_stationSeries, context);
            }
            return await BuildGriddedAsync(context, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _renderGate.Release();
        }
    }

    private Task<CoveragePortrayalResult> BuildGriddedAsync(RenderContext? context, CancellationToken cancellationToken)
    {
        var source = _source!;
        var dataset = ((S104DatasetData.GriddedCoverage)_data).Dataset;
        int step = SelectedStep(context);
        var selectedTime = dataset.Coverages[step].TimePoint;
        source.SelectTime(selectedTime);

        var subLayer = BuildIsolineSubLayer(step, context, cancellationToken);

        var metadata = source.Metadata;
        int crs = dataset.HorizontalCRS ?? 4326;
        var geoId = dataset.GeographicIdentifier ?? _fileName;
        var timeInfo = source.AvailableTimes.Count > 1
            ? $", time: {selectedTime:u} ({source.AvailableTimes.Count} steps)"
            : "";
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}{timeInfo}";

        return Task.FromResult(new CoveragePortrayalResult
        {
            // S-104 isolines → OnDemandSurface (S-98 Main §9.2.1 layer 6
            // "Official on demand data"; Annex A §A-6.9.1).
            SubLayers = new CoverageSubLayerBase[] { subLayer },
            Spec = Spec,
            SourceDatasetId = _fileName,
            Info = info,
        });
    }

    /// <summary>
    /// The coverage index of the time step <paramref name="context"/> selects
    /// (the nearest one), or the first step without one.
    /// </summary>
    private int SelectedStep(RenderContext? context)
    {
        var coverages = ((S104DatasetData.GriddedCoverage)_data).Dataset.Coverages;
        if (context?.TimeStep is not { } time)
            return 0;

        int best = 0;
        var bestDiff = TimeSpan.MaxValue;
        for (int i = 0; i < coverages.Count; i++)
        {
            var diff = (coverages[i].TimePoint - time).Duration();
            if (diff < bestDiff)
            {
                bestDiff = diff;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Builds the SoundCharts water-level isolines for time step
    /// <paramref name="step"/>: dashed lines in the palette's ink at the
    /// dataset's interval, labelled in metres (see <see cref="S104CoverageStyle"/>).
    /// </summary>
    private IsolineCoverageSubLayer BuildIsolineSubLayer(int step, RenderContext? context, CancellationToken cancellationToken)
    {
        var dataset = ((S104DatasetData.GriddedCoverage)_data).Dataset;
        var palette = context?.Palette ?? PaletteType.Day;
        // Labels follow the mariner's text scale, as S-101 labels do.
        double textScale = context?.TextScale ?? 1.0;
        var isolines = _isolinesByStep.GetOrAdd(step, s => TraceIsolines(dataset, s, cancellationToken));

        return new IsolineCoverageSubLayer
        {
            LayerKey = IsolineLayerKey,
            LayerName = $"S-104: {_fileName}",
            Plane = S98DisplayPlane.OnDemandSurface,
            WithinPlanePriority = 0,
            SourceFeatureType = IsolineLayerKey,
            Isolines = isolines,
            Extent = GridMercatorExtent(),
            Ink = S104CoverageStyle.Palette.Resolve(S104CoverageStyle.InkToken, palette),
            LabelHalo = S104CoverageStyle.Palette.Resolve(S104CoverageStyle.IsolineLabelHaloToken, palette),
            LabelFontSize = 10 * textScale,
            LabelHaloWidth = 3 * textScale,
        };
    }

    private IReadOnlyList<Isoline> TraceIsolines(S104Dataset dataset, int step, CancellationToken cancellationToken)
    {
        var (minimum, maximum, interval) = _isolineRange!.Value;
        var levels = S104CoverageStyle.IsolineLevels(minimum, maximum, interval);
        var coverage = dataset.Coverages[step];
        int rows = coverage.NumPointsLatitudinal;
        int columns = coverage.NumPointsLongitudinal;

        var heights = new float[coverage.Values.Length];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = coverage.Values[i].Height;

        cancellationToken.ThrowIfCancellationRequested();
        var traced = IsolineTracer.Trace(heights, rows, columns, S104CoverageSource.FillValue, levels);

        var nativeToWgs84 = _crsTransformFactory.Create($"EPSG:{dataset.HorizontalCRS ?? 4326}", "EPSG:4326");
        var result = new List<Isoline>(traced.Count);
        foreach (var line in traced)
        {
            var points = line.Points;
            var mercator = new double[points.Length];
            for (int i = 0; i + 1 < points.Length; i += 2)
            {
                double x = coverage.OriginLongitude + points[i] * coverage.SpacingLongitudinal;
                double y = coverage.OriginLatitude + points[i + 1] * coverage.SpacingLatitudinal;
                var (lon, lat) = nativeToWgs84.IsIdentity ? (x, y) : nativeToWgs84.Transform(x, y);
                (mercator[i], mercator[i + 1]) = WebMercator.FromLonLat(lon, lat);
            }

            result.Add(new Isoline
            {
                Level = line.Level,
                Label = S104CoverageStyle.FormatIsolineLabel(line.Level, interval),
                MercatorCoordinates = mercator,
            });
        }
        return result;
    }

    /// <summary>
    /// The dataset's value range over every time step (fill values excluded) and
    /// the isoline interval it selects. Computed once, so the lines keep their
    /// density while the time step changes.
    /// </summary>
    private static (double Minimum, double Maximum, double Interval) IsolineRange(S104Dataset dataset)
    {
        double minimum = double.PositiveInfinity, maximum = double.NegativeInfinity;
        foreach (var coverage in dataset.Coverages)
        {
            foreach (var value in coverage.Values)
            {
                float h = value.Height;
                if (float.IsNaN(h) || h == S104CoverageSource.FillValue)
                    continue;
                minimum = Math.Min(minimum, h);
                maximum = Math.Max(maximum, h);
            }
        }

        return double.IsFinite(minimum)
            ? (minimum, maximum, S104CoverageStyle.SelectIsolineInterval(minimum, maximum))
            : (0.0, 0.0, S104CoverageStyle.IsolineInterval);
    }

    /// <summary>The EPSG:3857 extent of the whole grid, half a cell beyond the outer nodes.</summary>
    private MercatorBounds GridMercatorExtent()
    {
        var extent = _source!.Metadata.GetGeographicExtent(_crsTransformFactory);
        var (minX, minY) = WebMercator.FromLonLat(extent.WestLongitude, extent.SouthLatitude);
        var (maxX, maxY) = WebMercator.FromLonLat(extent.EastLongitude, extent.NorthLatitude);
        return new MercatorBounds(minX, minY, maxX, maxY);
    }

    /// <summary>
    /// Renders the water-level portrayal to a standalone <see cref="SKBitmap"/>
    /// through the headless, Mapsui-free Skia path. The selected time step is
    /// taken from <see cref="RenderContext.TimeStep"/>, defaulting to the
    /// first available step.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <param name="context">Optional render context (palette, time step, mariner settings).</param>
    /// <param name="background">Optional background fill; defaults to opaque white.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public Task<SKBitmap> RenderHeadlessAsync(
        int widthPixels,
        int heightPixels,
        RenderContext? context = null,
        RgbaColor? background = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);
        cancellationToken.ThrowIfCancellationRequested();

        if (_stationSeries is { } stationSeries)
        {
            var portrayal = BuildStationSeries(stationSeries, context);
            var stations = portrayal.SubLayers.OfType<VectorCoverageSubLayer>().Single();
            if (stations.Extent is not { } stationExtent)
                throw new InvalidOperationException("The S-104 station portrayal has no geographic extent.");

            return Task.FromResult(PointGlyphHeadlessAdapter.Render(
                stations,
                stationExtent,
                widthPixels,
                heightPixels,
                background ?? new RgbaColor(255, 255, 255, 255),
                context?.Basemap ?? BasemapKind.None,
                context?.HiddenInstructionCategories ?? DrawingInstructionCategory.None));
        }

        if (_source is null)
            throw new InvalidOperationException("The S-104 dataset has no renderable data source.");

        var isolines = BuildIsolineSubLayer(SelectedStep(context), context, cancellationToken);
        return Task.FromResult(IsolineHeadlessAdapter.Render(
            isolines,
            widthPixels,
            heightPixels,
            background ?? new RgbaColor(255, 255, 255, 255),
            context?.Basemap ?? BasemapKind.None));
    }

    // ---- station-series rendering --------------------------------------

    /// <summary>
    /// Builds the station gauges for a fixed-station series (dcf1 / dcf8) at the
    /// selected time step.
    /// </summary>
    /// <remarks>
    /// S-104 Ed 2.0.0 §9 defines no portrayal, so the gauge is SoundCharts' own
    /// (see <see cref="S104CoverageStyle"/>): a rounded square in the
    /// palette's ink with a chevron up (rising), chevron down (falling), dash
    /// (steady) or "?" (unknown) for <c>waterLevelTrend</c>, and the height at
    /// the selected step as a signed label, shown at 1:100 000 and larger scales.
    /// A station whose height at the step is the fill value draws "—" and no
    /// label. Colour encodes neither trend nor height.
    /// </remarks>
    private CoveragePortrayalResult BuildStationSeries(S104StationSeriesDataset ds, RenderContext? context)
    {
        DateTime selectedTime;
        if (context?.TimeStep is { } timeStep)
        {
            selectedTime = timeStep;
        }
        else
        {
            selectedTime = ds.MinTime ?? DateTime.UtcNow;
        }

        _stationSelectedTime = selectedTime;

        var nativeToWgs84 = _crsTransformFactory.Create($"EPSG:{ds.HorizontalCRS ?? 4326}", "EPSG:4326");
        var palette = context?.Palette ?? PaletteType.Day;

        var instructions = new List<DrawingInstruction>(ds.Stations.Count * 2);
        var geometries = new Dictionary<string, FeatureGeometry>(ds.Stations.Count, StringComparer.Ordinal);
        double mercMinX = double.PositiveInfinity, mercMinY = double.PositiveInfinity;
        double mercMaxX = double.NegativeInfinity, mercMaxY = double.NegativeInfinity;

        foreach (var station in ds.Stations)
        {
            var (lon, lat) = nativeToWgs84.IsIdentity
                ? (station.Longitude, station.Latitude)
                : nativeToWgs84.Transform(station.Longitude, station.Latitude);
            var (mx, my) = WebMercator.FromLonLat(lon, lat);
            mercMinX = Math.Min(mercMinX, mx);
            mercMaxX = Math.Max(mercMaxX, mx);
            mercMinY = Math.Min(mercMinY, my);
            mercMaxY = Math.Max(mercMaxY, my);

            var featureRef = StationFeatureRefPrefix + station.Identifier;
            geometries[featureRef] = new FeatureGeometry
            {
                Type = GeometryType.Point,
                Coordinates = [new GeoPosition(lat, lon)],
            };

            int idx = station.NearestTimeIndex(selectedTime);
            var height = station.Heights[idx];
            bool noData = float.IsNaN(height) || height == S104CoverageSource.FillValue;

            instructions.Add(S104CoverageStyle.StationSymbols.Point(
                featureRef,
                noData ? S104CoverageStyle.NoDataSymbol : StationSymbol(station.Trends[idx])));

            if (!noData)
            {
                // Drawn at 1:100 000 and larger, as an S-101 label with SCAMIN is.
                instructions.Add(S104CoverageStyle.StationLabels.Text(
                    featureRef,
                    S104CoverageStyle.FormatStationValue(height),
                    S104CoverageStyle.StationLabelStyle));
            }
        }

        var extent = ds.Stations.Count == 0
            ? (MercatorBounds?)null
            : new MercatorBounds(mercMinX, mercMinY, mercMaxX, mercMaxY);

        var info = $"{ds.GeographicIdentifier ?? _fileName} — {ds.Stations.Count} stations, " +
                   $"time: {selectedTime:u}";

        return new CoveragePortrayalResult
        {
            // S-104 station gauges are point overlays drawn above coverage but
            // below cautions, per the MSC.530(106)/Rev.1 §App.2 layer-6
            // catch-all reading.
            SubLayers = new CoverageSubLayerBase[]
            {
                new VectorCoverageSubLayer
                {
                    LayerKey = StationLayerKey,
                    LayerName = $"S-104: {_fileName}",
                    Plane = S98DisplayPlane.OtherChartOverlays,
                    WithinPlanePriority = 0,
                    SourceFeatureType = StationLayerKey,
                    Instructions = instructions,
                    GeometryProvider = new StationGeometryProvider(geometries),
                    Palette = S104CoverageStyle.Palette.GetColorPalette(palette),
                    SymbolProvider = S104CoverageStyle.Symbols.Provider,
                    SymbolScale = context?.SymbolScale ?? 1.0,
                    TextScale = context?.TextScale ?? 1.0,
                    Product = "S-104",
                    Extent = extent,
                },
            },
            Spec = Spec,
            SourceDatasetId = _fileName,
            Info = info,
            ContentScope = _contentScope,
        };
    }

    private sealed class StationGeometryProvider(IReadOnlyDictionary<string, FeatureGeometry> geometries) : IFeatureGeometryProvider
    {
        public FeatureGeometry? GetGeometry(string featureReference) =>
            geometries.TryGetValue(featureReference, out var geometry) ? geometry : null;
    }

    private static IReadOnlyList<DateTime> ComputeStationUnionTimes(S104StationSeriesDataset ds)
    {
        var set = new SortedSet<DateTime>();
        foreach (var s in ds.Stations)
        {
            for (int i = 0; i < s.NumberOfTimes; i++)
            {
                set.Add(DateTime.SpecifyKind(s.TimeAt(i), DateTimeKind.Utc));
            }
        }
        return set.ToArray();
    }

    /// <summary>
    /// The station symbol for a <c>waterLevelTrend</c> code (S-104 Edition 2.0.0
    /// §10.2.7: 0 unknown, 1 decreasing, 2 increasing, 3 steady).
    /// </summary>
    private static string StationSymbol(byte trend) => trend switch
    {
        1 => S104CoverageStyle.FallingSymbol,
        2 => S104CoverageStyle.RisingSymbol,
        3 => S104CoverageStyle.SteadySymbol,
        _ => S104CoverageStyle.UnknownSymbol,
    };

    /// <summary>
    /// Resolves dcf8 station picks routed via the renderer's feature-ref tag
    /// the glyph layer attaches to each station point. Refs are formatted as
    /// <c>"station:&lt;id&gt;"</c> (see <see cref="StationFeatureRefPrefix"/>).
    /// For dcf2 gridded datasets and other refs this returns <c>null</c>;
    /// callers should fall back to <see cref="GetCoverageInfo"/>.
    /// </summary>
    public FeatureInfo? GetFeatureInfo(string featureRef)
    {
        ArgumentNullException.ThrowIfNull(featureRef);

        if (_stationSeries is null) return null;
        if (!featureRef.StartsWith(StationFeatureRefPrefix, StringComparison.Ordinal))
            return null;

        var id = featureRef[StationFeatureRefPrefix.Length..];
        if (!_stationsById.TryGetValue(id, out var station))
            return null;

        return BuildStationFeatureInfo(station, _stationSelectedTime);
    }

    private FeatureInfo BuildStationFeatureInfo(WaterLevelStation station, DateTime? time)
    {
        var selectedTime = time ?? station.StartTime;
        int idx = station.NearestTimeIndex(selectedTime);
        var height = station.Heights[idx];
        var trend = station.Trends[idx];
        var sampleTime = station.TimeAt(idx);

        return new FeatureInfo
        {
            FeatureRef = StationFeatureRefPrefix + station.Identifier,
            FeatureType = "WaterLevel",
            FeatureTypeName = "Water Level (Station)",
            StationSeries = BuildStationSeriesSnapshot(station),
            Attributes = new List<PickAttribute>
            {
                new()
                {
                    Code = "stationIdentification",
                    Name = "Station",
                    RawValue = station.Identifier,
                },
                new()
                {
                    Code = "stationPosition",
                    Name = "Position",
                    RawValue = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.######},{1:0.######}",
                        station.Latitude, station.Longitude),
                    DisplayValue = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:0.####}°, {1:0.####}°",
                        station.Latitude, station.Longitude),
                },
                new()
                {
                    Code = "waterLevelHeight",
                    Name = "Water Level Height",
                    RawValue = height.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{height.ToString("0.##", CultureInfo.InvariantCulture)} m",
                },
                new()
                {
                    Code = "waterLevelTrend",
                    Name = "Water Level Trend",
                    RawValue = ((int)trend).ToString(CultureInfo.InvariantCulture),
                    DisplayValue = DecodeTrend(trend),
                },
                new()
                {
                    Code = "timePoint",
                    Name = "Time",
                    RawValue = sampleTime.ToString("u", CultureInfo.InvariantCulture),
                    DateTimeValue = sampleTime,
                },
                new()
                {
                    Code = "sampleCount",
                    Name = "Sample Count",
                    RawValue = station.NumberOfTimes.ToString(CultureInfo.InvariantCulture),
                },
                new()
                {
                    Code = "timeRange",
                    Name = "Time Range",
                    RawValue = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:u}/{1:u}",
                        station.StartTime, station.EndTime),
                    DisplayValue = string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:u} → {1:u}",
                        station.StartTime, station.EndTime),
                    DateTimeRangeValue = (station.StartTime, station.EndTime),
                },
            },
        };
    }

    private static StationTimeSeriesSnapshot BuildStationSeriesSnapshot(WaterLevelStation station)
    {
        var times = new DateTime[station.NumberOfTimes];
        for (var i = 0; i < station.NumberOfTimes; i++)
            times[i] = station.TimeAt(i);

        return new StationTimeSeriesSnapshot
        {
            StationId = station.Identifier,
            StationName = station.Identifier,
            Latitude = station.Latitude,
            Longitude = station.Longitude,
            Times = times,
            Channels = new[]
            {
                new StationTimeSeriesChannel
                {
                    Key = "waterLevelHeight",
                    DisplayName = "Water Level Height",
                    Unit = "m",
                    Values = station.Heights,
                    // S-104 dcf8 producers commonly use -9999 as the
                    // missing-sample sentinel; viewers filter it out so
                    // the chart doesn't spike.
                    FillValue = -9999f,
                },
            },
        };
    }

    /// <summary>
    /// Samples water level at the supplied geographic position and time.
    /// For a gridded (dcf2) dataset the containing grid cell is sampled at
    /// <paramref name="time"/> (the nearest available step; the first step when
    /// <c>null</c>). For a station series the nearest station is reported at the
    /// sample closest to <paramref name="time"/> (its first sample when
    /// <c>null</c>).
    /// </summary>
    /// <param name="latitude">WGS84 latitude in degrees.</param>
    /// <param name="longitude">WGS84 longitude in degrees.</param>
    /// <param name="time">Time to sample at, or <c>null</c> for the first available time.</param>
    /// <returns>
    /// Water-level height, trend and sample time, or <c>null</c> when the point
    /// lies outside the grid, or the dataset has no time steps or stations.
    /// </returns>
    public FeatureInfo? GetCoverageInfo(double latitude, double longitude, DateTime? time)
    {
        if (_stationSeries is not null)
        {
            return GetStationInfo(_stationSeries, latitude, longitude, time);
        }

        var source = _source!;
        if (source.AvailableTimes.Count == 0)
            return null;

        var selectedTime = time ?? source.AvailableTimes[0];
        source.SelectTime(selectedTime);

        var sample = CoveragePickHelper.Sample(source, _crsTransformFactory, latitude, longitude);
        if (sample is null)
            return null;

        var height = sample.Values.TryGetValue("waterLevelHeight", out var h) ? h : sample.NoDataValue;
        var trend = sample.Values.TryGetValue("waterLevelTrend", out var t) ? t : 0f;

        var attrs = new List<PickAttribute>
        {
            new()
            {
                Code = "waterLevelHeight",
                Name = "Water Level Height",
                RawValue = height == sample.NoDataValue
                    ? "NoData"
                    : height.ToString("0.##########", CultureInfo.InvariantCulture),
                DisplayValue = height == sample.NoDataValue
                    ? "—"
                    : $"{height.ToString("0.##", CultureInfo.InvariantCulture)} m",
            },
            new()
            {
                Code = "waterLevelTrend",
                Name = "Water Level Trend",
                RawValue = ((int)trend).ToString(CultureInfo.InvariantCulture),
                DisplayValue = DecodeTrend((int)trend),
            },
            new()
            {
                Code = "timePoint",
                Name = "Time",
                RawValue = selectedTime.ToString("u", CultureInfo.InvariantCulture),
                DateTimeValue = selectedTime,
            },
        };

        return new FeatureInfo
        {
            FeatureRef = $"({sample.Row},{sample.Col})",
            FeatureType = "WaterLevel",
            FeatureTypeName = "Water Level",
            Attributes = attrs,
        };
    }

    /// <summary>
    /// Samples this S-104 dataset's water-level time series at a WGS-84 point
    /// for depth assimilation, returning the nearest-cell series together with
    /// the grid spacing, issue date and vertical datum used to rank competing
    /// S-104 datasets. Returns <c>null</c> for station-series or non-gridded
    /// (data coding format ≠ 2) datasets, or when the point is out of bounds
    /// (S-104 Ed 2.0.0 §10.2 regular-grid coverage).
    /// </summary>
    /// <param name="latitude">Latitude in decimal degrees (WGS-84).</param>
    /// <param name="longitude">Longitude in decimal degrees (WGS-84).</param>
    /// <param name="from">Optional inclusive lower time bound (UTC).</param>
    /// <param name="to">Optional inclusive upper time bound (UTC).</param>
    /// <returns>The sampled tide probe, or <c>null</c>.</returns>
    public S104TideProbe? SampleTide(double latitude, double longitude, DateTime? from, DateTime? to)
    {
        if (_data is not S104DatasetData.GriddedCoverage gridded)
            return null;

        var dataset = gridded.Dataset;
        var series = S104TimeSeriesSampler.Sample(dataset, latitude, longitude, from, to);
        if (series is null)
            return null;

        var geometry = dataset.Coverages[0];
        var spacing = Math.Min(
            Math.Abs(geometry.SpacingLatitudinal),
            Math.Abs(geometry.SpacingLongitudinal));

        DateTime? issueDate = DateTime.TryParse(
            dataset.IssueDate,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;

        return new S104TideProbe(spacing, issueDate, dataset.VerticalDatum, series);
    }

    private FeatureInfo? GetStationInfo(
        S104StationSeriesDataset ds,
        double latitude,
        double longitude,
        DateTime? time)
    {
        if (ds.Stations.Count == 0) return null;

        // Nearest station via small-angle approximation; the viewer's
        // PickService already supplies pixel-tolerant hit-testing.
        WaterLevelStation? best = null;
        double bestSqDeg = double.PositiveInfinity;
        foreach (var s in ds.Stations)
        {
            var dLat = s.Latitude - latitude;
            var dLon = (s.Longitude - longitude) * Math.Cos(latitude * Math.PI / 180.0);
            var d = dLat * dLat + dLon * dLon;
            if (d < bestSqDeg)
            {
                bestSqDeg = d;
                best = s;
            }
        }
        if (best is null) return null;

        var selectedTime = time ?? best.StartTime;
        int idx = best.NearestTimeIndex(selectedTime);
        var height = best.Heights[idx];
        var trend = best.Trends[idx];
        var sampleTime = best.TimeAt(idx);

        return new FeatureInfo
        {
            FeatureRef = best.Identifier,
            FeatureType = "WaterLevel",
            FeatureTypeName = "Water Level (Station)",
            Attributes = new List<PickAttribute>
            {
                new()
                {
                    Code = "stationIdentification",
                    Name = "Station",
                    RawValue = best.Identifier,
                },
                new()
                {
                    Code = "waterLevelHeight",
                    Name = "Water Level Height",
                    RawValue = height.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{height.ToString("0.##", CultureInfo.InvariantCulture)} m",
                },
                new()
                {
                    Code = "waterLevelTrend",
                    Name = "Water Level Trend",
                    RawValue = ((int)trend).ToString(CultureInfo.InvariantCulture),
                    DisplayValue = DecodeTrend(trend),
                },
                new()
                {
                    Code = "timePoint",
                    Name = "Time",
                    RawValue = sampleTime.ToString("u", CultureInfo.InvariantCulture),
                    DateTimeValue = sampleTime,
                },
            },
        };
    }

    private static string DecodeTrend(int code) => code switch
    {
        1 => "Decreasing",
        2 => "Increasing",
        3 => "Steady",
        _ => "Unknown",
    };

    private static string DecodeTrend(byte code) => DecodeTrend((int)code);

    /// <summary>
    /// Runs the S-104 normative rule pack
    /// (<see cref="S104DatasetRules.Default"/>) against the parsed
    /// dataset and returns the cached report. Returns <c>null</c> when
    /// the loaded HDF5 has no validatable shape (defensive — currently
    /// unreachable because the constructor produces either a
    /// <see cref="S104DatasetData.GriddedCoverage"/> or a
    /// <see cref="S104DatasetData.StationSeries"/>, both of which this
    /// method handles).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per <c>docs/design/non-gml-validation.md</c> §5.1 and §5.3,
    /// this override surfaces reader-time projection diagnostics under
    /// reserved rule ids:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>S104-PROJ-SCHEMA</c> — defensive try/catch for
    /// <see cref="S100DatasetSchemaException"/>. The realistic failure
    /// mode is the constructor itself throwing, so this only fires if
    /// a future reader change moves schema validation later in the
    /// pipeline.</description></item>
    /// <item><description>Station-series datasets are checked directly for
    /// sample shape, explicit timestamp ordering, and valid trend codes because
    /// the gridded V-2 rule pack operates on <see cref="S104Dataset"/>.</description></item>
    /// </list>
    /// <para>
    /// Validation is a pure function of the parsed dataset; the
    /// report is cached after the first call (mirroring the V-1
    /// S-102 processor and the GML processors' pattern).
    /// </para>
    /// </remarks>
    public ValidationReport? Validate()
    {
        if (!_validationCached)
        {
            _validationReport = AxisOrderFindings.Append(ComputeValidationReport(), AxisOrder, "S-104");
            _validationCached = true;
        }
        return _validationReport;
    }

    private ValidationReport? ComputeValidationReport()
    {
        switch (_data)
        {
            case S104DatasetData.GriddedCoverage g:
                try
                {
                    return S104DatasetRules.Default.Run(g.Dataset, new ValidationContext { CrsTransformFactory = _crsTransformFactory });
                }
                catch (S100DatasetSchemaException ex)
                {
                    return BuildSchemaSurrogateReport(ex);
                }
                catch (S100DatasetNotSupportedException ex)
                {
                    return BuildUnsupportedSurrogateReport(ex);
                }

            case S104DatasetData.StationSeries s:
                return ValidateStationSeries(s.Dataset);

            default:
                return null;
        }
    }

    private static ValidationReport BuildSchemaSurrogateReport(S100DatasetSchemaException ex)
    {
        var details = new List<string> { $"GroupPath='{ex.GroupPath}'" };
        if (!string.IsNullOrEmpty(ex.AttributeOrDataset))
            details.Add($"AttributeOrDataset='{ex.AttributeOrDataset}'");
        if (!string.IsNullOrEmpty(ex.SpecReference))
            details.Add($"SpecReference='{ex.SpecReference}'");

        var finding = new ValidationFinding
        {
            RuleId = "S104-PROJ-SCHEMA",
            Severity = ValidationSeverity.Error,
            Message = $"S104 reader raised S100DatasetSchemaException: {ex.Message} ({string.Join(", ", details)}).",
            RelatedFeatureId = ex.GroupPath,
        };

        return new ValidationReport(
            [finding],
            RulesEvaluated: 1,
            RulesWithFindings: 1);
    }

    private static ValidationReport BuildUnsupportedSurrogateReport(S100DatasetNotSupportedException ex)
    {
        var details = new List<string>();
        if (!string.IsNullOrEmpty(ex.Feature))
            details.Add($"Feature='{ex.Feature}'");
        if (!string.IsNullOrEmpty(ex.SpecReference))
            details.Add($"SpecReference='{ex.SpecReference}'");

        var finding = new ValidationFinding
        {
            RuleId = "S104-PROJ-UNSUPPORTED",
            Severity = ValidationSeverity.Error,
            Message = $"S104 reader raised S100DatasetNotSupportedException: {ex.Message} ({string.Join(", ", details)}).",
        };

        return new ValidationReport(
            [finding],
            RulesEvaluated: 1,
            RulesWithFindings: 1);
    }

    private static ValidationReport ValidateStationSeries(S104StationSeriesDataset dataset)
    {
        var findings = new List<ValidationFinding>();
        int rulesEvaluated = dataset.Stations.Any(station => station.SampleTimes.Count > 0) ? 3 : 2;

        foreach (var station in dataset.Stations)
        {
            if (station.NumberOfTimes != station.Heights.Length ||
                station.NumberOfTimes != station.Trends.Length ||
                (station.SampleTimes.Count > 0 && station.NumberOfTimes != station.SampleTimes.Count))
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = "S104-STATION-SHAPE",
                    Severity = ValidationSeverity.Error,
                    Message = $"Station '{station.Identifier}' has inconsistent timestamp, height, or trend counts.",
                    RelatedFeatureId = station.Identifier,
                });
            }

            if (station.SampleTimes.Count > 0 &&
                station.SampleTimes.Zip(station.SampleTimes.Skip(1)).Any(pair => pair.First >= pair.Second))
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = "S104-STATION-TIME",
                    Severity = ValidationSeverity.Error,
                    Message = $"Station '{station.Identifier}' explicit timestamps are not strictly increasing.",
                    RelatedFeatureId = station.Identifier,
                });
            }

            if (station.Trends.Any(trend => trend > 3))
            {
                findings.Add(new ValidationFinding
                {
                    RuleId = "S104-STATION-TREND",
                    Severity = ValidationSeverity.Error,
                    Message = $"Station '{station.Identifier}' contains an invalid waterLevelTrend code.",
                    RelatedFeatureId = station.Identifier,
                });
            }
        }

        return new ValidationReport(
            findings,
            RulesEvaluated: rulesEvaluated,
            RulesWithFindings: findings.Select(finding => finding.RuleId).Distinct(StringComparer.Ordinal).Count());
    }
}
