using System.Globalization;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S104;
using EncDotNet.S100.Datasets.S104.Validation;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for S-104 water-level datasets. Branches between
/// dcf2 (regular grid → coverage layer) and dcf1/dcf8 positioned station
/// series (station-glyph point layer; S-100 Part 10c §10.2.1).
/// </summary>
public sealed class S104DatasetProcessor : IDatasetProcessor, ICoveragePortrayalSource, IHeadlessImageRenderer, ITimeAwareDatasetProcessor
{
    // dcf2 only
    private readonly S104CoverageSource? _source;
    private readonly S104PortrayalCatalogue? _catalogue;

    // Station-series formats only
    private readonly S104StationSeriesDataset? _stationSeries;
    private readonly IReadOnlyList<DateTime> _stationTimes = Array.Empty<DateTime>();
    private readonly Dictionary<string, WaterLevelStation> _stationsById = new(StringComparer.Ordinal);

    /// <summary>
    /// Last time-step selected via <see cref="Render"/> for a station
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

    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private ValidationReport? _validationReport;
    private bool _validationCached;

    /// <inheritdoc/>
    public SpecRef Spec { get; }

    /// <inheritdoc/>
    public SpecVersionAssessment? VersionAssessment { get; }

    private DatasetMetadata? _metadata;

    /// <inheritdoc/>
    /// <remarks>
    /// Derived from the already-parsed dataset: for gridded (dcf2) surfaces
    /// from the coverage source's georeferencing metadata; for fixed-station
    /// (dcf8) series from the union of station coordinates. No HDF5 payload is
    /// re-read (issue #467, WS1).
    /// </remarks>
    public DatasetMetadata Metadata => _metadata ??= BuildMetadata();

