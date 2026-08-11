using System.Diagnostics;
using System.Reflection;
using EncDotNet.S100.Crs.ProjNet;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting.MoonSharp;
using EncDotNet.S100.Specifications;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using SkiaSharp;

namespace EncDotNet.S100.VisualRegression;

/// <summary>
/// Headless rendering harness for S-100 datasets. Drives the same pipelines and
/// renderers as the Avalonia viewer, but rasterises to an <see cref="SKBitmap"/>
/// without a UI so that rendering can be exercised in unit tests.
/// </summary>
/// <remarks>
/// <para>
/// The harness bootstraps a <see cref="PortrayalCatalogueManager"/> using the
/// portrayal catalogues bundled in <c>EncDotNet.S100.Specifications</c>, and
/// resolves feature catalogues from the same source. Callers can override either
/// by constructing a harness with a pre-configured catalogue manager and / or
/// feature-catalogue resolver.
/// </para>
/// <para>
/// Rendering proceeds in three stages:
/// </para>
/// <list type="number">
///   <item>The harness picks the right <see cref="IDatasetProcessor"/> via
///         <see cref="DatasetPipelineFactory"/> (same code path as the viewer).</item>
///   <item>It invokes <see cref="MapsuiDatasetRenderer.RenderAsync"/> with a spec-specific
///         <see cref="RenderContext"/> derived from <see cref="HarnessOptions"/>.</item>
///   <item>The resulting Mapsui <see cref="ILayer"/>s are dropped into a
///         <see cref="Map"/>, the viewport is zoomed to the dataset extent, and
///         <see cref="MapRenderer.RenderToBitmapStream(Map, float, RenderFormat, int)"/>
///         produces a PNG byte stream which is decoded to an <see cref="SKBitmap"/>.</item>
/// </list>
/// </remarks>
public sealed class RenderHarness : IDisposable
{
    private readonly PortrayalCatalogueManager _catalogueManager;
    private readonly bool _ownsCatalogueManager;
    private readonly DatasetPipelineFactory _factory;
    private readonly MapsuiDatasetRenderer _mapsuiRenderer;

    /// <summary>
    /// Creates a new harness with all bundled portrayal catalogues registered.
    /// </summary>
    public RenderHarness()
        : this(CreateDefaultCatalogueManager(), ownsCatalogueManager: true,
               featureCatalogueResolver: Specification.TryOpenFeatureCatalogue)
    {
    }

    /// <summary>
    /// Creates a new harness with a caller-supplied catalogue manager and feature
    /// catalogue resolver.
    /// </summary>
    public RenderHarness(
        PortrayalCatalogueManager catalogueManager,
        Func<string, Stream?>? featureCatalogueResolver = null)
        : this(catalogueManager, ownsCatalogueManager: false,
               featureCatalogueResolver ?? Specification.TryOpenFeatureCatalogue)
    {
    }

    private RenderHarness(
        PortrayalCatalogueManager catalogueManager,
        bool ownsCatalogueManager,
        Func<string, Stream?> featureCatalogueResolver)
    {
        _catalogueManager = catalogueManager;
        _ownsCatalogueManager = ownsCatalogueManager;

        var featureCatalogueManager =
            new EncDotNet.S100.Features.FeatureCatalogueManager(featureCatalogueResolver);

        _factory = new DatasetPipelineFactory(
            _catalogueManager,
            new MoonSharpLuaEngine(),
            new ProjNetCrsTransformFactory(),
            featureCatalogueManager,
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.DisplayPlaneAuthorityProvider());
        S100MapsuiRendering.Register();
        _mapsuiRenderer = new MapsuiDatasetRenderer(new ProjNetCrsTransformFactory());
    }

    /// <summary>
    /// Loads the dataset at <paramref name="path"/>, runs it through its pipeline,
    /// and returns the rendered bitmap. Caller owns the returned bitmap.
    /// </summary>
    public SKBitmap Render(string path, HarnessOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= HarnessOptions.Default;

        var prior = RenderingOptimizations.RenderSubsystem;
        try
        {
            RenderingOptimizations.RenderSubsystem = options.RenderSubsystem;

            var processor = _factory.CreateProcessor(path);
            var context = BuildContext(processor, options);
            var result = _mapsuiRenderer.RenderAsync(processor, context).GetAwaiter().GetResult();

            return Rasterize(result, options);
        }
        finally
        {
            RenderingOptimizations.RenderSubsystem = prior;
        }
    }

