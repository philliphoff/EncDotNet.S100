using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Coverage;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Renderers.Skia;
using EncDotNet.S100.Renderers.Skia.Scene;
using EncDotNet.S100.Rendering.Scene;
using SkiaSharp;

namespace EncDotNet.S100.Datasets.Pipelines;

/// <summary>
/// One dataset's contribution to a headless composite: exactly one of a
/// Mapsui-free vector or coverage portrayal result, plus its S-98 active flag.
/// </summary>
public sealed class HeadlessCompositeInput
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
    public static HeadlessCompositeInput ForVector(VectorPortrayalResult result, bool active = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new HeadlessCompositeInput { Vector = result, Active = active };
    }

    /// <summary>Creates a coverage composite input.</summary>
    public static HeadlessCompositeInput ForCoverage(CoveragePortrayalResult result, bool active = true)
    {
        ArgumentNullException.ThrowIfNull(result);
        return new HeadlessCompositeInput { Coverage = result, Active = active };
    }
}

/// <summary>
/// Options controlling a headless composite render.
/// </summary>
public sealed class HeadlessCompositeOptions
{
    /// <summary>Output width in pixels. Ignored when <see cref="Viewport"/> is supplied.</summary>
    public int Width { get; init; } = 1024;

    /// <summary>Output height in pixels. Ignored when <see cref="Viewport"/> is supplied.</summary>
    public int Height { get; init; } = 1024;

    /// <summary>Background fill painted once before the ordered layers. Defaults to opaque white.</summary>
    public RgbaColor Background { get; init; } = new(255, 255, 255, 255);

    /// <summary>
    /// Explicit shared viewport. When <see langword="null"/> the compositor
    /// computes the union extent of all active layers and fits a
    /// <see cref="Width"/> × <see cref="Height"/> viewport to it. When supplied,
    /// its pixel dimensions win over <see cref="Width"/> / <see cref="Height"/>.
    /// </summary>
    public Viewport? Viewport { get; init; }

    /// <summary>
    /// Mariner settings snapshot fed to the S-98 rule engine (e.g. for the
    /// R-101-102-B safety-contour exception). Defaults to
    /// <see cref="MarinerSettings.Default"/>.
    /// </summary>
    public MarinerSettings? Mariner { get; init; }

    /// <summary>
    /// Drawing-instruction categories (areas, lines, points, text) to suppress
    /// globally across every vector layer in the composite. Defaults to
    /// <see cref="DrawingInstructionCategory.None"/> (draw everything).
    /// </summary>
    public DrawingInstructionCategory HiddenCategories { get; init; }
        = DrawingInstructionCategory.None;

    /// <summary>
    /// Basemap drawn beneath all chart layers (issue #411). When
    /// <see cref="BasemapKind.Offline"/>, the bundled Natural Earth land layer is
    /// composited bottom-most against the shared viewport. Defaults to
    /// <see cref="BasemapKind.None"/> (no basemap; output unchanged).
    /// </summary>
    public BasemapKind Basemap { get; init; } = BasemapKind.None;
}

/// <summary>
/// Mapsui-free multi-layer S-100 compositor. Given a set of pre-built,
/// renderer-neutral portrayal results (vector and/or coverage), the compositor
/// drives the renderer-neutral S-98 ordering / suppression engine
/// (<see cref="LayerStackBuilder"/> + <see cref="IInteroperabilityAuthority"/>),
/// lowers each ordered sub-layer into a Skia <see cref="CompositeLayer"/>, and
/// paints them against one shared <see cref="Viewport"/> via
/// <see cref="HeadlessCompositeRenderer"/> — reproducing, without Mapsui, the
/// cross-dataset draw order and depth suppression the viewer applies (e.g. the
/// canonical S-101-under-S-102 interleave, S-98 Annex A §A-6.9.1).
/// </summary>
/// <remarks>
/// Coverage layers are reprojected from their grid's native CRS to WGS84 via the
/// supplied <see cref="ICrsTransformFactory"/> (ProjNet in the facade) so they
/// register with vector layers in the shared viewport's pixel space. Pre-projected
/// point-glyph coverage sub-layers (S-104 / S-111 fixed-station variants) are not
/// yet supported in the composite path and are skipped.
/// </remarks>
public sealed class HeadlessCompositor
{
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly IInteroperabilityAuthority _authority;

    /// <summary>
    /// Creates a compositor.
    /// </summary>
    /// <param name="crsTransformFactory">
    /// Factory used to reproject coverage grids' native CRS extents to WGS84.
    /// </param>
    /// <param name="authority">
    /// The S-98 interoperability authority (ordering + rule policy). Defaults to
    /// the fixed-table <see cref="InteroperabilityAuthority"/>.
    /// </param>
    public HeadlessCompositor(
        ICrsTransformFactory crsTransformFactory,
        IInteroperabilityAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(crsTransformFactory);
        _crsTransformFactory = crsTransformFactory;
        _authority = authority ?? new InteroperabilityAuthority();
    }

