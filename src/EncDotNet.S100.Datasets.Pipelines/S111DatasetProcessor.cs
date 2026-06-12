using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Datasets.S111;
using EncDotNet.S100.Datasets.S111.Validation;
using EncDotNet.S100.Hdf5;
using EncDotNet.S100.Hdf5.PureHdf;
using EncDotNet.S100.Interoperability;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Validation;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// Pipeline processor for S-111 surface-currents datasets. Branches
/// between dcf2 (regular grid → coverage + arrow layers, see existing
/// portrayal catalogue), dcf3 (ungeorectified grid → station-arrow
/// point layer; S-100 Part 10c §10.2.1), and dcf8 (time series at
/// fixed stations → station-arrow point layer; see S-111 Edition 2.0.0
/// §10.2.3 / §10.2.7).
/// </summary>
public sealed class S111DatasetProcessor : IDatasetProcessor, ICoveragePortrayalSource, IHeadlessImageRenderer, ITimeAwareDatasetProcessor, IDisposable
{
    // dcf2 only
    private readonly S111CoverageSource? _source;
    private readonly S111PortrayalCatalogue? _catalogue;
    private readonly PortrayalCatalogueProvider? _provider;
    private readonly S111Dataset? _dataset;

    // dcf8 only
    private readonly S111StationSeriesDataset? _stationSeries;
    private readonly IReadOnlyList<DateTime> _stationTimes = Array.Empty<DateTime>();
    private readonly Dictionary<string, SurfaceCurrentStation> _stationsById = new(StringComparer.Ordinal);

    /// <summary>
    /// Last time-step selected via <see cref="Render"/> for dcf8 station
    /// series. Cached so <see cref="GetFeatureInfo"/> reports the sample
    /// at the same time the rendered arrow is showing. <c>null</c> until
    /// the first render.
    /// </summary>
    private DateTime? _stationSelectedTime;

    /// <summary>
    /// Prefix used on <see cref="MapsuiDisplayListRenderer.FeatureRefKey"/>
    /// tags for dcf8 station-series point features. The remainder is the
    /// station identifier. <see cref="GetFeatureInfo"/> recognises this
    /// prefix to route station picks back through this processor.
    /// </summary>
    internal const string StationFeatureRefPrefix = "station:";

    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly S111DatasetData _data;
    private readonly string _fileName;

    private readonly SemaphoreSlim _renderGate = new(1, 1);
    private ValidationReport? _validationReport;
    private bool _validationCached;

    /// <summary>
    /// For dcf2 (regular grid) with deferred value reads, the underlying
    /// HDF5 file and stream are retained for the lifetime of the processor
    /// so per-time-step <c>values</c> datasets can be read lazily on first
    /// access. <see langword="null"/> for dcf3/dcf8, whose values are read
    /// eagerly and whose file is closed in the constructor.
    /// </summary>
    private readonly IHdf5File? _retainedHdf5;
    private readonly Stream? _retainedStream;

    /// <summary>
    /// Guards reads against the shared, retained HDF5 stream. PureHDF reads
    /// from a single stream are not concurrency-safe; lazy value factories
    /// and any post-load access lock this object.
    /// </summary>
    private readonly object _hdfGate = new();
    private bool _disposed;

    /// <inheritdoc/>
    public SpecRef Spec { get; }

    /// <inheritdoc/>
    public SpecVersionAssessment? VersionAssessment { get; }

    /// <summary>Available forecast time steps in this dataset.</summary>
    public IReadOnlyList<DateTime> AvailableTimes =>
        _source?.AvailableTimes ?? _stationTimes;

    public S111DatasetProcessor(
        string path,
        PortrayalCatalogueManager catalogueManager,
        ICrsTransformFactory crsTransformFactory)
        : this(File.OpenRead(path), Path.GetFileName(path), catalogueManager, crsTransformFactory)
    {
    }

    /// <summary>
    /// Initializes a new <see cref="S111DatasetProcessor"/> by reading
    /// the HDF5 dataset <paramref name="relativePath"/> from
    /// <paramref name="source"/>. Used by exchange-set bulk loading.
    /// </summary>
    public S111DatasetProcessor(
        IAssetSource source,
        string relativePath,
        PortrayalCatalogueManager catalogueManager,
        ICrsTransformFactory crsTransformFactory)
        : this(
            AssetSourceHelpers.OpenSeekable(source, relativePath),
            AssetSourceHelpers.GetFileName(relativePath),
            catalogueManager,
            crsTransformFactory)
    {
    }

