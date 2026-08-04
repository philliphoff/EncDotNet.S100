using System.Globalization;
using System.Runtime.CompilerServices;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Datasets.Pipelines;
using EncDotNet.S100.Datasets.Pipelines.Interoperability;
using EncDotNet.S100.Datasets.Pipelines.Portrayal;
using EncDotNet.S100.Pipelines;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using CoreRgbaColor = EncDotNet.S100.Pipelines.RgbaColor;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Converts a dataset processor's Mapsui-free portrayal output
/// (<see cref="IVectorPortrayalSource"/> / <see cref="ICoveragePortrayalSource"/>)
/// into a Mapsui-typed <see cref="MapsuiDatasetResult"/>. This is the Mapsui-aware
/// half of the portrayal-output seam: the processor (in the headless-facing
/// <c>EncDotNet.S100.Datasets.Pipelines</c> assembly) builds an immutable
/// snapshot of the dataset's portrayal, and this renderer rasterises it into
/// <c>ILayer</c>s, owning every Mapsui type (NTS pattern clipping, feature
/// tagging, out-of-scale-band cap, coverage / arrow / glyph layer build).
/// </summary>
/// <remarks>
/// <para>
/// Issue #189 keystone: relocating the <c>processor → ILayer</c> conversion
/// here lets the Pipelines assembly (and the headless facade / CLI that
/// reference it) drop Mapsui as a dependency.
/// </para>
/// <para>
/// This is a PLAIN renderer (consumes <see cref="IDatasetProcessor"/> +
/// <see cref="RenderContext"/>). Issue #213 will later unify it under
/// <c>IS100DatasetRenderer&lt;IReadOnlyList&lt;ILayer&gt;&gt;</c>; the present
/// shape is structured so that adoption is purely additive.
/// </para>
/// </remarks>
public sealed class MapsuiDatasetRenderer
{
    private readonly ICrsTransformFactory _crsTransformFactory;
    private readonly S100MapsuiOptions? _options;
    private readonly IPatternClipCache _patternClipCache;

    // The processor's portrayal build holds the processor's own render gate,
    // but the Mapsui conversion below uses a per-processor, non-thread-safe
    // render-asset cache, so the whole render is serialized per processor here.
    private static readonly ConditionalWeakTable<IDatasetProcessor, SemaphoreSlim> RenderGates = new();
    private static readonly ConditionalWeakTable<IDatasetProcessor, MapsuiRenderAssetCache> AssetCaches = new();

    /// <summary>
    /// Creates a new renderer.
    /// </summary>
    /// <param name="crsTransformFactory">
    /// CRS transform factory used by the coverage / arrow renderers to project
    /// the native grid CRS to EPSG:3857.
    /// </param>
    /// <param name="patternClipCache">
    /// Optional process-wide pattern-fill priority-clip cache (e.g. a
    /// <see cref="DiskPatternClipCache"/>) shared across every S-101 render, so
    /// the cold first open of a previously-seen cell skips the multi-second
    /// NetTopologySuite clip. When <see langword="null"/> an in-memory
    /// single-slot cache is retained for the lifetime of this renderer.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="crsTransformFactory"/> is
    /// <see langword="null"/>.
    /// </exception>
    public MapsuiDatasetRenderer(
        ICrsTransformFactory crsTransformFactory,
        IPatternClipCache? patternClipCache = null)
    {
        ArgumentNullException.ThrowIfNull(crsTransformFactory);
        _crsTransformFactory = crsTransformFactory;
        _patternClipCache = patternClipCache ?? new InMemoryPatternClipCache();
    }

    /// <summary>
    /// Creates a new renderer with captured Mapsui rendering configuration.
    /// </summary>
    /// <param name="crsTransformFactory">
    /// CRS transform factory used by the coverage / arrow renderers to project
    /// the native grid CRS to EPSG:3857.
    /// </param>
    /// <param name="patternClipCache">
    /// Optional process-wide pattern-fill priority-clip cache. When
    /// <see langword="null"/> an in-memory single-slot cache is retained for the
    /// lifetime of this renderer.
    /// </param>
    /// <param name="options">
    /// Captured rendering configuration.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="crsTransformFactory"/> or
    /// <paramref name="options"/> is <see langword="null"/>.
    /// </exception>
    public MapsuiDatasetRenderer(
        ICrsTransformFactory crsTransformFactory,
        IPatternClipCache? patternClipCache,
        S100MapsuiOptions options)
    {
        ArgumentNullException.ThrowIfNull(crsTransformFactory);
        ArgumentNullException.ThrowIfNull(options);
        _crsTransformFactory = crsTransformFactory;
        _patternClipCache = patternClipCache ?? new InMemoryPatternClipCache();
        _options = options;
    }