    /// <summary>
    /// Composites the supplied datasets into a single bitmap.
    /// </summary>
    /// <param name="datasets">
    /// The datasets to composite, in draw order (bottom-most first). Each carries
    /// a vector or coverage portrayal result.
    /// </param>
    /// <param name="options">Render options (size / explicit viewport / background / mariner).</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public SKBitmap Render(
        IReadOnlyList<HeadlessCompositeInput> datasets,
        HeadlessCompositeOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        options ??= new HeadlessCompositeOptions();

        // 1. Build each dataset's renderer-neutral stack items and its
        //    LoadedDatasetInfo, in paint-order top-first (the outer order the
        //    LayerStackBuilder expects — it reverses to seed the tiebreaker).
        //    The input list is bottom-first (draw order), so reverse it.
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

        // 2. Sort into S-98 paint order, then apply inter-product rules
        //    (suppression). ApplyRules sees inactive datasets as context so an
        //    inactive suppressor does not fire.
        var sorted = LayerStackBuilder.Build(_authority, perDataset);
        var ruled = _authority.ApplyRules(sorted, loaded, options.Mariner);

        // 3. Lower each active item to a CompositeLayer, in bottom-first paint
        //    order, accumulating the union extent (EPSG:3857) as we go.
        var activeIds = new HashSet<string>(
            loaded.Where(l => l.Active).Select(l => l.DatasetId),
            StringComparer.Ordinal);

        var lowered = new List<CompositeLayer>(ruled.Count);
        var bounds = new SeamAwareBoundsAccumulator();

        foreach (var item in ruled)
        {
            if (!activeIds.Contains(item.SourceDatasetId))
                continue;

            switch (item.Payload)
            {
                case VectorStackPayload vector:
                    {
                        var scene = LowerVector(vector, options.HiddenCategories);
                        lowered.Add(new VectorCompositeLayer(scene, honorScaleVisibility: false));
                        bounds.AddScene(scene);
                        break;
                    }

                case CoverageStackPayload coverage:
                    {
                        if (TryLowerCoverage(coverage, out var layer, out var west, out var east, out var south, out var north))
                        {
                            lowered.Add(layer);
                            bounds.AddLonLatBox(west, east, south, north);
                        }
                        break;
                    }
            }
        }

        // 4. Resolve the shared viewport: explicit wins; otherwise seam-aware
        //    auto-fit of the union extent.
        var viewport = options.Viewport
            ?? BuildUnionViewport(bounds, options.Width, options.Height);

        // 4a. Prepend the land basemap (issue #411) as the bottom-most layer so
        //     it draws under every chart layer, registered with the shared
        //     viewport. The land scene is viewport-independent world geometry.
        if (options.Basemap == BasemapKind.Offline)
        {
            lowered.Insert(0, new VectorCompositeLayer(
                NaturalEarthBasemap.LandScene, honorScaleVisibility: false));
        }

