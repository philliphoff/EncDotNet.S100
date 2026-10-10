using System.Collections.Concurrent;
using EncDotNet.S100.Cli.Commands;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;
using Spectre.Console;

namespace EncDotNet.S100.Cli.Infrastructure.Tiles;

/// <summary>The EPSG:3857 area to tile; <c>MaxX</c> may lie past +180° for data crossing the antimeridian.</summary>
internal readonly record struct TileArea(double MinX, double MinY, double MaxX, double MaxY);

/// <summary>
/// Where and how a tile set is rendered: the area, the zoom range, and the
/// latitude each zoom level's scale is measured at.
/// </summary>
internal sealed record TileLayout(TileArea Area, double ReferenceLatitude, int MinZoom, int MaxZoom, HeadlessTileOptions TileOptions)
{
    /// <summary>The WGS-84 bounds of the area, the full longitude range when it wraps the antimeridian.</summary>
    public (double West, double South, double East, double North) Bounds
    {
        get
        {
            var (west, south) = WebMercator.ToLonLat(Area.MinX, Area.MinY);
            var (east, north) = WebMercator.ToLonLat(Area.MaxX, Area.MaxY);
            if (Area.MaxX - Area.MinX >= WebMercator.Circumference || Area.MinX < -XyzTileGrid.Extent || Area.MaxX > XyzTileGrid.Extent)
                return (-180, south, 180, north);
            return (west, south, east, north);
        }
    }
}

/// <summary>
/// Datasets opened for tiling (issues #847, #865): the processors
/// <c>tiles export</c> and <c>tiles serve</c> portray, and the
/// <see cref="TileScene"/> each palette is rendered from.
/// </summary>
internal sealed class TileRenderSession : IDisposable
{
    /// <summary>The deepest zoom a default (derived) zoom range reaches.</summary>
    private const int DefaultZoomCeiling = 18;

    private readonly TilesRenderSettings _settings;
    private readonly IDisposable _catalogueManager;
    private readonly ExchangeSetLayerResolution? _resolution;
    private readonly List<IDatasetProcessor> _processors;

    // Portrayal is serialised: the processors are shared by every palette's
    // scene and every zoom level's coverage portrayal.
    private readonly Lock _portrayGate = new();

    private TileRenderSession(
        TilesRenderSettings settings,
        IDisposable catalogueManager,
        ExchangeSetLayerResolution? resolution,
        List<IDatasetProcessor> processors,
        IReadOnlyList<string> specs)
    {
        _settings = settings;
        _catalogueManager = catalogueManager;
        _resolution = resolution;
        _processors = processors;
        Specs = specs;
        TryParseImage(settings, out var format);
        Format = format;
    }

    /// <summary>The product specification of each dataset, in input order.</summary>
    public IReadOnlyList<string> Specs { get; }

    /// <summary>The tile encoding.</summary>
    public TileImageFormat Format { get; }

    /// <summary>
    /// Opens and checks the datasets <paramref name="settings"/> names. On
    /// failure, prints why and returns a non-zero exit code with
    /// <paramref name="session"/> <see langword="null"/>.
    /// </summary>
    public static int TryOpen(TilesRenderSettings settings, out TileRenderSession? session)
    {
        session = null;
        var (factory, catalogueManager) = ProcessorFactoryBuilder.Build();
        ExchangeSetLayerResolution? resolution = null;
        var processors = new List<IDatasetProcessor>();
        bool opened = false;
        try
        {
            IReadOnlyList<(string Path, string Spec)> inputs;
            bool applyUpdates = !settings.NoUpdates;
            if (settings.IsExchangeSet)
            {
                IReadOnlySet<string>? only = null;
                if (settings.Only is not null && RenderCommand.TryParseOnlySpecs(settings.Only, out var specs))
                    only = specs;

                resolution = ExchangeSetLayerResolution.Resolve(settings.ExchangeSetSource!, only);
                foreach (var warning in resolution.Warnings)
                    Console.Error.WriteLine(warning);
                inputs = resolution.Layers.Select(l => (l.Path, l.Spec)).ToList();
                applyUpdates = false;
            }
            else
            {
                var paths = settings.IsComposite ? settings.Layers : [settings.Input!];
                var resolved = new List<(string, string)>();
                foreach (var path in paths)
                {
                    var spec = DatasetPipelineFactory.DetectProductSpec(path);
                    if (spec is null)
                    {
                        AnsiConsole.MarkupLineInterpolated(
                            $"[red]Could not detect an S-100 product specification for:[/] {path}");
                        return 2;
                    }

                    resolved.Add((path, spec));
                }

                inputs = resolved;
            }

            if (inputs.Count == 0)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]No renderable datasets were found in:[/] {settings.ExchangeSetSource}");
                return 2;
            }