    private S111DatasetProcessor(
        Stream datasetStream,
        string fileName,
        PortrayalCatalogueManager catalogueManager,
        ICrsTransformFactory crsTransformFactory)
    {
        ArgumentNullException.ThrowIfNull(datasetStream);
        _fileName = fileName;
        _crsTransformFactory = crsTransformFactory;

        // Open the file and request deferred value reads. The reader only
        // defers for dcf2 (regular grid); dcf3/dcf8 station series ignore
        // the flag and materialize fully, so their file can be closed
        // immediately. For dcf2 we retain the file so per-time-step values
        // are decoded lazily on first access (S-111 Edition 2.0.0 §10.2.6).
        Stream? stream = datasetStream;
        IHdf5File? hdf5 = null;
        bool retain = false;
        try
        {
            hdf5 = PureHdfFile.Open(stream);
            try
            {
                _data = S111DatasetReader.ReadAny(
                    hdf5,
                    new S111ReadOptions
                    {
                        DeferValueReads = true,
                        HdfSyncRoot = _hdfGate,
                    });
            }
            catch (S100DatasetSchemaException ex) when (ex.File is null)
            {
                throw ex.WithFile(_fileName);
            }
            catch (S100DatasetNotSupportedException ex) when (ex.File is null)
            {
                throw ex.WithFile(_fileName);
            }

            retain = _data is S111DatasetData.GriddedCoverage;
            if (retain)
            {
                _retainedHdf5 = hdf5;
                _retainedStream = stream;
            }
        }
        finally
        {
            if (!retain)
            {
                hdf5?.Dispose();
                stream?.Dispose();
            }
        }

        Spec = HdfDeclaredSpec.Resolve(_data.DeclaredProductSpecification, "S-111");

        switch (_data)
        {
            case S111DatasetData.GriddedCoverage g:
                _dataset = g.Dataset;
                _source = new S111CoverageSource(g.Dataset);
                _provider = catalogueManager.HasCatalogue("S-111")
                    ? catalogueManager.GetProvider("S-111")
                    : throw new InvalidOperationException(
                        "S-111 portrayal catalogue is not registered. " +
                        "Ensure the S-111 portrayal catalogue is loaded before opening S-111 datasets.");
                _catalogue = new S111PortrayalCatalogue(_provider);
                Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
                break;
            case S111DatasetData.StationSeries s:
                _stationSeries = s.Dataset;
                _stationTimes = ComputeStationUnionTimes(s.Dataset);
                foreach (var station in s.Dataset.Stations)
                {
                    _stationsById[station.Identifier] = station;
                }

                // DCF 3 (ungeorectified grid) uses the portrayal catalogue
                // for PC-faithful color/symbol rendering. DCF 8 (time series
                // at fixed stations) uses inline arrow glyphs — no PC required.
                if (s.Dataset.DataCodingFormat == 3
                    && catalogueManager.HasCatalogue("S-111"))
                {
                    _provider = catalogueManager.GetProvider("S-111");
                    _catalogue = new S111PortrayalCatalogue(_provider);
                    Diagnostics.CatalogueResolutionDiagnostics.Report(this, Spec, _catalogue.CatalogueRef, "portrayal");
                }
                break;
        }

        VersionAssessment = SupportedSpecEditions.Assess(Spec, _catalogue?.CatalogueRef);
    }

    /// <summary>
    /// Disposes the retained HDF5 file and stream, if any. For dcf2
    /// (regular grid) datasets the file is kept open so per-time-step
    /// values can be read lazily; this releases it.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        lock (_hdfGate)
        {
            _retainedHdf5?.Dispose();
            _retainedStream?.Dispose();
        }