    /// <summary>
    /// Same as <see cref="Render"/> but also returns the <see cref="MapsuiDatasetResult"/>
    /// produced by the pipeline (useful when a test wants to assert against
    /// <see cref="MapsuiDatasetResult.Info"/> or similar).
    /// </summary>
    public (SKBitmap Bitmap, MapsuiDatasetResult Result) RenderWithResult(
        string path, HarnessOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= HarnessOptions.Default;

        var prior = RenderingOptimizations.RenderSubsystem;
        try
        {
            RenderingOptimizations.RenderSubsystem = options.RenderSubsystem;

            var processor = _factory.CreateProcessor(path);
            var context = BuildContext(processor, options);
            var result = _mapsuiRenderer.RenderAsync(processor, context).GetAwaiter().GetResult();

            return (Rasterize(result, options), result);
        }
        finally
        {
            RenderingOptimizations.RenderSubsystem = prior;
        }
    }

    /// <summary>
    /// Runs the dataset pipeline and Mapsui layer build for <paramref name="path"/>
    /// under the subsystem selected in <paramref name="options"/> and returns the
    /// produced layers <b>without rasterising a frame</b>. Used by fidelity tests
    /// that inspect the bound tiled <c>VectorScene</c> (via
    /// <see cref="S100VectorTileRenderer.TryGetPartitionedScene"/>) at the paint-op
    /// level rather than through pixels — see the issue #347 multi-product parity
    /// guard. Returns the layers together with the dataset extent.
    /// </summary>
    public (IReadOnlyList<ILayer> Layers, MRect Extent) BuildLayers(
        string path, HarnessOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= HarnessOptions.Default;

        var prior = RenderingOptimizations.RenderSubsystem;
        try
        {
            RenderingOptimizations.RenderSubsystem = options.RenderSubsystem;

            var processor = _factory.CreateProcessor(path);
            var context = BuildContext(processor, options);
            var result = _mapsuiRenderer.RenderAsync(processor, context).GetAwaiter().GetResult();

            return (result.Layers.ToList(), result.Extent);
        }
        finally
        {
            RenderingOptimizations.RenderSubsystem = prior;
        }
    }