            if (!string.IsNullOrWhiteSpace(settings.DisplayMode)
                && !inputs.Any(i => i.Spec.Equals("S-411", StringComparison.OrdinalIgnoreCase)))
            {
                AnsiConsole.MarkupLine("[red]--display-mode is only supported for S-411 sea-ice datasets; none of the inputs is S-411.[/]");
                return 2;
            }

            foreach (var (path, spec) in inputs)
            {
                var processor = DatasetProcessorLoader.Create(factory, spec, path, noUpdates: !applyUpdates);
                processors.Add(processor);
                if (processor.VersionAssessment?.IsWarning == true)
                    Console.Error.WriteLine(processor.VersionAssessment.BuildMessage());
                if (processor is not (IVectorPortrayalSource or ICoveragePortrayalSource))
                {
                    AnsiConsole.MarkupLineInterpolated($"[red]Tiling is not supported for {spec}:[/] {path}");
                    return 3;
                }
            }

            session = new TileRenderSession(settings, catalogueManager, resolution, processors, inputs.Select(i => i.Spec).ToList());
            opened = true;
            return 0;
        }
        finally
        {
            if (!opened)
            {
                foreach (var processor in processors)
                    (processor as IDisposable)?.Dispose();
                resolution?.Dispose();
                catalogueManager.Dispose();
            }
        }
    }

    /// <summary>
    /// Portrays the datasets in <paramref name="palette"/> (or the
    /// <c>--palette</c> option) and prepares the composite each zoom level is
    /// rendered from.
    /// </summary>
    public TileScene Prepare(string? palette = null)
    {
        lock (_portrayGate)
            return PrepareLocked(palette);
    }

    private TileScene PrepareLocked(string? palette)
    {
        RenderCommand.TryParsePalette(palette ?? _settings.Palette, out var parsedPalette);
        RenderCommand.TryParseBasemap(_settings.Basemap, out var basemap);
        RenderCommand.TryParseDisplayMode(_settings.DisplayMode, out var displayModeId);
        var hidden = HiddenCategories(_settings);

        RenderContext ContextFor(IDatasetProcessor processor, Viewport? viewport) =>
            RenderContextBuilder.Build(
                processor, parsedPalette, _settings.SymbolScale, _settings.TextScale, _settings.TimeStep, hidden,
                displayModeId: displayModeId, viewport: viewport);

        // Vector products portray once, independent of the viewport; coverage
        // products portray here without a viewport (the full grid) to find the
        // extent, and again for each zoom level (TileScene.RendererFor).
        var inputs = new HeadlessCompositeInput[_processors.Count];
        var vectorResults = new List<VectorPortrayalResult>();
        bool hasCoverage = false;
        for (int i = 0; i < _processors.Count; i++)
        {
            var processor = _processors[i];
            if (processor is IVectorPortrayalSource vectorSource)
            {
                var result = vectorSource.BuildVectorPortrayalAsync(ContextFor(processor, null)).GetAwaiter().GetResult();
                vectorResults.Add(result);
                inputs[i] = HeadlessCompositeInput.ForVector(result);
            }
            else
            {
                var coverageSource = (ICoveragePortrayalSource)processor;
                var result = coverageSource.BuildCoveragePortrayalAsync(ContextFor(processor, null)).GetAwaiter().GetResult();
                inputs[i] = HeadlessCompositeInput.ForCoverage(result);
                hasCoverage = true;
            }
        }

        var compositor = new HeadlessCompositor(new ProjNetCrsTransformFactory());
        var options = new HeadlessCompositeOptions
        {
            Background = Background(_settings),
            Mariner = MarinerSettings.Default,
            HiddenCategories = hidden,
            Basemap = basemap,
            HonorScaleVisibility = true,
            EnableSeamWrap = false,
        };

        return new TileScene(_processors, inputs, vectorResults, hasCoverage, compositor, options, ContextFor, _portrayGate);
    }

    /// <summary>
    /// Resolves the area to tile and its zoom range from a prepared scene, or
    /// <see langword="null"/> when the datasets have no geometry and no
    /// <c>--bbox</c> is given.
    /// </summary>
    public TileLayout? ResolveLayout(TileScene scene)
    {
        if (!TryResolveArea(_settings, scene.Scene, out var area))
            return null;

        double referenceLatitude = WebMercator.ToLonLat(0, (area.MinY + area.MaxY) / 2.0).Latitude;
        var (minZoom, maxZoom) = ResolveZoomRange(_settings, scene.VectorResults, area, referenceLatitude);
        var tileOptions = new HeadlessTileOptions
        {
            PixelRatio = _settings.TileSize / XyzTileGrid.TileSize,
            ReferenceLatitude = referenceLatitude,
            Background = Background(_settings),
        };
        return new TileLayout(area, referenceLatitude, minZoom, maxZoom, tileOptions);
    }

    /// <summary>Encodes a rendered tile in the session's format and quality.</summary>
    public byte[] Encode(HeadlessTile tile)
    {
        var encodeFormat = Format switch
        {
            TileImageFormat.Jpeg => SKEncodedImageFormat.Jpeg,
            TileImageFormat.Webp => SKEncodedImageFormat.Webp,
            _ => SKEncodedImageFormat.Png,
        };
        using var data = tile.Bitmap.Encode(encodeFormat, _settings.Quality)
            ?? throw new NotSupportedException($"SkiaSharp could not encode a tile as {encodeFormat} on this platform.");
        return data.ToArray();
    }

    /// <summary>The display settings baked into the tiles, as tile-set metadata records them.</summary>
    public Dictionary<string, string> DescribeSettings(double referenceLatitude, string? palette = null)
    {
        var description = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["generator"] = "s100 " + CliVersionInfo.FromAssembly(typeof(TileRenderSession).Assembly).InformationalVersion,
            ["palette"] = (palette ?? _settings.Palette).Trim().ToLowerInvariant(),
            ["symbolScale"] = _settings.SymbolScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["textScale"] = _settings.TextScale.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["timeStep"] = _settings.TimeStep.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["basemap"] = _settings.Basemap.Trim().ToLowerInvariant(),
            ["scaleLatitude"] = referenceLatitude.ToString("F4", System.Globalization.CultureInfo.InvariantCulture),
        };
        var hidden = HiddenCategories(_settings);
        if (hidden != DrawingInstructionCategory.None)
            description["hidden"] = hidden.ToString();
        if (!string.IsNullOrWhiteSpace(_settings.DisplayMode))
            description["displayMode"] = _settings.DisplayMode.Trim().ToLowerInvariant();
        return description;
    }

    public void Dispose()
    {
        foreach (var processor in _processors)
            (processor as IDisposable)?.Dispose();
        _resolution?.Dispose();
        _catalogueManager.Dispose();
    }

    /// <summary>
    /// Resolves the zoom range: explicit options win; otherwise the coarsest
    /// cell minimum display scale sets the lowest zoom and one level past the
    /// finest compilation scale sets the highest, falling back to the extent
    /// (fit in about one tile, then six levels deeper) for data without them.
    /// </summary>
    internal static (int Min, int Max) ResolveZoomRange(
        TilesRenderSettings settings, IReadOnlyList<VectorPortrayalResult> vectorResults, TileArea area, double referenceLatitude)
    {
        double span = Math.Max(area.MaxX - area.MinX, area.MaxY - area.MinY);
        int extentZoom = span > 0
            ? Math.Clamp((int)Math.Floor(Math.Log2(2.0 * XyzTileGrid.Extent / span)), 0, XyzTileGrid.MaxZoom)
            : DefaultZoomCeiling;

        var minimumScales = vectorResults
            .Select(r => r.CellMinimumDisplayScale)
            .OfType<int>()
            .Where(s => s > 0)
            .ToList();
        var compilationScales = vectorResults
            .Select(r => r.CellCompilationScale ?? r.CellMinimumDisplayScale)
            .OfType<int>()
            .Where(s => s > 0)
            .ToList();

        int defaultMin = minimumScales.Count > 0
            ? XyzTileGrid.ZoomForScaleDenominator(minimumScales.Max(), referenceLatitude)
            : extentZoom;
        int defaultMax = compilationScales.Count > 0
            ? XyzTileGrid.ZoomForScaleDenominator(compilationScales.Min(), referenceLatitude) + 1
            : Math.Min(defaultMin + 6, DefaultZoomCeiling);

        int min = settings.MinZoom ?? Math.Min(defaultMin, settings.MaxZoom ?? defaultMin);
        int max = settings.MaxZoom ?? Math.Max(Math.Min(defaultMax, XyzTileGrid.MaxZoom), min);
        return (min, Math.Max(min, max));
    }

    private static bool TryResolveArea(TilesRenderSettings settings, HeadlessCompositeScene scene, out TileArea area)
    {
        if (settings.BoundingBox is not null
            && CompositeViewportBuilder.TryParseDoubles(settings.BoundingBox, 4, out var bb))
        {
            var (minX, minY) = WebMercator.FromLonLat(bb[0], Math.Max(bb[1], -WebMercator.MaxLatitude));
            var (maxX, maxY) = WebMercator.FromLonLat(bb[2], Math.Min(bb[3], WebMercator.MaxLatitude));
            area = new TileArea(minX, minY, maxX, maxY);
            return true;
        }

        if (scene.TryGetWorldBounds(out double sMinX, out double sMinY, out double sMaxX, out double sMaxY))
        {
            area = new TileArea(
                sMinX,
                Math.Max(sMinY, -XyzTileGrid.Extent),
                sMaxX,
                Math.Min(sMaxY, XyzTileGrid.Extent));
            return true;
        }

        area = default;
        return false;
    }

    private static DrawingInstructionCategory HiddenCategories(TilesRenderSettings settings)
    {
        var hidden = DrawingInstructionCategory.None;
        if (settings.Hide is not null && RenderCommand.TryParseHideCategories(settings.Hide, out var parsed, out _))
            hidden |= parsed;
        if (settings.NoText)
            hidden |= DrawingInstructionCategory.Text;
        return hidden;
    }

    private static RgbaColor Background(TilesRenderSettings settings) =>
        settings.Background is not null && RenderCommand.TryParseHexColor(settings.Background, out var color)
            ? color
            : RgbaColor.Transparent;

    private static void TryParseImage(TilesRenderSettings settings, out TileImageFormat format) =>
        TilesExportCommand.TryParseImageFormat(settings.Format, out format);
}

