using EncDotNet.S100.Core.Scheduling;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using NetTopologySuite.Geometries;
using SkiaSharp;
using SkiaFinerCoverage = EncDotNet.S100.Renderers.Skia.Scene.FinerCoverage;

namespace EncDotNet.S100.Rendering.Engine;

/// <summary>
/// One dataset's contribution to a chart composite: exactly one of a vector or
/// coverage portrayal result, plus its S-98 active flag.
/// </summary>
public sealed class ChartCompositeInput
{
    /// <summary>The vector portrayal result, when this dataset is a vector product.</summary>
    public VectorPortrayalResult? Vector { get; init; }

    /// <summary>The coverage portrayal result, when this dataset is a coverage product.</summary>
    public CoveragePortrayalResult? Coverage { get; init; }

    /// <summary>
    /// Whether the dataset is active for S-98 inter-product rule evaluation and
    /// painting. Inactive datasets are still passed to the rule engine as
    /// context (so, e.g., an inactive S-102 does not suppress S-101 depths) but
    /// are not painted. Defaults to <see langword="true"/>.
    /// </summary>
    public bool Active { get; init; } = true;

    /// <summary>Creates a vector composite input.</summary>
    /// <param name="result">The vector portrayal result.</param>
    /// <param name="active">Whether the dataset is painted.</param>
    public static ChartCompositeInput ForVector(VectorPortrayalResult result, bool active = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ChartCompositeInput { Vector = result, Active = active };
    }

    /// <summary>Creates a coverage composite input.</summary>
    /// <param name="result">The coverage portrayal result.</param>
    /// <param name="active">Whether the dataset is painted.</param>
    public static ChartCompositeInput ForCoverage(CoveragePortrayalResult result, bool active = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new ChartCompositeInput { Coverage = result, Active = active };
    }
}

/// <summary>Options for a chart composite.</summary>
public sealed class ChartCompositeOptions
{
    /// <summary>Output width in device-independent pixels. Ignored when <see cref="Viewport"/> is supplied.</summary>
    public int Width { get; init; } = 1024;

    /// <summary>Output height in device-independent pixels. Ignored when <see cref="Viewport"/> is supplied.</summary>
    public int Height { get; init; } = 1024;

    /// <summary>
    /// Image pixels per viewport pixel: 1 for an image the size of the
    /// viewport, 2 for a HiDPI image of the same view, with symbols, text and
    /// lines twice as many pixels across. Defaults to 1.
    /// </summary>
    public float DeviceScale { get; init; } = 1f;

    /// <summary>Background fill painted once before the ordered layers. Defaults to opaque white.</summary>
    public RgbaColor Background { get; init; } = new(255, 255, 255, 255);

    /// <summary>
    /// Explicit shared viewport, in device-independent pixels. When
    /// <see langword="null"/> the compositor fits a <see cref="Width"/> ×
    /// <see cref="Height"/> viewport to the union extent of the active layers.
    /// When supplied, its pixel dimensions win over <see cref="Width"/> and
    /// <see cref="Height"/>.
    /// </summary>
    public Viewport? Viewport { get; init; }

    /// <summary>
    /// Mariner settings fed to the S-98 rule engine (e.g. for the R-101-102-B
    /// safety-contour exception). Defaults to <see cref="MarinerSettings.Default"/>.
    /// </summary>
    public MarinerSettings? Mariner { get; init; }

    /// <summary>
    /// Drawing-instruction categories (areas, lines, points, text) to suppress
    /// across every vector layer. Defaults to <see cref="DrawingInstructionCategory.None"/>.
    /// </summary>
    public DrawingInstructionCategory HiddenCategories { get; init; } = DrawingInstructionCategory.None;

    /// <summary>
    /// Basemap drawn beneath all chart layers. When <see cref="BasemapKind.Offline"/>,
    /// the bundled Natural Earth land layer is drawn bottom-most. Defaults to
    /// <see cref="BasemapKind.None"/>.
    /// </summary>
    public BasemapKind Basemap { get; init; } = BasemapKind.None;