    private DatasetMetadata BuildMetadata()
    {
        if (_source is not null && _data is S104DatasetData.GriddedCoverage gridded)
        {
            var extent = _source.Metadata.Extent;
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

    /// <summary>
    /// <see langword="true"/> when this dataset is a regularly-gridded (dcf2)
    /// water-level <em>surface</em> — the full-tile colour-band heatmap — rather
    /// than a fixed-station (dcf8) point series. S-104 Edition 2.0.0 defines no
    /// official portrayal catalogue and treats water level primarily as input to
    /// ECDIS vertical adjustment (see <see cref="S104PortrayalCatalogue"/>), so
    /// the synthesised surface is hidden by default in interactive viewers and
    /// shown on demand (issue #483). Fixed-station glyphs (dcf8) are discrete
    /// symbols at genuine tide-station locations and remain visible by default.
    /// </summary>
    public bool IsGriddedSurface => _source is not null;

    public S104DatasetProcessor(
        string path,
        ICrsTransformFactory crsTransformFactory)
        : this(File.OpenRead(path), Path.GetFileName(path), crsTransformFactory)
    {
    }

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
                _catalogue = new S104PortrayalCatalogue();
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

    private async Task<CoveragePortrayalResult> BuildGriddedAsync(RenderContext? context, CancellationToken cancellationToken)
    {
        var source = _source!;
        var catalogue = _catalogue!;
        await catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);

        DateTime selectedTime;
        if (context is S104RenderContext { TimeStep: { } timeStep })
        {
            source.SelectTime(timeStep);
            selectedTime = timeStep;
        }
        else
        {
            selectedTime = source.AvailableTimes[0];
            source.SelectTime(selectedTime);
        }

        var metadata = source.Metadata;

        var viewport = new EncDotNet.S100.Pipelines.Viewport
        {
            MinLatitude = metadata.Extent.SouthLatitude,
            MaxLatitude = metadata.Extent.NorthLatitude,
            MinLongitude = metadata.Extent.WestLongitude,
            MaxLongitude = metadata.Extent.EastLongitude,
            WidthPixels = metadata.GridMetadata.NumColumns,
            HeightPixels = metadata.GridMetadata.NumRows,
            ScaleDenominator = 50_000,
        };

        var pipeline = new PortrayalPipeline();

        // Viewport-scoped sampling (issue #487).
        int crs = ((S104DatasetData.GriddedCoverage)_data).Dataset.HorizontalCRS ?? 4326;
        ICrsTransform? wgs84ToNative = null;
        if (context?.Viewport is not null && crs != 4326)
        {
            wgs84ToNative = _crsTransformFactory.Create("EPSG:4326", $"EPSG:{crs}");
        }

        var layer = await pipeline.ProcessAsync(
            source,
            catalogue,
            viewport: context?.Viewport,
            wgs84ToNative: wgs84ToNative,
            mariner: context?.Mariner ?? MarinerSettings.Default,
            cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var styledLayer = (StyledCoverageLayer)layer;

        var griddedDataset = ((S104DatasetData.GriddedCoverage)_data).Dataset;
        var geoId = griddedDataset.GeographicIdentifier ?? _fileName;
        var timeInfo = source.AvailableTimes.Count > 1
            ? $", time: {selectedTime:u} ({source.AvailableTimes.Count} steps)"
            : "";
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}{timeInfo}";

        return new CoveragePortrayalResult
        {
            // S-104 colour band → OnDemandSurface (S-98 Main §9.2.1
            // layer 6 "Official on demand data"; Annex A §A-6.9.1).
            SubLayers = new CoverageSubLayerBase[]
            {
                new GridCoverageSubLayer
                {
                    LayerKey = "s104.color-band",
                    LayerName = $"S-104: {_fileName}",
                    Plane = S98DisplayPlane.OnDemandSurface,
                    WithinPlanePriority = 0,
                    SourceFeatureType = "s104.color-band",
                    Coverage = styledLayer,
                    Viewport = viewport,
                },
            },
            Spec = new SpecRef("S-104", default),
            SourceDatasetId = _fileName,
            Info = info,
        };
    }

    /// <summary>
    /// Renders the water-level portrayal to a standalone <see cref="SKBitmap"/>
    /// through the headless, Mapsui-free Skia path. The selected time step is
    /// taken from <see cref="S104RenderContext.TimeStep"/>, defaulting to the
    /// first available step.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <param name="context">Optional render context (palette, time step, mariner settings).</param>
    /// <param name="background">Optional background fill; defaults to opaque white.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public async Task<SKBitmap> RenderHeadlessAsync(
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
            var glyphLayer = portrayal.SubLayers.OfType<GlyphCoverageSubLayer>().Single();
            if (glyphLayer.Extent is not { } glyphExtent)
                throw new InvalidOperationException("The S-104 station portrayal has no geographic extent.");

            return PointGlyphHeadlessAdapter.Render(
                glyphLayer,
                glyphExtent,
                widthPixels,
                heightPixels,
                background ?? new RgbaColor(255, 255, 255, 255));
        }

        if (_source is null)
            throw new InvalidOperationException("The S-104 dataset has no renderable data source.");