/// <summary>
/// The datasets portrayed in one palette, prepared as a composite. Hands out a
/// <see cref="HeadlessTileRenderer"/> per zoom level, portraying coverage
/// products again for each so grid sampling and arrow density suit it.
/// <see cref="RendererFor"/> may be called concurrently.
/// </summary>
internal sealed class TileScene
{
    /// <summary>The widest coverage portrayal viewport per axis, in pixels.</summary>
    private const int MaxCoverageViewportPixels = 1 << 20;

    private readonly IReadOnlyList<IDatasetProcessor> _processors;
    private readonly HeadlessCompositeInput[] _inputs;
    private readonly bool _hasCoverage;
    private readonly HeadlessCompositor _compositor;
    private readonly HeadlessCompositeOptions _options;
    private readonly Func<IDatasetProcessor, Viewport?, RenderContext> _contextFor;
    private readonly Lock _portrayGate;
    private readonly ConcurrentDictionary<int, Lazy<HeadlessTileRenderer>> _renderers = new();

    internal TileScene(
        IReadOnlyList<IDatasetProcessor> processors,
        HeadlessCompositeInput[] inputs,
        IReadOnlyList<VectorPortrayalResult> vectorResults,
        bool hasCoverage,
        HeadlessCompositor compositor,
        HeadlessCompositeOptions options,
        Func<IDatasetProcessor, Viewport?, RenderContext> contextFor,
        Lock portrayGate)
    {
        _processors = processors;
        _inputs = inputs;
        VectorResults = vectorResults;
        _hasCoverage = hasCoverage;
        _compositor = compositor;
        _options = options;
        _contextFor = contextFor;
        _portrayGate = portrayGate;
        Scene = compositor.Prepare(inputs, options);
    }

