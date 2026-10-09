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
    /// auto-fitted viewport. Tiled rendering turns it off and draws the world
    /// copies itself (see <see cref="HeadlessCompositeScene.Draw"/>).
    /// </summary>
    public bool EnableSeamWrap { get; init; } = true;
}

/// <summary>
/// A headless composite whose S-98 ordering, suppression and scene lowering are
/// already done, ready to paint against any number of viewports. Build one with
/// <see cref="HeadlessCompositor.Prepare"/> when rendering the same datasets
/// many times, as a tile pyramid does.
/// </summary>
/// <remarks>
/// A scene is immutable, and <see cref="Render"/> and <see cref="Draw"/> may be
/// called concurrently from several threads.
/// </remarks>
public sealed class HeadlessCompositeScene
{
    private readonly bool _hasBounds;
    private readonly double _minX;
    private readonly double _minY;
    private readonly double _maxX;
    private readonly double _maxY;

    internal HeadlessCompositeScene(
        IReadOnlyList<CompositeLayer> layers,
        SeamAwareBoundsAccumulator bounds,
        HeadlessCompositeOptions options)
    {
        Layers = layers;
        Background = options.Background;
        Basemap = options.Basemap;
        HonorScaleVisibility = options.HonorScaleVisibility;
        _hasBounds = bounds.TryResolve(out _minX, out _minY, out _maxX, out _maxY);
    }

    /// <summary>The lowered layers, bottom-most first.</summary>
    public IReadOnlyList<CompositeLayer> Layers { get; }

    /// <summary>The background <see cref="Render"/> clears to.</summary>
    public RgbaColor Background { get; }

    /// <summary>The basemap drawn beneath the chart layers.</summary>
    public BasemapKind Basemap { get; }

    /// <summary>Whether the vector layers apply scale-visibility culling.</summary>
    public bool HonorScaleVisibility { get; }

    /// <summary>
    /// Gets the EPSG:3857 union extent of the active layers. The extent is
    /// seam-aware: for data crossing the antimeridian, <paramref name="maxX"/>
    /// may lie east of +180° (see <see cref="SeamAwareBoundsAccumulator"/>).
    /// </summary>
    /// <param name="minX">The western edge, in metres.</param>
    /// <param name="minY">The southern edge, in metres.</param>
    /// <param name="maxX">The eastern edge, in metres.</param>
    /// <param name="maxY">The northern edge, in metres.</param>
    /// <returns><see langword="true"/> when any layer has geometry; otherwise <see langword="false"/>.</returns>
    public bool TryGetWorldBounds(out double minX, out double minY, out double maxX, out double maxY)
    {
        minX = _minX;
        minY = _minY;
        maxX = _maxX;
        maxY = _maxY;
        return _hasBounds;
    }

    /// <summary>
    /// Renders the scene into a new bitmap of the viewport's pixel size, cleared
    /// to <see cref="Background"/>.
    /// </summary>
    /// <param name="viewport">The viewport to render.</param>
    /// <returns>A newly allocated bitmap owned by the caller.</returns>
    public SKBitmap Render(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);