        _renderGate.Dispose();
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
                return await BuildStationSeriesAsync(_stationSeries, context, cancellationToken).ConfigureAwait(false);
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
        var provider = _provider!;
        await catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);

        DateTime selectedTime;
        if (context is S111RenderContext { TimeStep: { } timeStep })
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
        var layer = await pipeline.ProcessAsync(source, catalogue, context?.Mariner ?? MarinerSettings.Default, cancellationToken)
            .ConfigureAwait(false);
        var styledLayer = (StyledCoverageLayer)layer;

        // NOTE: pre-warm every catalogue symbol so the synchronous Mapsui
        // arrow renderer's SymbolProvider lambda reads from a memory dict.
        // The symbol set is small (one entry per S-111 speed band).
        var symbolSvgs = await PreWarmProviderSymbolsAsync(provider, cancellationToken).ConfigureAwait(false);

        int crs = _dataset!.HorizontalCRS ?? 4326;
        var geoId = _dataset.GeographicIdentifier ?? _fileName;
        var timeInfo = source.AvailableTimes.Count > 1
            ? $", time: {selectedTime:u} ({source.AvailableTimes.Count} steps)"
            : "";
        var info = $"{geoId} — {metadata.GridMetadata.NumColumns}×{metadata.GridMetadata.NumRows} grid, CRS: EPSG:{crs}{timeInfo}";

        // The bundled S-111 portrayal catalogue
        // (content/S111/pc/Rules/select_arrow.xsl) defines arrow symbology
        // only — no coverageFill on surfaceCurrentSpeed. S-111 therefore emits
        // a single arrow sub-layer; rendering a synthetic colour-band heatmap
        // on top of S-101 ENC was removed because it obscured the base chart.
        //
        // The bundled portrayal catalogue (S-111 Ed 2.0.0, select_arrow.xsl)
        // declares the arrow sub-layer with intra-product
        // displayPlane="OverRadar"; S-98 Annex A §A-6.9.1 maps current arrows
        // to the DynamicArrows plane.
        return new CoveragePortrayalResult
        {
            SubLayers = new CoverageSubLayerBase[]
            {
                new ArrowCoverageSubLayer
                {
                    LayerKey = "s111.arrows",
                    LayerName = $"S-111 Arrows: {_fileName}",
                    Plane = S98DisplayPlane.DynamicArrows,
                    WithinPlanePriority = 10,
                    SourceFeatureType = "s111.arrows",
                    Coverage = styledLayer,
                    Viewport = viewport,
                    Palette = catalogue.ActivePalette,
                    BaseSymbolScale = context?.SymbolScale ?? 1.0,
                    SymbolProvider = symbolName =>
                        symbolSvgs.TryGetValue(symbolName, out var svg) ? svg : null,
                    FallbackExtent = new GeographicBounds(
                        metadata.Extent.WestLongitude,
                        metadata.Extent.SouthLatitude,
                        metadata.Extent.EastLongitude,
                        metadata.Extent.NorthLatitude),
                },
            },
            Spec = new SpecRef("S-111", default),
            SourceDatasetId = _fileName,
            Info = info,
            LayerNames = new[] { "s111.arrows" },
        };
    }

    /// <summary>
    /// Renders the gridded surface-current arrows to a standalone
    /// <see cref="SKBitmap"/> through the headless, Mapsui-free Skia coverage
    /// core (<see cref="CoverageHeadlessRenderer"/> +
    /// <see cref="SkiaCoverageArrowRenderer"/>). The selected time step is taken
    /// from <see cref="S111RenderContext.TimeStep"/> (defaulting to the first
    /// available step). Fixed-station / ungeorectified (dcf3 / dcf8) datasets
    /// emit point glyphs rather than a gridded coverage and therefore throw
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    /// <param name="widthPixels">Output bitmap width in pixels.</param>
    /// <param name="heightPixels">Output bitmap height in pixels.</param>
    /// <param name="context">Optional render context (palette, time step, symbol scale).</param>
    /// <param name="background">Optional background fill; defaults to opaque white.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown for dcf3 / dcf8 station-series datasets.
    /// </exception>
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

        if (_stationSeries is not null || _source is null || _catalogue is null || _provider is null)
            throw new NotSupportedException(
                "Headless rendering is not supported for S-111 fixed-station / " +
                "ungeorectified (data coding format 3 or 8) datasets; only " +
                "gridded surface-current coverages can be rendered to an image.");

        var source = _source;
        var catalogue = _catalogue;
        var provider = _provider;
        await catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);

        if (context is S111RenderContext { TimeStep: { } timeStep })
            source.SelectTime(timeStep);
        else
            source.SelectTime(source.AvailableTimes[0]);

        var styledLayer = (StyledCoverageLayer)await new PortrayalPipeline()
            .ProcessAsync(source, catalogue, context?.Mariner ?? MarinerSettings.Default, cancellationToken)
            .ConfigureAwait(false);

        var symbolSvgs = await PreWarmProviderSymbolsAsync(provider, cancellationToken).ConfigureAwait(false);

        var arrowRenderer = new SkiaCoverageArrowRenderer
        {
            Palette = catalogue.ActivePalette,
            BaseSymbolScale = context?.SymbolScale ?? 1.0,
            SymbolProvider = symbolName =>
                symbolSvgs.TryGetValue(symbolName, out var svg) ? svg : null,
        };

        var nativeToWgs84 = _crsTransformFactory.Create(
            styledLayer.Georeferencer.CRS, "EPSG:4326");

        var extent = source.Metadata.Extent;
        var renderer = new CoverageHeadlessRenderer
        {
            Background = background ?? new RgbaColor(255, 255, 255, 255),
            ArrowRenderer = arrowRenderer,
            NativeToWgs84 = nativeToWgs84,
        };

        return renderer.Render(
            styledLayer,
            extent.WestLongitude,
            extent.EastLongitude,
            extent.SouthLatitude,
            extent.NorthLatitude,
            widthPixels,
            heightPixels);
    }

    // ---- dcf8 station series rendering ---------------------------------

    /// <summary>
    /// Projects each station/node to a single point feature with an arrow
    /// glyph oriented along <c>DirectionsDegreesTrue</c> and scaled by
    /// speed magnitude. When the portrayal catalogue is loaded (DCF 3),
    /// colors and scale factors are resolved from the PC's speed-band
    /// table; otherwise (DCF 8) a hardcoded palette is used.
    /// Rebuilt per <see cref="S111RenderContext.TimeStep"/>.
    /// </summary>
    private async Task<CoveragePortrayalResult> BuildStationSeriesAsync(S111StationSeriesDataset ds, RenderContext? context, CancellationToken cancellationToken)
    {
        DateTime selectedTime;
        if (context is S111RenderContext { TimeStep: { } timeStep })
        {
            selectedTime = timeStep;
        }
        else
        {
            selectedTime = ds.MinTime ?? DateTime.UtcNow;
        }

        _stationSelectedTime = selectedTime;

        // Resolve PC schemes if available (DCF 3 with catalogue loaded)
        CoverageColorScheme? colorScheme = null;
        CoverageSymbolScheme? symbolScheme = null;
        Dictionary<string, string>? svgCache = null;
        Dictionary<string, string>? prewarmedSvgs = null;
        if (_catalogue is not null && _provider is not null)
        {
            await _catalogue.SwitchPaletteAsync(context?.Palette ?? PaletteType.Day, cancellationToken).ConfigureAwait(false);
            var mariner = context?.Mariner ?? MarinerSettings.Default;
            colorScheme = _catalogue.ResolveColorScheme(mariner);
            symbolScheme = _catalogue.ResolveSymbolScheme(mariner);
            svgCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            prewarmedSvgs = await PreWarmProviderSymbolsAsync(_provider, cancellationToken).ConfigureAwait(false);
        }

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
            var speed = station.SpeedsMetresPerSecond[idx];
            var direction = station.DirectionsDegreesTrue[idx];

            var attributes = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["StationId"] = station.Identifier,
                ["SpeedMetresPerSecond"] = speed,
                ["SpeedKnots"] = speed * 1.9438444924406046,
                ["DirectionDegreesTrue"] = direction,
                ["SampleTime"] = station.TimeAt(idx),
                ["Latitude"] = station.Latitude,
                ["Longitude"] = station.Longitude,
            };

            RgbaColor arrowColour;
            double symbolScale;
            string? svgSource = null;
            string? symbolRef = null;

            if (colorScheme is not null)
            {
                // PC-faithful rendering: resolve color from speed bands
                var hex = colorScheme.Resolve(speed);
                arrowColour = hex is not null
                    ? ParseHexColor(hex)
                    : new RgbaColor(0x80, 0x80, 0x80); // grey fallback for out-of-range

                // Use PC symbol scheme for scaling and SVG symbol if available
                if (symbolScheme is not null)
                {
                    var band = symbolScheme.Resolve(speed);
                    if (band is not null)
                    {
                        symbolRef = band.SymbolRef;
                        symbolScale = band.ScaleByValue
                            ? band.ScaleFactor * speed
                            : band.ScaleFactor;
                        // Clamp to reasonable visual range
                        symbolScale = Math.Clamp(symbolScale, 0.20, 2.0);
                    }
                    else
                    {
                        symbolScale = 0.30; // minimum visible
                    }
                }
                else
                {
                    symbolScale = SymbolScaleForSpeed(speed);
                }

                // Load SVG from PC if symbol ref resolved
                if (symbolRef is not null && svgCache is not null && _provider is not null)
                {
                    if (!svgCache.TryGetValue(symbolRef, out svgSource))
                    {
                        if (prewarmedSvgs is not null && prewarmedSvgs.TryGetValue(symbolRef, out var rawSvg))
                        {
                            // Process SVG through palette color resolver and
                            // wrap with the svg-content:// URI scheme that
                            // Mapsui's ImageStyle expects.
                            var processed = SvgProcessor.Process(rawSvg, _catalogue!.ActivePalette);
                            svgSource = "svg-content://" + processed;
                        }
                        svgCache[symbolRef] = svgSource ?? "";
                    }
                    if (string.IsNullOrEmpty(svgSource))
                        svgSource = null;
                }
            }
            else
            {
                // Fallback (DCF 8): hardcoded palette
                arrowColour = ColorByMagnitude(speed);
                symbolScale = SymbolScaleForSpeed(speed);
            }

            // Symbol orientation: Mapsui rotation is counter-clockwise from
            // east; compass bearing is clockwise from north. Negate to convert.
            if (svgSource is not null)
            {
                // PC SVG arrow symbol
                glyphs.Add(new PointGlyph
                {
                    MercatorX = mx,
                    MercatorY = my,
                    FeatureRefTag = StationFeatureRefPrefix + station.Identifier,
                    Attributes = attributes,
                    Symbol = PointGlyphSymbol.Svg,
                    SvgSource = svgSource,
                    SymbolScale = symbolScale * 0.6,
                    Rotation = -direction,
                });
            }
            else
            {
                // Triangle fallback
                glyphs.Add(new PointGlyph
                {
                    MercatorX = mx,
                    MercatorY = my,
                    FeatureRefTag = StationFeatureRefPrefix + station.Identifier,
                    Attributes = attributes,
                    Symbol = PointGlyphSymbol.Triangle,
                    FillColor = arrowColour,
                    OutlineColor = arrowColour,
                    OutlineWidth = 1.0,
                    SymbolScale = symbolScale,
                    Rotation = -direction,
                });
            }
        }

        var extent = ds.Stations.Count == 0
            ? (MercatorBounds?)null
            : new MercatorBounds(mercMinX, mercMinY, mercMaxX, mercMaxY);

        var dcfLabel = ds.DataCodingFormat == 3 ? "nodes" : "stations";
        var info = $"{ds.GeographicIdentifier ?? _fileName} — {ds.Stations.Count} {dcfLabel}, " +
                   $"time: {selectedTime:u}";

        return new CoveragePortrayalResult
        {
            // S-111 station glyphs (dcf3/dcf8) — point overlays on the
            // catch-all OtherChartOverlays plane.
            SubLayers = new CoverageSubLayerBase[]
            {
                new GlyphCoverageSubLayer
                {
                    LayerKey = "s111.stations",
                    LayerName = $"S-111: {_fileName}",
                    Plane = S98DisplayPlane.OtherChartOverlays,
                    WithinPlanePriority = 0,
                    SourceFeatureType = "s111.stations",
                    Glyphs = glyphs,
                    Extent = extent,
                },
            },
            Spec = new SpecRef("S-111", default),
            SourceDatasetId = _fileName,
            Info = info,
        };
    }

    /// <summary>
    /// Parses a hex color string (e.g. "#RRGGBB" or "#AARRGGBB") into
    /// an <see cref="RgbaColor"/>.
    /// </summary>
    private static RgbaColor ParseHexColor(string hex)
    {
        var span = hex.AsSpan();
        if (span.Length > 0 && span[0] == '#')
            span = span[1..];

        if (span.Length == 6)
        {
            byte r = byte.Parse(span[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new RgbaColor(r, g, b);
        }

        if (span.Length == 8)
        {
            byte a = byte.Parse(span[0..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte r = byte.Parse(span[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte g = byte.Parse(span[4..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            byte b = byte.Parse(span[6..8], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return new RgbaColor(r, g, b, a);
        }

        return new RgbaColor(0x80, 0x80, 0x80);
    }

    private static IReadOnlyList<DateTime> ComputeStationUnionTimes(S111StationSeriesDataset ds)
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
    /// Graduated Beaufort-style palette across 0..2.0 m/s with a hot
    /// overflow above 2.0 m/s.
    /// </summary>
    private static RgbaColor ColorByMagnitude(float speedMetresPerSecond)
    {
        if (float.IsNaN(speedMetresPerSecond) || speedMetresPerSecond < 0.25f)
            return new RgbaColor(0xcf, 0xe2, 0xf3); // very pale blue
        if (speedMetresPerSecond < 0.50f) return new RgbaColor(0x6f, 0xa8, 0xdc);
        if (speedMetresPerSecond < 1.00f) return new RgbaColor(0x3d, 0x85, 0xc6);
        if (speedMetresPerSecond < 1.50f) return new RgbaColor(0x1c, 0x45, 0x87);
        if (speedMetresPerSecond < 2.00f) return new RgbaColor(0xa6, 0x4d, 0x79);
        return new RgbaColor(0xc1, 0x12, 0x1f);
    }

    /// <summary>
    /// Maps a current speed to a Mapsui <see cref="SymbolStyle.SymbolScale"/>
    /// with a visible floor (~6 px) and ceiling (~24 px at 2 m/s).
    /// </summary>
    private static double SymbolScaleForSpeed(float speedMetresPerSecond)
    {
        const double minScale = 0.30; // ~6 px at default symbol size
        const double maxScale = 1.20; // ~24 px
        const double fastReference = 2.0;
        if (float.IsNaN(speedMetresPerSecond) || speedMetresPerSecond <= 0) return minScale;
        var t = Math.Clamp(speedMetresPerSecond / fastReference, 0.0, 1.0);
        return minScale + (maxScale - minScale) * t;
    }

    /// <summary>
    /// Resolves dcf8 station picks routed via the Mapsui
    /// <see cref="MapsuiDisplayListRenderer.FeatureRefKey"/> tag the
    /// arrow layer attaches to each station point. Refs are formatted as
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

    private FeatureInfo BuildStationFeatureInfo(SurfaceCurrentStation station, DateTime? time)
    {
        var selectedTime = time ?? station.StartTime;
        int idx = station.NearestTimeIndex(selectedTime);
        var speed = station.SpeedsMetresPerSecond[idx];
        var direction = station.DirectionsDegreesTrue[idx];
        var sampleTime = station.TimeAt(idx);
        var speedKnots = speed * 1.9438444924406046;

        return new FeatureInfo
        {
            FeatureRef = StationFeatureRefPrefix + station.Identifier,
            FeatureType = "SurfaceCurrent",
            FeatureTypeName = "Surface Current (Station)",
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
                    Code = "surfaceCurrentSpeed",
                    Name = "Current Speed",
                    RawValue = speed.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{speed.ToString("0.##", CultureInfo.InvariantCulture)} m/s ({speedKnots.ToString("0.##", CultureInfo.InvariantCulture)} kn)",
                },
                new()
                {
                    Code = "surfaceCurrentDirection",
                    Name = "Current Direction (going to)",
                    RawValue = direction.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{direction.ToString("0.#", CultureInfo.InvariantCulture)}°",
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

    private static StationTimeSeriesSnapshot BuildStationSeriesSnapshot(SurfaceCurrentStation station)
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
                    Key = "surfaceCurrentSpeed",
                    DisplayName = "Surface Current Speed",
                    Unit = "m/s",
                    Values = station.SpeedsMetresPerSecond,
                    FillValue = -9999f,
                },
                new StationTimeSeriesChannel
                {
                    Key = "surfaceCurrentDirection",
                    DisplayName = "Surface Current Direction",
                    Unit = "°",
                    Values = station.DirectionsDegreesTrue,
                    FillValue = -9999f,
                },
            },
        };
    }

    /// <summary>
    /// Samples the surface-current grid at the supplied geographic
    /// position and time. <paramref name="time"/> is matched to the
    /// nearest available time step; when <c>null</c> the first
    /// available step is used. The reported direction is "going to"
    /// in degrees true (S-111 Edition 2.0.0 §10.2).
    /// </summary>
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

        var speed = sample.Values.TryGetValue("surfaceCurrentSpeed", out var s) ? s : sample.NoDataValue;
        var direction = sample.Values.TryGetValue("surfaceCurrentDirection", out var dir) ? dir : sample.NoDataValue;

        var speedUnit = source.Metadata.ValueFields
            .FirstOrDefault(f => string.Equals(f.Name, "surfaceCurrentSpeed", StringComparison.Ordinal))
            ?.Units ?? "knots";

        var attrs = new List<PickAttribute>
        {
            new()
            {
                Code = "surfaceCurrentSpeed",
                Name = "Current Speed",
                RawValue = speed == sample.NoDataValue
                    ? "NoData"
                    : speed.ToString("0.##########", CultureInfo.InvariantCulture),
                DisplayValue = speed == sample.NoDataValue
                    ? "—"
                    : $"{speed.ToString("0.##", CultureInfo.InvariantCulture)} {speedUnit}",
            },
            new()
            {
                Code = "surfaceCurrentDirection",
                Name = "Current Direction (going to)",
                RawValue = direction == sample.NoDataValue
                    ? "NoData"
                    : direction.ToString("0.##########", CultureInfo.InvariantCulture),
                DisplayValue = direction == sample.NoDataValue
                    ? "—"
                    : $"{direction.ToString("0.#", CultureInfo.InvariantCulture)}°",
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
            FeatureType = "SurfaceCurrent",
            FeatureTypeName = "Surface Current",
            Attributes = attrs,
        };
    }

    private FeatureInfo? GetStationInfo(
        S111StationSeriesDataset ds,
        double latitude,
        double longitude,
        DateTime? time)
    {
        if (ds.Stations.Count == 0) return null;

        SurfaceCurrentStation? best = null;
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
        var speed = best.SpeedsMetresPerSecond[idx];
        var direction = best.DirectionsDegreesTrue[idx];
        var sampleTime = best.TimeAt(idx);
        var speedKnots = speed * 1.9438444924406046;

        return new FeatureInfo
        {
            FeatureRef = best.Identifier,
            FeatureType = "SurfaceCurrent",
            FeatureTypeName = "Surface Current (Station)",
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
                    Code = "surfaceCurrentSpeed",
                    Name = "Current Speed",
                    RawValue = speed.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{speed.ToString("0.##", CultureInfo.InvariantCulture)} m/s ({speedKnots.ToString("0.##", CultureInfo.InvariantCulture)} kn)",
                },
                new()
                {
                    Code = "surfaceCurrentDirection",
                    Name = "Current Direction (going to)",
                    RawValue = direction.ToString("0.##########", CultureInfo.InvariantCulture),
                    DisplayValue = $"{direction.ToString("0.#", CultureInfo.InvariantCulture)}°",
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

    /// <summary>
    /// Runs the V-3 S-111 validation rule pack
    /// (<see cref="S111SurfaceCurrentRules.Default"/>) against the
    /// gridded <see cref="S111Dataset"/> view of this processor's
    /// dataset, or returns a single-finding
    /// <c>S111-PROJ-UNSUPPORTED</c> report when the underlying dataset
    /// is the station-series (<see cref="S111DatasetData.StationSeries"/>)
    /// variant (dcf 3 ungeorectified grid or dcf 8 time series at fixed
    /// stations, both of which the rule pack does not cover).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Per <c>docs/design/non-gml-validation.md</c> §5.1 and §5.3,
    /// this override surfaces reader-time projection diagnostics under
    /// reserved rule ids:
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>S111-PROJ-SCHEMA</c> — defensive try/catch for
    /// <see cref="S100DatasetSchemaException"/>. The realistic failure
    /// mode is the constructor itself throwing, so this only fires if
    /// a future reader change moves schema validation later in the
    /// pipeline.</description></item>
    /// <item><description><c>S111-PROJ-UNSUPPORTED</c> — surfaced
    /// proactively when the loaded dataset is a
    /// <see cref="S111DatasetData.StationSeries"/> (S-111 dcf 3 or
    /// dcf 8) because the V-3 rule pack operates on
    /// <see cref="S111Dataset"/> (the gridded view) and not on the
    /// station-series shape. Also surfaced defensively if rule
    /// evaluation ever throws an
    /// <see cref="S100DatasetNotSupportedException"/>.</description></item>
    /// </list>
    /// <para>
    /// Validation is a pure function of the parsed dataset; the
    /// report is cached after the first call (mirroring the V-1
    /// S-102 and V-2 S-104 processors).
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
            case S111DatasetData.GriddedCoverage g:
                try
                {
                    return S111SurfaceCurrentRules.Default.Run(g.Dataset);
                }
                catch (S100DatasetSchemaException ex)
                {
                    return BuildSchemaSurrogateReport(ex);
                }
                catch (S100DatasetNotSupportedException ex)
                {
                    return BuildUnsupportedSurrogateReport(ex);
                }

            case S111DatasetData.StationSeries s:
                return BuildStationSeriesUnsupportedReport(s.Dataset.DataCodingFormat);

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
            RuleId = "S111-PROJ-SCHEMA",
            Severity = ValidationSeverity.Error,
            Message = $"S111 reader raised S100DatasetSchemaException: {ex.Message} ({string.Join(", ", details)}).",
            RelatedFeatureId = ex.GroupPath,
        };

        return new ValidationReport(
            ImmutableArray.Create(finding),
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
            RuleId = "S111-PROJ-UNSUPPORTED",
            Severity = ValidationSeverity.Error,
            Message = $"S111 reader raised S100DatasetNotSupportedException: {ex.Message} ({string.Join(", ", details)}).",
        };

        return new ValidationReport(
            ImmutableArray.Create(finding),
            RulesEvaluated: 1,
            RulesWithFindings: 1);
    }

    private ValidationReport BuildStationSeriesUnsupportedReport(int dataCodingFormat)
    {
        // The reader produced a StationSeries variant (dcf 3 ungeorectified
        // grid or dcf 8 time series at fixed stations). The V-3 rule pack
        // targets the gridded S111Dataset view (dcf 2) only, so surface
        // this proactively under S111-PROJ-UNSUPPORTED per
        // docs/design/non-gml-validation.md §5.3.
        string formatLabel = dataCodingFormat switch
        {
            3 => "data coding format 3 (ungeorectified grid)",
            8 => "data coding format 8 (time series at fixed stations)",
            _ => $"data coding format {dataCodingFormat} (station-series projection)",
        };

        var finding = new ValidationFinding
        {
            RuleId = "S111-PROJ-UNSUPPORTED",
            Severity = ValidationSeverity.Error,
            Message =
                $"S111 dataset '{_fileName}' uses {formatLabel}. The V-3 S-111 rule pack " +
                "targets the gridded (dcf 2) S111Dataset shape and does not currently " +
                "evaluate station-series datasets " +
                "(S-100 Part 10c §10.2.1; S-111 Edition 2.0.0 §10.2.3 / §10.2.7).",
            RelatedFeatureId = "/SurfaceCurrent",
        };

        return new ValidationReport(
            ImmutableArray.Create(finding),
            RulesEvaluated: 1,
            RulesWithFindings: 1);
    }

    /// <summary>
    /// Pre-warms every symbol declared by the given portrayal catalogue
    /// provider into a name → SVG-content dict so the synchronous arrow
    /// renderer's <c>SymbolProvider</c> lambda can resolve symbols without
    /// async I/O. Bounded to the catalogue's symbol manifest, which is small.
    /// </summary>
    private static async Task<Dictionary<string, string>> PreWarmProviderSymbolsAsync(
        PortrayalCatalogueProvider provider, CancellationToken cancellationToken)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in provider.Catalogue.Symbols)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using var stream = await provider.FetchAssetAsync(item, "Symbols", cancellationToken).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                dict[item.Id] = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Skip symbols that fail to load; the renderer treats missing
                // entries the same as before — no symbol rendered for that name.
            }
        }
        return dict;
    }
}