    internal long PatternClipCacheHits => _patternClipCache.Hits;

    internal long PatternClipCacheMisses => _patternClipCache.Misses;

    /// <summary>
    /// Renders the supplied processor's portrayal into Mapsui layers.
    /// </summary>
    /// <param name="processor">
    /// The dataset processor; must implement <see cref="IVectorPortrayalSource"/>
    /// or <see cref="ICoveragePortrayalSource"/>.
    /// </param>
    /// <param name="context">Optional render context (palette, scales, ECDIS, time step).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The Mapsui-typed render result.</returns>
    /// <exception cref="NotSupportedException">
    /// Thrown when the processor exposes neither portrayal-output capability.
    /// </exception>
    public async Task<MapsuiDatasetResult> RenderAsync(
        IDatasetProcessor processor,
        RenderContext? context = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(processor);
        cancellationToken.ThrowIfCancellationRequested();

        var gate = RenderGates.GetValue(processor, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (processor is IVectorPortrayalSource vectorSource)
            {
                var result = await vectorSource.BuildVectorPortrayalAsync(context, cancellationToken).ConfigureAwait(false);
                return ConvertVector(processor, result);
            }

            if (processor is ICoveragePortrayalSource coverageSource)
            {
                var result = await coverageSource.BuildCoveragePortrayalAsync(context, cancellationToken).ConfigureAwait(false);
                return ConvertCoverage(result);
            }

            throw new NotSupportedException(
                $"Processor '{processor.GetType().Name}' implements neither IVectorPortrayalSource nor "
                + "ICoveragePortrayalSource and cannot be rendered to Mapsui layers.");
        }
        finally
        {
            gate.Release();
        }
    }

    private MapsuiDatasetResult ConvertVector(IDatasetProcessor processor, VectorPortrayalResult result)
    {
        var assetCache = AssetCaches.GetValue(processor, static _ => new MapsuiRenderAssetCache());

        var layers = new List<ILayer>(result.SubLayers.Count);
        var stackEntries = new List<LayerStackEntry>(result.SubLayers.Count);
        MRect? union = null;

        foreach (var sub in result.SubLayers)
        {
            var renderer = new MapsuiDisplayListRenderer
            {
                LayerName = sub.LayerName,
                Product = result.Product,
                Palette = result.Palette,
                AssetCache = assetCache,
                PatternClipCache = sub.PatternClipCacheKey is not null
                    ? _patternClipCache
                    : null,
                PatternClipCacheKey = sub.PatternClipCacheKey is not null
                    ? QualifyPatternClipKey(sub.PatternClipCacheKey)
                    : null,
                SymbolScale = result.SymbolScale,
                TextScale = result.TextScale,
                SymbolProvider = result.SymbolProvider,
                AreaFillProvider = result.AreaFillProvider,
                LineStyleProvider = result.LineStyleProvider,
                Options = _options,
                // TiledScene ("B") subsystem only: the Mapsui ("A") path enforces
                // this cell-wide cap via per-feature MaxVisible below
                // (ApplyOutOfScaleBandCap). Propagating it here lets the
                // VectorScene IR honour it too, so features without their own
                // SCAMIN are hidden out of scale band in both subsystems.
                OutOfBandMinDisplayScale = sub.ApplyOutOfBandCap
                    ? result.OutOfBandMinDisplayScale
                    : null,
            };

            var layer = renderer.Render(sub.Instructions, result.GeometryProvider);

            TagFeatures(layer, result.FeatureTags);
            TagLineLodPyramids(layer, result.LineLodPyramids);

            if (sub.ApplyOutOfBandCap
                && result.OutOfBandMinDisplayScale is int denom
                && layer is MemoryLayer memoryLayer)
            {
                // Convert the cell-wide out-of-band denominator at the layer's
                // centre latitude so the cutoff matches the cell's true scale in
                // EPSG:3857 (web-mercator inflates ground distance by 1/cos φ).
                var layerExtent = memoryLayer.Extent;
                var latitudeRadians = layerExtent is null
                    ? 0.0
                    : MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians((layerExtent.MinY + layerExtent.MaxY) / 2.0);
                var cap = MapsuiDisplayListRenderer.DenominatorToResolution(denom, latitudeRadians);
                ApplyOutOfScaleBandCap(memoryLayer.Features, cap);
            }

            layers.Add(layer);
            stackEntries.Add(new LayerStackEntry(
                layer,
                new SubLayerStackItem(
                    new VectorStackPayload(result, sub),
                    sub.Plane,
                    sub.WithinPlanePriority,
                    result.SourceDatasetId,
                    sub.SourceFeatureType)
                {
                    SourceScaleDenominator = result.CellMinimumDisplayScale,
                }));

            union = Union(union, layer.Extent);
        }

        // GML XSLT products carry an authoritative, padded geographic extent
        // (GeographicExtent) used verbatim. S-131 prefers its rendered layer's
        // own extent but supplies a padded fallback for the layer-less case
        // (FallbackGeographicExtent). S-101 / S-57 set neither and derive the
        // extent purely from the built layers' union.
        var extent =
            ToMercator(result.GeographicExtent)
            ?? union
            ?? ToMercator(result.FallbackGeographicExtent)
            ?? new MRect(0, 0, 0, 0);

        return new MapsuiDatasetResult
        {
            Layers = layers,
            Extent = extent,
            Info = result.Info,
            Spec = result.Spec,
            LayerNames = result.LayerNames,
            StackEntries = stackEntries,
            CellMinimumDisplayScale = result.CellMinimumDisplayScale,
            CoverageGeometry = ToMercatorCoverage(result.CoverageAreas),
        };
    }