    /// <summary>The composite of the datasets' full-extent portrayal.</summary>
    public HeadlessCompositeScene Scene { get; }

    /// <summary>The vector datasets' portrayal results (for their display scales).</summary>
    public IReadOnlyList<VectorPortrayalResult> VectorResults { get; }

    /// <summary>The renderer for <paramref name="zoom"/>, created once per zoom level.</summary>
    public HeadlessTileRenderer RendererFor(int zoom, TileLayout layout) =>
        _renderers.GetOrAdd(zoom, z => new Lazy<HeadlessTileRenderer>(() => new HeadlessTileRenderer(
            _hasCoverage ? _compositor.Prepare(PortrayCoverageForZoom(layout, z), _options) : Scene,
            layout.TileOptions))).Value;

    /// <summary>
    /// Re-portrays each coverage input for one zoom level, against a viewport
    /// covering the whole tiled area at that zoom's resolution, so grid sampling
    /// and arrow density match the tiles. Vector inputs are reused.
    /// </summary>
    private HeadlessCompositeInput[] PortrayCoverageForZoom(TileLayout layout, int zoom)
    {
        var area = layout.Area;
        double resolution = XyzTileGrid.Resolution(zoom);
        var (minLon, minLat) = WebMercator.ToLonLat(area.MinX, area.MinY, clampLatitude: false);
        var (maxLon, maxLat) = WebMercator.ToLonLat(area.MaxX, area.MaxY, clampLatitude: false);
        var viewport = new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = (int)Math.Clamp(Math.Ceiling((area.MaxX - area.MinX) / resolution), 1, MaxCoverageViewportPixels),
            HeightPixels = (int)Math.Clamp(Math.Ceiling((area.MaxY - area.MinY) / resolution), 1, MaxCoverageViewportPixels),
            ScaleDenominator = XyzTileGrid.ScaleDenominator(zoom, layout.ReferenceLatitude),
        };

        var zoomInputs = _inputs.ToArray();
        lock (_portrayGate)
        {
            for (int i = 0; i < _processors.Count; i++)
            {
                if (_processors[i] is ICoveragePortrayalSource coverageSource)
                {
                    var result = coverageSource.BuildCoveragePortrayalAsync(_contextFor(_processors[i], viewport))
                        .GetAwaiter().GetResult();
                    zoomInputs[i] = HeadlessCompositeInput.ForCoverage(result);
                }
            }
        }

        return zoomInputs;
    }
}