        var source = _source;
        var catalogue = _catalogue!;
        await catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);

        if (context is S104RenderContext { TimeStep: { } timeStep })
            source.SelectTime(timeStep);
        else
            source.SelectTime(source.AvailableTimes[0]);

        var styledLayer = (StyledCoverageLayer)await new PortrayalPipeline()
            .ProcessAsync(source, catalogue, mariner: context?.Mariner ?? MarinerSettings.Default, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        var extent = source.Metadata.Extent;
        var renderer = new CoverageHeadlessRenderer
        {
            Background = background ?? new RgbaColor(255, 255, 255, 255),
        };

        return renderer.Render(
            styledLayer,
            extent.WestLongitude,
            extent.EastLongitude,
            extent.SouthLatitude,
            extent.NorthLatitude,
            widthPixels,
            heightPixels,
            context?.Basemap ?? BasemapKind.None);
    }

    // ---- station-series rendering --------------------------------------

    private CoveragePortrayalResult BuildStationSeries(S104StationSeriesDataset ds, RenderContext? context)
    {
        DateTime selectedTime;
        if (context is S104RenderContext { TimeStep: { } timeStep })
        {
            selectedTime = timeStep;
        }
        else
        {
            selectedTime = ds.MinTime ?? DateTime.UtcNow;
        }

        _stationSelectedTime = selectedTime;

        var nativeToMerc = _crsTransformFactory.Create($"EPSG:{ds.HorizontalCRS ?? 4326}", "EPSG:3857");

        var glyphs = new List<PointGlyph>(ds.Stations.Count);
        double mercMinX = double.PositiveInfinity, mercMinY = double.PositiveInfinity;
        double mercMaxX = double.NegativeInfinity, mercMaxY = double.NegativeInfinity;

        foreach (var station in ds.Stations)
        {
            double mx, my;
            if (nativeToMerc.IsIdentity)
            {
                mx = station.Longitude;
                my = station.Latitude;
            }
            else
            {
                (mx, my) = nativeToMerc.Transform(station.Longitude, station.Latitude);
            }

            if (mx < mercMinX) mercMinX = mx;
            if (mx > mercMaxX) mercMaxX = mx;
            if (my < mercMinY) mercMinY = my;
            if (my > mercMaxY) mercMaxY = my;

            int idx = station.NearestTimeIndex(selectedTime);
            var height = station.Heights[idx];
            var trend = station.Trends[idx];

            var fill = TrendFill(trend);
            var stroke = ResolveStrokeColor(height, ds.WaterLevelTrendThreshold);

            glyphs.Add(new PointGlyph
            {
                MercatorX = mx,
                MercatorY = my,
                FeatureRefTag = StationFeatureRefPrefix + station.Identifier,
                Attributes = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["StationId"] = station.Identifier,
                    ["WaterLevelHeight"] = height,
                    ["WaterLevelTrend"] = (int)trend,
                    ["WaterLevelTrendLabel"] = DecodeTrend(trend),
                    ["SampleTime"] = station.TimeAt(idx),
                    ["Latitude"] = station.Latitude,
                    ["Longitude"] = station.Longitude,
                },
                Symbol = PointGlyphSymbol.Ellipse,
                FillColor = fill,
                OutlineColor = stroke,
                OutlineWidth = 1.5,
                SymbolScale = 0.7,
            });
        }

        var extent = ds.Stations.Count == 0
            ? (MercatorBounds?)null
            : new MercatorBounds(mercMinX, mercMinY, mercMaxX, mercMaxY);

        var info = $"{ds.GeographicIdentifier ?? _fileName} — {ds.Stations.Count} stations, " +
                   $"time: {selectedTime:u}";

        return new CoveragePortrayalResult
        {
            // S-104 station glyphs (PR-I) are point overlays drawn above
            // coverage but below cautions, per the MSC.530(106)/Rev.1
            // §App.2 layer-6 catch-all reading.
            SubLayers = new CoverageSubLayerBase[]
            {
                new GlyphCoverageSubLayer
                {
                    LayerKey = "s104.stations",
                    LayerName = $"S-104: {_fileName}",
                    Plane = S98DisplayPlane.OtherChartOverlays,
                    WithinPlanePriority = 0,
                    SourceFeatureType = "s104.stations",
                    Glyphs = glyphs,
                    Extent = extent,
                },
            },
            Spec = new SpecRef("S-104", default),
            SourceDatasetId = _fileName,
            Info = info,
        };
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
    /// Trend → fill colour table (S-104 Edition 2.0.0 §10.2.7 trend
    /// enumeration: 0 unknown, 1 decreasing, 2 increasing, 3 steady).
    /// </summary>
    private static RgbaColor TrendFill(byte trend) => trend switch
    {
        1 => new RgbaColor(42, 111, 151),    // #2a6f97 descending blue
        2 => new RgbaColor(193, 18, 31),     // #c1121f ascending red
        3 => new RgbaColor(42, 157, 143),    // #2a9d8f neutral teal
        _ => new RgbaColor(128, 128, 128),   // #808080 unknown grey
    };

    private static RgbaColor ResolveStrokeColor(float height, double? trendThreshold)
    {
        if (trendThreshold is null) return new RgbaColor(0, 0, 0);
        return height >= trendThreshold.Value
            ? new RgbaColor(255, 255, 255)
            : new RgbaColor(0, 0, 0);
    }

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
            _validationReport = ComputeValidationReport();
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
                    return S104DatasetRules.Default.Run(g.Dataset);
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