    private MapsuiDatasetResult ConvertCoverage(CoveragePortrayalResult result)
    {
        var layers = new List<ILayer>(result.SubLayers.Count);
        var stackEntries = new List<LayerStackEntry>(result.SubLayers.Count);
        var layerNames = new List<string>(result.SubLayers.Count);
        MRect? union = null;
        MRect? fallback = null;

        foreach (var sub in result.SubLayers)
        {
            ILayer? layer = null;

            switch (sub)
            {
                case GridCoverageSubLayer grid:
                    {
                        layer = BuildGridCoverageLayer(grid);
                        break;
                    }

                case ArrowCoverageSubLayer arrow:
                    {
                        var renderer = new MapsuiCoverageArrowRenderer(_crsTransformFactory)
                        {
                            LayerName = arrow.LayerName,
                            Palette = arrow.Palette,
                            BaseSymbolScale = arrow.BaseSymbolScale,
                            SymbolProvider = arrow.SymbolProvider,
                        };
                        layer = renderer.Render(arrow.Coverage, arrow.Viewport);
                        fallback = Union(fallback, ToMercator(arrow.FallbackExtent));
                        break;
                    }

                case GlyphCoverageSubLayer glyph:
                    {
                        layer = BuildGlyphLayer(glyph);
                        fallback = Union(fallback, ToMercator(glyph.Extent));
                        break;
                    }
            }

            if (layer is null)
                continue;

            layers.Add(layer);
            layerNames.Add(sub.LayerKey);
            stackEntries.Add(new LayerStackEntry(
                layer,
                new SubLayerStackItem(
                    new CoverageStackPayload(result, sub),
                    sub.Plane,
                    sub.WithinPlanePriority,
                    result.SourceDatasetId,
                    sub.SourceFeatureType)));

            union = Union(union, layer.Extent);
        }

        var extent = union ?? fallback ?? new MRect(0, 0, 0, 0);

        return new MapsuiDatasetResult
        {
            Layers = layers,
            Extent = extent,
            Info = result.Info,
            Spec = result.Spec,
            LayerNames = layerNames,
            StackEntries = stackEntries,
        };
    }

    /// <summary>
    /// Builds the Mapsui raster layer for an S-104-style gridded coverage
    /// surface, applying the optional S-98 land-area mask
    /// (<see cref="GridCoverageSubLayer.LandAreaMask"/>) so the surface is
    /// clipped to water (issue #483). Exposed so the S-98 layer-stack projector
    /// can rebuild the raster after an inter-product rule attaches a mask that
    /// was not present when the layer was first rasterised.
    /// </summary>
    /// <param name="grid">The gridded coverage sub-layer to rasterise.</param>
    /// <returns>The rasterised layer, or <see langword="null"/> if none was produced.</returns>
    public ILayer? BuildGridCoverageLayer(GridCoverageSubLayer grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var renderer = new MapsuiCoverageRenderer(_crsTransformFactory)
        {
            LayerName = grid.LayerName,
            LandAreas = grid.LandAreaMask,
        };
        return renderer.Render(grid.Coverage, grid.Viewport);
    }