    /// <summary>
    /// Whether vector layers apply S-100 Part 9 §11.1 scale-visibility culling
    /// (each op's SCAMIN, and each cell's minimum display scale for the whole
    /// cell) at the viewport's <see cref="Viewport.ScaleDenominator"/>. Defaults to
    /// <see langword="false"/>, which draws every op whatever the scale.
    /// </summary>
    public bool HonorScaleVisibility { get; init; }

    /// <summary>
    /// Whether vector layers wrap geometry into a viewport that crosses the ±180°
    /// antimeridian. Defaults to <see langword="true"/>, which suits a single
    /// fitted viewport. Tiled rendering turns it off and draws the world copies
    /// itself (see <see cref="ChartComposition.Draw"/>).
    /// </summary>
    public bool EnableSeamWrap { get; init; } = true;

    /// <summary>
    /// Whether point symbols and text placed in the visible parts of a surface
    /// (S-100 Part 9 <c>AreaPlacement:VisibleParts</c>) move into the part of
    /// the surface in the viewport. Defaults to <see langword="true"/>. Tiled
    /// rendering turns it off, so a surface across several tiles keeps one
    /// symbol at its representative point instead of one per tile.
    /// </summary>
    public bool KeepAreaSymbolsInView { get; init; } = true;
}

/// <summary>
/// The chart engine's host-neutral compositor. Given renderer-neutral portrayal
/// results (vector and coverage), it drives the S-98 ordering and suppression
/// engine (<see cref="LayerStackBuilder"/> and an
/// <see cref="IInteroperabilityAuthority"/>), lowers each ordered sub-layer to a
/// Skia layer, and applies cross-cell scale-band overlap suppression with the
/// viewer's NTS coverage model, so a finer cell hides a coarser one exactly
/// where its normalised coverage footprint does.
/// </summary>
/// <remarks>
/// <para>
/// Coverage layers are reprojected from their grid's native CRS to WGS-84 with
/// the supplied <see cref="ICrsTransformFactory"/>, so they register with
/// vector layers in the shared viewport.
/// </para>
/// <para>
/// A compositor holds no state between calls; <see cref="Prepare"/> and
/// <see cref="Render"/> may run concurrently.
/// </para>
/// </remarks>
public sealed class ChartCompositor
{
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly IInteroperabilityAuthority _authority;

    /// <summary>Creates a compositor.</summary>
    /// <param name="crsTransformFactory">Reprojects coverage grids' native CRS extents to WGS-84.</param>
    /// <param name="authority">
    /// The S-98 interoperability authority (ordering and rule policy), usually an
    /// <see cref="InteroperabilityAuthority"/> over the product registry's display planes.
    /// </param>
    public ChartCompositor(ICrsTransformFactory crsTransformFactory, IInteroperabilityAuthority authority)
    {
        ArgumentNullException.ThrowIfNull(crsTransformFactory);
        ArgumentNullException.ThrowIfNull(authority);
        _crsTransformFactory = crsTransformFactory;
        _authority = authority;
    }

    /// <summary>
    /// Composites the datasets into one bitmap: the explicit viewport, or a
    /// viewport fitted to their union extent, at <see cref="ChartCompositeOptions.DeviceScale"/>.
    /// </summary>
    /// <param name="datasets">The datasets, in draw order (bottom-most first).</param>
    /// <param name="options">Render options; defaults when <see langword="null"/>.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public SKBitmap Render(IReadOnlyList<ChartCompositeInput> datasets, ChartCompositeOptions? options = null)
    {
        options ??= new ChartCompositeOptions();
        var composition = Prepare(datasets, options);
        var viewport = options.Viewport ?? composition.FitViewport(options.Width, options.Height);
        return composition.Render(viewport, options.DeviceScale);
    }