        // 5. Paint.
        var renderer = new HeadlessCompositeRenderer { Background = options.Background };
        return renderer.Render(viewport, lowered);
    }

    private static (IReadOnlyList<SubLayerStackItem> Items, string Spec, string DatasetId) BuildItems(
        HeadlessCompositeInput input)
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
                    SourceScaleDenominator = vector.CellMinimumDisplayScale,
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
            "HeadlessCompositeInput must carry either a Vector or a Coverage portrayal result.",
            nameof(input));
    }

    private static VectorScene LowerVector(VectorStackPayload payload, DrawingInstructionCategory hiddenCategories)
    {
        var result = payload.Result;
        var sub = payload.SubLayer;
        return HeadlessVectorRenderer.BuildScene(
            sub.Instructions,
            result.GeometryProvider,
            result.Palette,
            result.SymbolProvider,
            result.LineStyleProvider,
            result.SymbolScale,
            result.TextScale,
            result.AreaFillProvider,
            hiddenCategories);
    }

    private bool TryLowerCoverage(
        CoverageStackPayload payload,
        out CompositeLayer layer,
        out double west, out double east, out double south, out double north)
    {
        layer = null!;
        west = east = south = north = 0;

        switch (payload.SubLayer)
        {
            case GridCoverageSubLayer grid:
                {
                    var (w, e, s, n, nativeToWgs84) = ReprojectExtent(grid.Coverage, grid.Viewport);
                    var landCellMask = ComputeLandCellMask(grid);
                    layer = new CoverageCompositeLayer(
                        grid.Coverage, w, e, s, n, nativeToWgs84: nativeToWgs84, landCellMask: landCellMask);
                    west = w; east = e; south = s; north = n;
                    return true;
                }

            case ArrowCoverageSubLayer arrow:
                {
                    var (w, e, s, n, nativeToWgs84) = ReprojectExtent(arrow.Coverage, arrow.Viewport);
                    var arrowRenderer = new SkiaCoverageArrowRenderer
                    {
                        SymbolProvider = arrow.SymbolProvider,
                        BaseSymbolScale = arrow.BaseSymbolScale,
                    };
                    layer = new CoverageCompositeLayer(arrow.Coverage, w, e, s, n, arrowRenderer, nativeToWgs84);
                    west = w; east = e; south = s; north = n;
                    return true;
                }

            // Pre-projected point-glyph sub-layers (fixed-station variants) are
            // not yet supported in the headless composite path.
            default:
                return false;
        }
    }

    private bool[]? ComputeLandCellMask(GridCoverageSubLayer grid)
    {
        var land = grid.LandAreaMask;
        if (land is null || land.Count == 0)
        {
            return null;
        }

        var georeferencer = grid.Coverage.Georeferencer;
        var metadata = grid.Coverage.Coverage.Metadata;
        var transform = _crsTransformFactory.Create("EPSG:4326", georeferencer.CRS);
        return CoverageLandMask.Compute(
            georeferencer,
            metadata.NumRows,
            metadata.NumColumns,
            land,
            transform);
    }

    private (double West, double East, double South, double North, ICrsTransform NativeToWgs84) ReprojectExtent(
        StyledCoverageLayer coverage, Viewport nativeViewport)
    {
        // The sub-layer's Viewport carries the grid extent in the grid's NATIVE
        // CRS (labelled lat/lon but, for projected UTM grids, actually
        // easting/northing). Reproject the corners to WGS84 so the coverage
        // registers in the shared viewport's pixel space.
        var crs = coverage.Georeferencer.CRS;
        var nativeToWgs84 = _crsTransformFactory.Create(crs, "EPSG:4326");

        var (west, south) = nativeToWgs84.IsIdentity
            ? (nativeViewport.MinLongitude, nativeViewport.MinLatitude)
            : nativeToWgs84.Transform(nativeViewport.MinLongitude, nativeViewport.MinLatitude);
        var (east, north) = nativeToWgs84.IsIdentity
            ? (nativeViewport.MaxLongitude, nativeViewport.MaxLatitude)
            : nativeToWgs84.Transform(nativeViewport.MaxLongitude, nativeViewport.MaxLatitude);

        return (west, east, south, north, nativeToWgs84);
    }

    private static Viewport BuildUnionViewport(
        SeamAwareBoundsAccumulator bounds, int widthPixels, int heightPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);

        if (!bounds.TryResolve(out double minX, out double minY, out double maxX, out double maxY))
        {
            minX = -1000; minY = -1000; maxX = 1000; maxY = 1000;
        }

        double spanX = maxX - minX;
        double spanY = maxY - minY;

        // Pad 10% (guard degenerate zero-span extents), matching the
        // single-dataset HeadlessVectorRenderer.FitViewport behaviour.
        double padX = spanX > 0 ? spanX * 0.1 : 1000;
        double padY = spanY > 0 ? spanY * 0.1 : 1000;
        minX -= padX; maxX += padX;
        minY -= padY; maxY += padY;
        spanX = maxX - minX;
        spanY = maxY - minY;

        // Expand the smaller dimension so the extent's aspect matches the output.
        double viewAspect = (double)widthPixels / heightPixels;
        double dataAspect = spanX / spanY;
        if (dataAspect > viewAspect)
        {
            double targetSpanY = spanX / viewAspect;
            double grow = (targetSpanY - spanY) / 2.0;
            minY -= grow; maxY += grow;
        }
        else
        {
            double targetSpanX = spanY * viewAspect;
            double grow = (targetSpanX - spanX) / 2.0;
            minX -= grow; maxX += grow;
        }

        var (minLon, minLat) = WebMercator.ToLonLat(minX, minY);
        var (maxLon, maxLat) = WebMercator.ToLonLat(maxX, maxY);

        double midLatRad = (minLat + maxLat) * 0.5 * Math.PI / 180.0;
        double groundMetresPerPixel = (maxX - minX) / widthPixels * Math.Cos(midLatRad);
        double denom = groundMetresPerPixel / ScaleVisibility.DenomToResolutionMetres;

        return new Viewport
        {
            MinLongitude = minLon,
            MaxLongitude = maxLon,
            MinLatitude = minLat,
            MaxLatitude = maxLat,
            WidthPixels = widthPixels,
            HeightPixels = heightPixels,
            ScaleDenominator = denom > 0 ? denom : 1.0,
        };
    }
}