    private static MemoryLayer BuildGlyphLayer(GlyphCoverageSubLayer sub)
    {
        var features = new List<IFeature>(sub.Glyphs.Count);

        foreach (var glyph in sub.Glyphs)
        {
            var feature = new GeometryFeature
            {
                Geometry = new Point(glyph.MercatorX, glyph.MercatorY),
            };
            feature[MapsuiDisplayListRenderer.FeatureRefKey] = glyph.FeatureRefTag;
            foreach (var (key, value) in glyph.Attributes)
                feature[key] = value;

            switch (glyph.Symbol)
            {
                case PointGlyphSymbol.Svg when glyph.SvgSource is not null:
                    feature.Styles.Add(new ImageStyle
                    {
                        Image = new Image { Source = glyph.SvgSource, RasterizeSvg = true },
                        SymbolScale = glyph.SymbolScale,
                        SymbolRotation = glyph.Rotation,
                    });
                    break;

                case PointGlyphSymbol.Triangle:
                    feature.Styles.Add(new SymbolStyle
                    {
                        SymbolType = SymbolType.Triangle,
                        Fill = new Brush(ToMapsuiColor(glyph.FillColor)),
                        Outline = new Pen(ToMapsuiColor(glyph.OutlineColor), glyph.OutlineWidth),
                        SymbolScale = glyph.SymbolScale,
                        SymbolRotation = glyph.Rotation,
                    });
                    break;

                default:
                    feature.Styles.Add(new SymbolStyle
                    {
                        SymbolType = SymbolType.Ellipse,
                        Fill = new Brush(ToMapsuiColor(glyph.FillColor)),
                        Outline = new Pen(ToMapsuiColor(glyph.OutlineColor), glyph.OutlineWidth),
                        SymbolScale = glyph.SymbolScale,
                        SymbolRotation = glyph.Rotation,
                    });
                    break;
            }

            features.Add(feature);
        }

        return new MemoryLayer
        {
            Name = sub.LayerName,
            Features = features,
            Style = null,
        };
    }

    /// <summary>
    /// Appends the Mapsui clip-algorithm parameters and serialization
    /// format-version to the processor's Mapsui-free identity key so the final
    /// pattern-clip cache key self-invalidates when any of those change. Mirrors
    /// the original in-processor key composition (S-101 PR-L2).
    /// </summary>
    private static string QualifyPatternClipKey(string identityKey)
    {
        var c = CultureInfo.InvariantCulture;
        return identityKey
            + "|tol:" + EncDotNet.S100.Rendering.Scene.PatternPriorityClipper.SimplifyToleranceMetres.ToString("R", c)
            + "|gate:" + EncDotNet.S100.Rendering.Scene.PatternPriorityClipper.MinPointsToSimplify.ToString(c)
            + "|fmt:" + DiskPatternClipCache.FormatVersion.ToString(c);
    }

    /// <summary>
    /// Copies pre-built line-LOD pyramids onto each Mapsui feature so the
    /// fast-line paint path (<c>CachedVectorStyleRenderer.DrawLine</c>) can
    /// skip the per-frame Douglas–Peucker pass. Runs after
    /// <see cref="TagFeatures"/> and follows the same feature-ref join key
    /// (<see cref="MapsuiDisplayListRenderer.FeatureRefKey"/>). No-op when
    /// no pyramids were pre-built at open (issue #489, PR-3).
    /// </summary>
    private static void TagLineLodPyramids(
        ILayer layer,
        IReadOnlyDictionary<long, EncDotNet.S100.Pipelines.Vector.Caching.LineLodPyramid>? pyramids)
    {
        if (pyramids is null || pyramids.Count == 0)
            return;
        if (layer is not MemoryLayer memoryLayer)
            return;

        foreach (var feature in memoryLayer.Features)
        {
            if (feature[MapsuiDisplayListRenderer.FeatureRefKey] is not string featureRef)
                continue;
            if (!long.TryParse(featureRef, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var id))
                continue;
            if (!pyramids.TryGetValue(id, out var pyramid))
                continue;

            feature[CachedVectorStyleRenderer.LineLodPyramidKey] = pyramid;
        }
    }