        var renderer = new HeadlessCompositeRenderer { Background = Background };
        return renderer.Render(viewport, WithBasemap(viewport));
    }

    /// <summary>
    /// Paints the basemap and the layers onto <paramref name="canvas"/> against
    /// <paramref name="viewport"/>, without clearing it. Each layer is also drawn
    /// shifted by each of <paramref name="longitudeOffsets"/> (in degrees, for
    /// example −360 and +360), so data stored one world east or west of the
    /// viewport shows up in it; layer order is kept across the copies.
    /// </summary>
    /// <param name="canvas">The canvas to paint onto.</param>
    /// <param name="viewport">A north-up viewport.</param>
    /// <param name="longitudeOffsets">Extra world copies to draw, in degrees; may be empty.</param>
    public void Draw(SKCanvas canvas, Viewport viewport, IReadOnlyList<double> longitudeOffsets)
    {
        ArgumentNullException.ThrowIfNull(canvas);
        ArgumentNullException.ThrowIfNull(viewport);
        ArgumentNullException.ThrowIfNull(longitudeOffsets);

        if (Basemap == BasemapKind.Offline)
        {
            new VectorCompositeLayer(NaturalEarthBasemap.GetLandScene(viewport), honorScaleVisibility: false)
                .Draw(canvas, viewport);
        }

        foreach (var layer in Layers)
        {
            layer.Draw(canvas, viewport);
            foreach (var offset in longitudeOffsets)
            {
                // Data stored at longitude L + offset lands where L would.
                layer.Draw(canvas, viewport with
                {
                    MinLongitude = viewport.MinLongitude + offset,
                    MaxLongitude = viewport.MaxLongitude + offset,
                });
            }
        }
    }

    private IReadOnlyList<CompositeLayer> WithBasemap(Viewport viewport)
    {
        if (Basemap != BasemapKind.Offline)
            return Layers;

        // The land basemap draws under every chart layer, registered with the
        // viewport, at a level of detail matching its pixel size (#411, #731).
        var layers = new List<CompositeLayer>(Layers.Count + 1)
        {
            new VectorCompositeLayer(NaturalEarthBasemap.GetLandScene(viewport), honorScaleVisibility: false),
        };
        layers.AddRange(Layers);
        return layers;
    }
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
        options ??= new HeadlessCompositeOptions();
        var scene = Prepare(datasets, options);

        // Explicit viewport wins; otherwise seam-aware auto-fit of the union extent.
        var viewport = options.Viewport ?? BuildUnionViewport(scene, options.Width, options.Height);
        return scene.Render(viewport);
    }

    /// <summary>
    /// Orders, rules and lowers the supplied datasets once, returning a scene
    /// that can be painted against many viewports.
    /// </summary>
    /// <param name="datasets">
    /// The datasets to composite, in draw order (bottom-most first). Each carries
    /// a vector or coverage portrayal result.
    /// </param>
    /// <param name="options">
    /// Composite options. <see cref="HeadlessCompositeOptions.Width"/>,
    /// <see cref="HeadlessCompositeOptions.Height"/> and
    /// <see cref="HeadlessCompositeOptions.Viewport"/> are not used.
    /// </param>
    /// <returns>The prepared scene.</returns>
    public HeadlessCompositeScene Prepare(
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
                        lowered.Add(new VectorCompositeLayer(scene, options.HonorScaleVisibility)
                        {
                            EnableSeamWrap = options.EnableSeamWrap,
                            // The whole cell stops at its minimum display scale
                            // unless the mariner asks to ignore it (S-101 FC §3.1.1).
                            MinimumDisplayScale = (options.Mariner?.IgnoreScaleMinimum ?? false)
                                ? null
                                : vector.Result.CellMinimumDisplayScale,
                        });
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

        return new HeadlessCompositeScene(lowered, bounds, options);
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
                    SourceScaleDenominator = vector.CellCompilationScale ?? vector.CellMinimumDisplayScale,
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
                    var extent = ReprojectExtent(grid.Coverage);
                    if (extent is null)
                        return false;

                    var (w, e, s, n, nativeToWgs84) = extent.Value;
                    layer = new CoverageCompositeLayer(
                        grid.Coverage, w, e, s, n, nativeToWgs84: nativeToWgs84, landAreas: grid.LandAreaMask)
                    {
                        Opacity = grid.Opacity,
                    };
                    west = w; east = e; south = s; north = n;
                    return true;
                }

            case ArrowCoverageSubLayer arrow:
                {
                    var extent = ReprojectExtent(arrow.Coverage);
                    if (extent is null)
                        return false;

                    var (w, e, s, n, nativeToWgs84) = extent.Value;
                    var arrowRenderer = new SkiaCoverageArrowRenderer
                    {
                        // Without the palette the SCAROW fSCBN{N} colour tokens
                        // stay unresolved and every arrow draws black.
                        Palette = arrow.Palette,
                        SymbolProvider = arrow.SymbolProvider,
                        BaseSymbolScale = arrow.BaseSymbolScale,
                    };
                    layer = new CoverageCompositeLayer(arrow.Coverage, w, e, s, n, arrowRenderer, nativeToWgs84);
                    west = w; east = e; south = s; north = n;
                    return true;
                }

            case GlyphCoverageSubLayer glyph:
                {
                    if (glyph.Extent is not { } extent)
                        return false;

                    layer = PointGlyphHeadlessAdapter.CreateLayer(glyph);
                    var (w, s) = WebMercator.ToLonLat(extent.MinX, extent.MinY, clampLatitude: false);
                    var (e, n) = WebMercator.ToLonLat(extent.MaxX, extent.MaxY, clampLatitude: false);
                    west = w; east = e; south = s; north = n;
                    return true;
                }

            default:
                return false;
        }
    }

    private (double West, double East, double South, double North, ICrsTransform NativeToWgs84)? ReprojectExtent(
        StyledCoverageLayer coverage)
    {
        var metadata = coverage.Georeferencer.Metadata;
        if (metadata.NumRows <= 0 || metadata.NumColumns <= 0)
            return null;

        // Sampled grid nodes are cell centres. Derive the native bounds from the
        // sampled metadata and add the half-cell pad before reprojection so a
        // viewport subset is neither stretched to the source extent nor clipped
        // through its outer cells.
        double xEnd = metadata.OriginLongitude
            + (metadata.NumColumns - 1) * metadata.SpacingLongitudinal;
        double yEnd = metadata.OriginLatitude
            + (metadata.NumRows - 1) * metadata.SpacingLatitudinal;
        double halfCellX = Math.Abs(metadata.SpacingLongitudinal) / 2.0;
        double halfCellY = Math.Abs(metadata.SpacingLatitudinal) / 2.0;
        double minX = Math.Min(metadata.OriginLongitude, xEnd) - halfCellX;
        double maxX = Math.Max(metadata.OriginLongitude, xEnd) + halfCellX;
        double minY = Math.Min(metadata.OriginLatitude, yEnd) - halfCellY;
        double maxY = Math.Max(metadata.OriginLatitude, yEnd) + halfCellY;

        var crs = coverage.Georeferencer.CRS;
        var nativeToWgs84 = _crsTransformFactory.Create(crs, "EPSG:4326");

        var corners = new (double Longitude, double Latitude)[4];
        corners[0] = Transform(minX, minY);
        corners[1] = Transform(minX, maxY);
        corners[2] = Transform(maxX, minY);
        corners[3] = Transform(maxX, maxY);

        double west = corners.Min(corner => corner.Longitude);
        double east = corners.Max(corner => corner.Longitude);
        double south = corners.Min(corner => corner.Latitude);
        double north = corners.Max(corner => corner.Latitude);

        return (west, east, south, north, nativeToWgs84);

        (double Longitude, double Latitude) Transform(double x, double y) =>
            nativeToWgs84.IsIdentity ? (x, y) : nativeToWgs84.Transform(x, y);
    }

    private static Viewport BuildUnionViewport(
        HeadlessCompositeScene scene, int widthPixels, int heightPixels)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(widthPixels);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(heightPixels);

        if (!scene.TryGetWorldBounds(out double minX, out double minY, out double maxX, out double maxY))
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
