using System.Reflection;
using EncDotNet.S100.Core;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Mapsui;
using EncDotNet.S100.Scripting;
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
///   <item>It invokes <see cref="IDatasetProcessor.Render"/> with a spec-specific
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
            new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthorityProvider(
                new EncDotNet.S100.Datasets.Pipelines.Interoperability.InteroperabilityAuthority()));
    }

    /// <summary>
    /// Loads the dataset at <paramref name="path"/>, runs it through its pipeline,
    /// and returns the rendered bitmap. Caller owns the returned bitmap.
    /// </summary>
    public SKBitmap Render(string path, HarnessOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= HarnessOptions.Default;

        var processor = _factory.CreateProcessor(path);
        var context = BuildContext(processor, options);
        var result = processor.Render(context);

        return Rasterize(result, options);
    }

    /// <summary>
    /// Same as <see cref="Render"/> but also returns the <see cref="DatasetResult"/>
    /// produced by the pipeline (useful when a test wants to assert against
    /// <see cref="DatasetResult.Info"/> or similar).
    /// </summary>
    public (SKBitmap Bitmap, DatasetResult Result) RenderWithResult(
        string path, HarnessOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        options ??= HarnessOptions.Default;

        var processor = _factory.CreateProcessor(path);
        var context = BuildContext(processor, options);
        var result = processor.Render(context);

        return (Rasterize(result, options), result);
    }

    private static RenderContext BuildContext(IDatasetProcessor processor, HarnessOptions options)
    {
        var palette = options.Palette;
        var symScale = options.SymbolScale;
        var txtScale = options.TextScale;

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
            "S-101" => new S101RenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale },
            "S-102" => new S102RenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale },
            "S-104" => new S104RenderContext(timeStep) { Palette = palette, SymbolScale = symScale, TextScale = txtScale },
            "S-111" => new S111RenderContext(timeStep) { Palette = palette, SymbolScale = symScale, TextScale = txtScale },
            "S-124" => new S124RenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale },
            "S-129" => new S129RenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale },
            // S-421 reuses the base RenderContext (no spec-specific record exists yet).
            _ => new SimpleRenderContext { Palette = palette, SymbolScale = symScale, TextScale = txtScale },
        };
    }

    /// <summary>
    /// Rasterises the layer stack and viewport extent of <paramref name="result"/>
    /// to an <see cref="SKBitmap"/> at the size requested by <paramref name="options"/>.
    /// </summary>
    private static SKBitmap Rasterize(DatasetResult result, HarnessOptions options)
    {
        var map = new Map { CRS = "EPSG:3857" };
        map.BackColor = MapsuiColorFromUInt(options.BackgroundColor);

        foreach (var layer in result.Layers)
        {
            map.Layers.Add(layer);
        }

        map.Navigator.SetSize(options.Width, options.Height);

        var extent = result.Extent;
        if (extent.Width > 0 && extent.Height > 0)
        {
            map.Navigator.ZoomToBox(extent, MBoxFit.Fit);
        }

        using var stream = new MapRenderer().RenderToBitmapStream(
            map, pixelDensity: 1f, renderFormat: RenderFormat.Png, quality: 100);
        stream.Position = 0;
        var bitmap = SKBitmap.Decode(stream)
            ?? throw new InvalidOperationException(
                "Mapsui.Rendering.Skia produced a stream that SkiaSharp could not decode.");
        return bitmap;
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