    private static RenderContext BuildContext(IDatasetProcessor processor, HarnessOptions options)
    {
        var palette = options.Palette;
        var symScale = options.SymbolScale;
        var txtScale = options.TextScale;
        var ecdis = options.DisplayCategory is { } category
            ? new EcdisDisplaySettings { Category = category }
            : null;

        // Time-series specs need a DateTime resolved from the time-step index.
        DateTime? timeStep = null;
        if (options.TimeStepIndex > 0 || processor.Spec.Name is "S-104" or "S-111")
        {
            // Reach for the AvailableTimes property (publicly defined on the
            // concrete S104/S111 processors) without a hard reference.
            var times = processor.GetType().GetProperty(
                "AvailableTimes",
                BindingFlags.Public | BindingFlags.Instance)?.GetValue(processor)
                as IReadOnlyList<DateTime>;
            if (times is not null && times.Count > 0)
            {
                int idx = Math.Clamp(options.TimeStepIndex, 0, times.Count - 1);
                timeStep = times[idx];
            }
        }

        return processor.Spec.Name switch
        {
            "S-101" => new S101RenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale, EcdisDisplay = ecdis },
            "S-102" => new S102RenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale, EcdisDisplay = ecdis },
            "S-104" => new S104RenderContext(timeStep) { Palette = palette, SymbolScale = symScale, TextScale = txtScale, EcdisDisplay = ecdis },
            "S-111" => new S111RenderContext(timeStep) { Palette = palette, SymbolScale = symScale, TextScale = txtScale, EcdisDisplay = ecdis },
            "S-124" => new S124RenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale, EcdisDisplay = ecdis },
            "S-129" => new S129RenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale, EcdisDisplay = ecdis },
            // S-421 reuses the base RenderContext (no spec-specific record exists yet).
            _ => new SimpleRenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale, EcdisDisplay = ecdis },
        };
    }

    /// <summary>
    /// Rasterises the layer stack and viewport extent of <paramref name="result"/>
    /// to an <see cref="SKBitmap"/> at the size requested by <paramref name="options"/>.
    /// </summary>
    private static SKBitmap Rasterize(MapsuiDatasetResult result, HarnessOptions options)
    {
        var map = new Map { CRS = "EPSG:3857" };
        map.BackColor = MapsuiColorFromUInt(options.BackgroundColor);

        foreach (var layer in result.Layers)
        {
            map.Layers.Add(layer);
        }

        map.Navigator.SetSize(options.Width, options.Height);

        if (options.Viewport is { } vp)
        {
            var (minX, minY) = Mapsui.Projections.SphericalMercator.FromLonLat(vp.West, vp.South);
            var (maxX, maxY) = Mapsui.Projections.SphericalMercator.FromLonLat(vp.East, vp.North);
            map.Navigator.ZoomToBox(new MRect(minX, minY, maxX, maxY), MBoxFit.Fit);
        }
        else
        {
            var extent = result.Extent;
            if (extent.Width > 0 && extent.Height > 0)
            {
                map.Navigator.ZoomToBox(extent, MBoxFit.Fit);
            }
        }

        return options.RenderSubsystem == RenderSubsystemKind.TiledScene
            ? RasterizeSettled(map, options)
            : RenderFrame(map);
    }

    /// <summary>
    /// Renders the map once to an <see cref="SKBitmap"/> via Mapsui's headless
    /// <see cref="MapRenderer.RenderToBitmapStream(Map, float, RenderFormat, int)"/>.
    /// </summary>
    private static SKBitmap RenderFrame(Map map)
    {
        using var stream = new MapRenderer().RenderToBitmapStream(
            map, pixelDensity: 1f, renderFormat: RenderFormat.Png, quality: 100);
        stream.Position = 0;
        return SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException(
                "Mapsui.Rendering.Skia produced a stream that SkiaSharp could not decode.");
    }

    /// <summary>
    /// Drives the TiledScene ("B") subsystem to a settled frame. Unlike the "A"
    /// arm — where a single <see cref="MapRenderer.RenderToBitmapStream(Map, float, RenderFormat, int)"/>
    /// produces the final pixels — the "B" base plane rasterises on a worker
    /// thread: the first paint blits nothing and schedules a worker, which later
    /// requests a repaint through the layer's redraw sink when a tile publishes.
    /// This loop is the headless analogue of the viewer's
    /// <c>await_render_idle</c>: it re-renders on every published tile and stops
    /// once no new tile arrives within <see cref="HarnessOptions.SettleQuietPeriod"/>
    /// (or <see cref="HarnessOptions.SettleTimeout"/> elapses). Prediction
    /// (pre-warm) tiles deliberately do not request a redraw, so the loop is
    /// not kept alive by speculative rasterisation.
    /// </summary>
    private static SKBitmap RasterizeSettled(Map map, HarnessOptions options)
    {
        using var redraw = new ManualResetEventSlim(initialState: false);
        void OnRedraw() => redraw.Set();

        // The background tile / scene renderers repaint through each vector
        // layer's per-session redraw sink; stamp OnRedraw onto the instrumented
        // layers so a published tile wakes this settle loop (the headless
        // analogue of a live host's UI-thread redraw). Capture and restore any
        // prior sink rather than clearing to null, so we never clobber a caller
        // that had wired its own.
        var instrumented = map.Layers.OfType<InstrumentedMemoryLayer>().ToArray();
        var priorSinks = Array.ConvertAll(instrumented, layer => layer.RequestRedraw);
        foreach (var layer in instrumented)
        {
            layer.RequestRedraw = OnRedraw;
        }

        try
        {
            var deadline = Stopwatch.GetTimestamp()
                + (long)(options.SettleTimeout.TotalSeconds * Stopwatch.Frequency);

            // First frame schedules the worker raster for the visible tiles.
            var bitmap = RenderFrame(map);

            while (Stopwatch.GetTimestamp() < deadline)
            {
                if (!redraw.Wait(options.SettleQuietPeriod))
                {
                    // No new tile published within the quiet period: the base
                    // plane has settled.
                    break;
                }

                redraw.Reset();
                bitmap.Dispose();
                bitmap = RenderFrame(map);
            }

            return bitmap;
        }
        finally
        {
            for (var i = 0; i < instrumented.Length; i++)
            {
                instrumented[i].RequestRedraw = priorSinks[i];
            }
        }
    }

    private static Mapsui.Styles.Color MapsuiColorFromUInt(uint argb)
    {
        byte a = (byte)((argb >> 24) & 0xFF);
        byte r = (byte)((argb >> 16) & 0xFF);
        byte g = (byte)((argb >> 8) & 0xFF);
        byte b = (byte)(argb & 0xFF);
        return new Mapsui.Styles.Color(r, g, b, a);
    }

    private static PortrayalCatalogueManager CreateDefaultCatalogueManager()
    {
        var manager = new PortrayalCatalogueManager();
        foreach (var spec in Specification.AvailableSpecs)
        {
            if (Specification.HasPortrayalCatalogue(spec))
            {
                manager.SetSource(spec, Specification.CreatePortrayalCatalogueSource(spec));
            }
        }
        return manager;
    }

    public void Dispose()
    {
        if (_ownsCatalogueManager)
        {
            _catalogueManager.Dispose();
        }
    }

    /// <summary>
    /// Concrete fallback used for specs without their own RenderContext record
    /// (currently S-421).
    /// </summary>
    private sealed record SimpleRenderContext : RenderContext;
}