    /// <summary>
    /// Orders, rules and lowers the datasets once, returning a composition that
    /// can be painted against many viewports. Each sub-layer is lowered in turn
    /// on the calling thread; <see cref="PrepareAsync"/> lowers them in parallel.
    /// </summary>
    /// <param name="datasets">The datasets, in draw order (bottom-most first).</param>
    /// <param name="options">
    /// Composite options. <see cref="ChartCompositeOptions.Width"/>,
    /// <see cref="ChartCompositeOptions.Height"/>, <see cref="ChartCompositeOptions.DeviceScale"/>
    /// and <see cref="ChartCompositeOptions.Viewport"/> are not used.
    /// </param>
    /// <returns>The prepared composition.</returns>
    public ChartComposition Prepare(IReadOnlyList<ChartCompositeInput> datasets, ChartCompositeOptions? options = null)
    {
        options ??= new ChartCompositeOptions();
        var plan = Plan(datasets, options);
        var lowered = new LoweredLayer?[plan.Count];
        for (int i = 0; i < plan.Count; i++)
            lowered[i] = plan[i]();
        return Assemble(lowered, options);
    }

    /// <summary>
    /// Orders, rules and lowers the datasets once, as <see cref="Prepare"/> does,
    /// lowering each sub-layer to its scene as its own <see cref="WorkStage.Scene"/>
    /// job in the <see cref="WorkClass.Visible"/> class, so they lower in parallel.
    /// Don't call it from a job on the same scheduler and block on the result.
    /// </summary>
    /// <param name="datasets">The datasets, in draw order (bottom-most first).</param>
    /// <param name="options">Composite options, as for <see cref="Prepare"/>.</param>
    /// <param name="scheduler">The scheduler to run on; <see langword="null"/> for <see cref="WorkScheduler.Shared"/>.</param>
    /// <param name="cancellationToken">Cancels the lowering still queued.</param>
    /// <returns>The prepared composition, the same as <see cref="Prepare"/> makes.</returns>
    public async Task<ChartComposition> PrepareAsync(
        IReadOnlyList<ChartCompositeInput> datasets,
        ChartCompositeOptions? options = null,
        IWorkScheduler? scheduler = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new ChartCompositeOptions();
        scheduler ??= WorkScheduler.Shared;
        var plan = Plan(datasets, options);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var jobs = new Task<LoweredLayer?>[plan.Count];
        for (int i = 0; i < plan.Count; i++)
        {
            var lower = plan[i];
            jobs[i] = scheduler.RunAsync(WorkStage.Scene, _ => lower(), cancel.Token, WorkPriority.Visible(i));
        }

        try
        {
            return Assemble(await Task.WhenAll(jobs).ConfigureAwait(false), options);
        }
        catch
        {
            await cancel.CancelAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// Orders and rules the datasets and returns one lowering step per active
    /// sub-layer, bottom first; a step returns <see langword="null"/> for a
    /// sub-layer with nothing to draw.
    /// </summary>
    private List<Func<LoweredLayer?>> Plan(IReadOnlyList<ChartCompositeInput> datasets, ChartCompositeOptions options)
    {
        ArgumentNullException.ThrowIfNull(datasets);

        // 1. Each dataset's renderer-neutral stack items, in paint order top
        //    first (the order LayerStackBuilder expects); the input is bottom first.
        var perDataset = new List<IReadOnlyList<SubLayerStackItem>>(datasets.Count);
        var loaded = new List<LoadedDatasetInfo>(datasets.Count);
        for (int i = datasets.Count - 1; i >= 0; i--)
        {
            var input = datasets[i];
            var (items, spec, datasetId) = BuildItems(input);
            if (items.Count == 0)
                continue;
            perDataset.Add(items);
            loaded.Add(new LoadedDatasetInfo(datasetId, spec, input.Active));
        }

        // 2. S-98 paint order, then inter-product rules. Inactive datasets are
        //    context, so an inactive suppressor does not fire.
        var sorted = LayerStackBuilder.Build(_authority, perDataset);
        var ruled = _authority.ApplyRules(sorted, loaded, options.Mariner);

        // 3. One lowering step per active item, bottom first.
        var activeIds = new HashSet<string>(
            loaded.Where(l => l.Active).Select(l => l.DatasetId),
            StringComparer.Ordinal);

        bool ignoreScaleMinimum = options.Mariner?.IgnoreScaleMinimum ?? false;
        var finerByDataset = ignoreScaleMinimum
            ? new Dictionary<string, CellOverlap[]>(StringComparer.Ordinal)
            : CollectFinerCoverages(datasets, activeIds);
        var coverageSettings = new CoverageLayerLowering.Settings(
            options.HiddenCategories, options.HonorScaleVisibility, options.EnableSeamWrap, options.KeepAreaSymbolsInView);

        var steps = new List<Func<LoweredLayer?>>(ruled.Count);
        foreach (var item in ruled)
        {
            if (!activeIds.Contains(item.SourceDatasetId))
                continue;

            switch (item.Payload)
            {
                case VectorStackPayload vector:
                    {
                        var finer = finerByDataset.GetValueOrDefault(item.SourceDatasetId) ?? [];
                        steps.Add(() =>
                        {
                            var scene = LowerVector(vector, options.HiddenCategories);
                            var layer = new VectorCompositeLayer(scene, options.HonorScaleVisibility)
                            {
                                EnableSeamWrap = options.EnableSeamWrap,
                                KeepAreaSymbolsInView = options.KeepAreaSymbolsInView,
                                // The whole cell stops at its minimum display scale
                                // unless the mariner asks to ignore it (S-101 FC §3.1.1).
                                MinimumDisplayScale = ignoreScaleMinimum ? null : vector.Result.CellMinimumDisplayScale,
                                FinerCoverages = [.. finer.Select(f => f.Clip)],
                            };
                            return new LoweredLayer(new ChartComposition.Entry(layer, finer), bounds => bounds.AddScene(scene));
                        });
                        break;
                    }

                case CoverageStackPayload coverage:
                    {
                        steps.Add(() => CoverageLayerLowering.TryLower(
                                coverage.SubLayer, _crsTransformFactory, coverageSettings,
                                out var layer, out var west, out var east, out var south, out var north)
                            ? new LoweredLayer(new ChartComposition.Entry(layer, []), bounds => bounds.AddLonLatBox(west, east, south, north))
                            : null);
                        break;
                    }
            }
        }

        return steps;
    }

    /// <summary>Gathers the lowered layers, bottom first, and their union extent.</summary>
    private static ChartComposition Assemble(IReadOnlyList<LoweredLayer?> lowered, ChartCompositeOptions options)
    {
        var entries = new List<ChartComposition.Entry>(lowered.Count);
        var bounds = new SeamAwareBoundsAccumulator();
        foreach (var layer in lowered)
        {
            if (layer is null)
                continue;
            entries.Add(layer.Entry);
            layer.AddBounds(bounds);
        }

        return new ChartComposition(entries, bounds, options);
    }

    /// <summary>
    /// For each active chart cell, the coverages of the strictly finer active
    /// cells that overlap it, chosen by the engine's own rule
    /// (<see cref="OverlapSuppression.CollectFinerCoverages"/>): envelopes and
    /// then normalised NTS footprints must intersect. Cells are ranked by
    /// compilation scale, falling back to the minimum display scale; a finer
    /// coverage hides the coarser cell only while the finer cell draws, up to
    /// its minimum display scale. Cells without a ranking scale or a usable
    /// coverage take no part.
    /// </summary>
    private static Dictionary<string, CellOverlap[]> CollectFinerCoverages(
        IReadOnlyList<ChartCompositeInput> datasets, IReadOnlySet<string> activeIds)
    {
        var cells = new List<(string Id, OverlapSuppressionCell Cell, double Cutoff)>();
        foreach (var input in datasets)
        {
            if (input.Vector is not { } vector
                || !activeIds.Contains(vector.SourceDatasetId)
                || CellScaleRanking.Rank(vector.CellCompilationScale, vector.CellMinimumDisplayScale, vector.CellMaximumDisplayScale) is not CellRank rank
                || CoverageFootprint.FromAreas(vector.CoverageAreas) is not { } footprint)
            {
                continue;
            }

            cells.Add((
                vector.SourceDatasetId,
                new OverlapSuppressionCell { Layers = [], Coverage = footprint, Rank = rank },
                vector.CellMinimumDisplayScale ?? rank.Scale));
        }

        var byCoverage = new Dictionary<Geometry, (OverlapSuppressionCell Cell, double Cutoff)>(ReferenceEqualityComparer.Instance);
        foreach (var (_, cell, cutoff) in cells)
            byCoverage[cell.Coverage!] = (cell, cutoff);

        var clips = new Dictionary<OverlapSuppressionCell, CellOverlap>();
        var cellList = cells.Select(c => c.Cell).ToList();
        var result = new Dictionary<string, CellOverlap[]>(StringComparer.Ordinal);
        foreach (var (id, cell, _) in cells)
        {
            if (OverlapSuppression.CollectFinerCoverages(cell, cellList) is not { Count: > 0 } finer)
                continue;

            var overlaps = new CellOverlap[finer.Count];
            for (int i = 0; i < finer.Count; i++)
            {
                var (finerCell, cutoff) = byCoverage[finer[i].Coverage];
                if (!clips.TryGetValue(finerCell, out var overlap))
                {
                    overlap = new CellOverlap(
                        new SkiaFinerCoverage(CoverageFootprint.Rings(finerCell.Coverage!), cutoff),
                        new HiddenCoverageCache.Item(finerCell.Coverage!));
                    clips[finerCell] = overlap;
                }

                overlaps[i] = overlap;
            }

            result[id] = overlaps;
        }

        return result;
    }

    private static (IReadOnlyList<SubLayerStackItem> Items, string Spec, string DatasetId) BuildItems(ChartCompositeInput input)
    {
        if (input.Vector is { } vector)
        {
            var items = new List<SubLayerStackItem>(vector.SubLayers.Count);
            foreach (var sub in vector.SubLayers)
            {
                items.Add(new SubLayerStackItem(
                    new VectorStackPayload(vector, sub),
                    sub.Plane,
                    sub.WithinPlanePriority,
                    vector.SourceDatasetId,
                    sub.SourceFeatureType)
                {
                    SourceScaleDenominator = CellScaleRanking.Rank(vector.CellCompilationScale, vector.CellMinimumDisplayScale)?.Scale,
                });
            }

            return (items, vector.Spec.Name, vector.SourceDatasetId);
        }

        if (input.Coverage is { } coverage)
        {
            var items = new List<SubLayerStackItem>(coverage.SubLayers.Count);
            foreach (var sub in coverage.SubLayers)
            {
                items.Add(new SubLayerStackItem(
                    new CoverageStackPayload(coverage, sub),
                    sub.Plane,
                    sub.WithinPlanePriority,
                    coverage.SourceDatasetId,
                    sub.SourceFeatureType));
            }

            return (items, coverage.Spec.Name, coverage.SourceDatasetId);
        }

        throw new ArgumentException(
            "ChartCompositeInput must carry either a Vector or a Coverage portrayal result.",
            nameof(input));
    }

    private static VectorScene LowerVector(VectorStackPayload payload, DrawingInstructionCategory hiddenCategories)
    {
        var result = payload.Result;
        return HeadlessVectorRenderer.BuildScene(
            payload.SubLayer.Instructions,
            result.GeometryProvider,
            result.Palette,
            result.SymbolProvider,
            result.LineStyleProvider,
            result.SymbolScale,
            result.TextScale,
            result.AreaFillProvider,
            hiddenCategories);
    }
}

/// <summary>
/// One finer cell's coverage as a coarser cell sees it: the clip that erases
/// it, and the prepared footprint that answers whether a rectangle lies wholly
/// inside it.
/// </summary>
/// <param name="Clip">The difference clip drawn by the coarser layer.</param>
/// <param name="Footprint">The finer cell's normalised footprint, for skip tests.</param>
internal sealed record CellOverlap(SkiaFinerCoverage Clip, HiddenCoverageCache.Item Footprint);

/// <summary>A lowered sub-layer and how it adds to the composition's extent.</summary>
/// <param name="Entry">The lowered layer.</param>
/// <param name="AddBounds">Adds the layer's extent to the union.</param>
internal sealed record LoweredLayer(ChartComposition.Entry Entry, Action<SeamAwareBoundsAccumulator> AddBounds);