    /// <summary>
    /// Copies the S-98 feature tags (feature-type code and, for depth contours,
    /// the VALDCO depth value) onto each built Mapsui feature so the
    /// cross-dataset suppression rules can filter without re-portrayal
    /// (R-101-102-B). The feature id is read back from the renderer's
    /// feature-ref tag.
    /// </summary>
    private static void TagFeatures(ILayer layer, IReadOnlyDictionary<long, VectorFeatureTag>? tags)
    {
        if (tags is null || tags.Count == 0)
            return;
        if (layer is not MemoryLayer memoryLayer)
            return;

        foreach (var feature in memoryLayer.Features)
        {
            if (feature[MapsuiDisplayListRenderer.FeatureRefKey] is not string featureRef)
                continue;
            if (!long.TryParse(featureRef, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                continue;
            if (!tags.TryGetValue(id, out var tag))
                continue;

            feature[FeatureTagKeys.FeatureType] = tag.FeatureType;
            if (tag.DepthContourValue is not null)
                feature[FeatureTagKeys.DepthContourValue] = tag.DepthContourValue;
        }
    }

    /// <summary>
    /// Clamps the maximum visible resolution (zoomed-out limit) of every style
    /// on <paramref name="features"/> to <paramref name="maxResolution"/>,
    /// suppressing point/line/text detail past the cell's intended scale band
    /// (S-101 out-of-scale-band declutter, FC §3.1.1). Only ever tightens.
    /// Exposed for diagnostics and tests.
    /// </summary>
    public static void ApplyOutOfScaleBandCap(IEnumerable<IFeature> features, double maxResolution)
    {
        foreach (var feature in features)
        {
            foreach (var style in feature.Styles)
            {
                if (style is null) continue;
                if (style.MaxVisible > 0)
                    style.MaxVisible = Math.Min(style.MaxVisible, maxResolution);
            }
        }
    }

    /// <summary>
    /// Applies the hole-safe per-cell zoom-out visibility window (issue #438
    /// Phase 1) to a cell's built Mapsui layers: each layer stops drawing once
    /// the viewport is zoomed out beyond <paramref name="minimumDisplayScale"/>
    /// (the coarsest denominator in the cell's intended band, S-100 Part 17 /
    /// S-101 FC §3.1.1 <c>DataCoverage.minimumDisplayScale</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The denominator is converted to a Mapsui EPSG:3857 resolution at each
    /// layer's own extent-centre latitude (undoing web-mercator <c>1/cos φ</c>
    /// distortion, matching <see cref="ApplyOutOfScaleBandCap"/>) and clamped
    /// onto the layer's <c>MaxVisible</c>. The clamp only ever tightens, so an
    /// existing (smaller) cap is preserved.
    /// </para>
    /// <para>
    /// This is hole-safe: finer nested cells carry a smaller
    /// <paramref name="minimumDisplayScale"/> and therefore drop out first as
    /// the viewport zooms out, leaving the coarser cell underneath visible.
    /// Only the zoom-out edge is enforced — the zoom-in edge
    /// (<c>maximumDisplayScale</c> → <c>MinVisible</c>) is deliberately not
    /// applied here because a whole-cell zoom-in cutoff would blank areas a
    /// finer cell does not fully cover; that suppression is deferred to the
    /// coverage-clipping work (issue #438 Phase 2).
    /// </para>
    /// </remarks>
    /// <param name="layers">The cell's built layers.</param>
    /// <param name="minimumDisplayScale">
    /// The coarsest display-scale denominator of the cell's band (must be
    /// positive; non-positive values are ignored).
    /// </param>
    public static void ApplyCellScaleWindow(IEnumerable<ILayer> layers, int minimumDisplayScale)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (minimumDisplayScale <= 0)
            return;

        foreach (var layer in layers)
        {
            if (layer is not BaseLayer baseLayer)
                continue;

            var extent = layer.Extent;
            var latitudeRadians = extent is null
                ? 0.0
                : MapsuiDisplayListRenderer.WebMercatorYToLatitudeRadians((extent.MinY + extent.MaxY) / 2.0);
            var maxResolution = MapsuiDisplayListRenderer.DenominatorToResolution(minimumDisplayScale, latitudeRadians);

            baseLayer.MaxVisible = Math.Min(baseLayer.MaxVisible, maxResolution);
        }
    }

    private static MRect? Union(MRect? acc, MRect? next)
    {
        if (next is null)
            return acc;
        if (acc is null)
            return next;
        return acc.Join(next);
    }

    private static MRect? ToMercator(GeographicBounds? bounds)
    {
        if (bounds is not { } b)
            return null;
        var (minX, minY) = SphericalMercator.FromLonLat(b.MinLongitude, b.MinLatitude);
        var (maxX, maxY) = SphericalMercator.FromLonLat(b.MaxLongitude, b.MaxLatitude);
        return new MRect(minX, minY, maxX, maxY);
    }

    private static MRect? ToMercator(MercatorBounds? bounds)
    {
        if (bounds is not { } b)
            return null;
        return new MRect(b.MinX, b.MinY, b.MaxX, b.MaxY);
    }

    /// <summary>
    /// Projects a cell's EPSG:4326 <c>DataCoverage</c> polygons (S-101 FC §3.1.1;
    /// S-57 <c>M_COVR</c>) into a single EPSG:3857 (Web Mercator) geometry — the
    /// union of the individual coverage polygons, holes preserved — for
    /// cross-cell overlap suppression (issue #438 Phase 2). Returns
    /// <see langword="null"/> when the cell declares no usable coverage geometry
    /// or the projected rings are degenerate.
    /// </summary>
    private static Geometry? ToMercatorCoverage(IReadOnlyList<CoverageArea> areas)
    {
        if (areas.Count == 0)
            return null;

        var polygons = new List<Polygon>(areas.Count);
        foreach (var area in areas)
        {
            var shell = ToMercatorRing(area.ExteriorRing);
            if (shell is null)
                continue;

            LinearRing[]? holes = null;
            if (area.InteriorRings.Count > 0)
            {
                var holeList = new List<LinearRing>(area.InteriorRings.Count);
                foreach (var interior in area.InteriorRings)
                {
                    var hole = ToMercatorRing(interior);
                    if (hole is not null)
                        holeList.Add(hole);
                }

                if (holeList.Count > 0)
                    holes = holeList.ToArray();
            }

            polygons.Add(new Polygon(shell, holes));
        }

        if (polygons.Count == 0)
            return null;

        Geometry geometry = polygons.Count == 1
            ? polygons[0]
            : new MultiPolygon(polygons.ToArray());

        // Coverage rings can be self-touching or slightly non-simple after
        // projection; a zero-width buffer normalises them and unions the parts
        // into a clean footprint for reliable clip algebra.
        try
        {
            var normalized = geometry.Buffer(0);
            // A degenerate / zero-area coverage normalises to an empty geometry;
            // treat that as "no usable coverage" (return null so suppression is
            // simply disabled for the cell) rather than propagating the raw,
            // possibly non-simple geometry into Intersects / SKPath clipping,
            // where it risks TopologyExceptions or incorrect clipping.
            return normalized.IsEmpty ? null : normalized;
        }
        catch (NetTopologySuite.Geometries.TopologyException)
        {
            // Normalisation failed outright — disable suppression for this cell
            // rather than feed an invalid geometry downstream.
            return null;
        }
    }

    /// <summary>
    /// Projects a single EPSG:4326 coverage ring (lat/lon per S-100 Part 10b
    /// §6.2) to a closed EPSG:3857 <see cref="LinearRing"/>, or
    /// <see langword="null"/> when it has fewer than three distinct positions.
    /// </summary>
    private static LinearRing? ToMercatorRing(IReadOnlyList<GeoPosition> ring)
    {
        if (ring.Count < 3)
            return null;

        var coordinates = new List<Coordinate>(ring.Count + 1);
        foreach (var position in ring)
        {
            var (x, y) = SphericalMercator.FromLonLat(position.Longitude, position.Latitude);
            coordinates.Add(new Coordinate(x, y));
        }

        // Ensure the ring is explicitly closed for NTS.
        if (!coordinates[0].Equals2D(coordinates[^1]))
            coordinates.Add(coordinates[0].Copy());

        if (coordinates.Count < 4)
            return null;

        return new LinearRing(coordinates.ToArray());
    }

    private static Color ToMapsuiColor(CoreRgbaColor c) => new(c.R, c.G, c.B, c.A);
}
