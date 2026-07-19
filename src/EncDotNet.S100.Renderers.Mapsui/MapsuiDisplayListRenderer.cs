using System.Diagnostics;
using System.Globalization;
using EncDotNet.S100.DataModel;
using EncDotNet.S100.Diagnostics;
using EncDotNet.S100.Pipelines;
using EncDotNet.S100.Pipelines.Vector;
using EncDotNet.S100.Pipelines.Vector.Caching;
using EncDotNet.S100.Portrayals;
using EncDotNet.S100.Renderers.Skia;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Projections;
using Mapsui.Styles;
using NetTopologySuite.Geometries;
using MapsuiColor = Mapsui.Styles.Color;
using S100Diag = EncDotNet.S100.Renderers.Mapsui.Diagnostics;
using Scene = EncDotNet.S100.Rendering.Scene;

namespace EncDotNet.S100.Renderers.Mapsui;

/// <summary>
/// Renders an S-100 Part 9 drawing-instruction display list into a Mapsui
/// <see cref="ILayer"/> by resolving feature geometry from an
/// <see cref="IFeatureGeometryProvider"/>, projecting to EPSG:3857, and
/// applying styles derived from the instruction properties.
/// </summary>
/// <remarks>
/// This renderer is product-agnostic: it consumes the unified
/// <see cref="DrawingInstruction"/> model (produced by S-101 Lua, S-124/S-129/S-421
/// XSLT, or other portrayal pipelines) and a geometry provider that knows how to
/// look up feature geometry for the current product.
/// </remarks>
public sealed class MapsuiDisplayListRenderer
{
    /// <summary>
    /// Key used to store the originating S-100 feature reference on Mapsui features.
    /// Consumers can read <c>feature[FeatureRefKey]</c> to trace a rendered feature
    /// back to its source dataset record.
    /// </summary>
    public const string FeatureRefKey = "S100.FeatureRef";

    /// <summary>
    /// Size, in millimetres, of one S-100 portrayal "pixel" on the nominal
    /// display surface (S-100 Part 9 §3.10.4 — 1 pixel = 0.32 mm).  Used to
    /// convert spec-defined widths from millimetres to Mapsui screen pixels.
    /// </summary>
    private const double S100PixelSizeMm = 0.32;

    /// <summary>Name assigned to the generated Mapsui layer.</summary>
    public string LayerName { get; set; } = "S-101 Vector";

    /// <summary>
    /// Optional S-100 product identifier (e.g. <c>"S-101"</c>, <c>"S-131"</c>)
    /// used as the <c>s100.product</c> dimension on cache and render-frame
    /// metrics. Set by dataset processors so cache hit/miss counts can be
    /// attributed by product. When <see langword="null"/>, the counter is
    /// emitted without a product tag (preserving legacy behaviour for
    /// direct callers).
    /// </summary>
    public string? Product { get; set; }

    /// <summary>
    /// The color palette to use for resolving S-100 color tokens.
    /// When set, overrides the built-in fallback colors.
    /// </summary>
    public ColorPalette? Palette { get; set; }

    /// <summary>
    /// Optional function that returns raw SVG content for a symbol name
    /// (e.g. "POSGEN03" → the contents of POSGEN03.svg).
    /// When set, point features will render using actual SVG symbols.
    /// </summary>
    public Func<string, string?>? SymbolProvider { get; set; }

    /// <summary>
    /// Optional function that returns an <see cref="AreaFill"/> definition by name.
    /// When set, non-colorFill area instructions will render using tiled SVG patterns.
    /// </summary>
    public Func<string, AreaFill?>? AreaFillProvider { get; set; }

    /// <summary>
    /// Optional function that returns a <see cref="LineStyle"/> definition by
    /// name. When set, line instructions that carry only a
    /// <c>lineStyleReference</c> (e.g. S-421 <c>RTEACTLEGLINE</c>) will render
    /// using the referenced colour, width, and dash pattern from the
    /// portrayal catalogue.
    /// </summary>
    public Func<string, LineStyle?>? LineStyleProvider { get; set; }

    /// <summary>
    /// Global scale factor applied to all point symbols (default 1.0).
    /// </summary>
    public double SymbolScale { get; set; } = 1.0;

    /// <summary>
    /// Global scale factor applied to all text labels (default 1.0).
    /// </summary>
    public double TextScale { get; set; } = 1.0;

    /// <summary>
    /// Optional dataset-wide out-of-scale-band cap, as an S-100 Part 9 §11.1
    /// scale denominator (the cell's most-permissive
    /// <c>DataCoverage.minimumDisplayScale</c>; S-101 FC §3.1.1). Applies only
    /// to the TiledScene ("B") render subsystem: it is propagated onto the
    /// <see cref="Scene.VectorScene"/> ops so that, like the Mapsui ("A")
    /// feature path (<c>MapsuiDatasetRenderer.ApplyOutOfScaleBandCap</c>),
    /// features lacking their own SCAMIN are hidden once the display is zoomed
    /// out past the cell's compilation scale. <see langword="null"/> (the
    /// default) applies no cap.
    /// </summary>
    public int? OutOfBandMinDisplayScale { get; set; }

    /// <summary>
    /// Optional shared cache for processed-SVG symbol entries and rasterised
    /// pattern tiles. When set, the renderer routes its symbol/pattern
    /// lookups through this cache so re-renders of the same dataset (e.g.
    /// after a palette toggle, time-step scrub, or mariner-setting change)
    /// reuse the SVG processing + pattern rasterization work. When unset
    /// (the default), a per-renderer cache is used, preserving legacy
    /// behaviour for ad-hoc / one-shot callers such as tests.
    /// </summary>
    public MapsuiRenderAssetCache? AssetCache { get; set; }

    // Per-renderer fallback used when AssetCache is null.
    private readonly MapsuiRenderAssetCache _localAssetCache = new();

    /// <summary>
    /// Optional cache for the pattern-fill priority clip result. When set
    /// together with <see cref="PatternClipCacheKey"/>, the renderer obtains
    /// the clipped pattern geometry via
    /// <see cref="IPatternClipCache.GetOrCompute"/> so re-renders that do not
    /// change the clip inputs (most importantly Day/Dusk/Night palette
    /// switches) skip the expensive NetTopologySuite overlay. When unset, the
    /// clip is computed inline on every render, preserving legacy behaviour for
    /// products without pattern fills and for ad-hoc / one-shot callers.
    /// </summary>
    public IPatternClipCache? PatternClipCache { get; init; }

    /// <summary>
    /// Key that fully identifies this render's pattern-clip inputs (for S-101,
    /// the mariner + ECDIS display-state portrayal cache key). Used with
    /// <see cref="PatternClipCache"/>; ignored when either is <see langword="null"/>.
    /// </summary>
    public string? PatternClipCacheKey { get; init; }

    /// <summary>
    /// Optional per-render override of the active base-plane render subsystem
    /// (see <see cref="RenderingOptimizations.RenderSubsystem"/>). When set, this
    /// render uses the specified arm regardless of the process-wide default,
    /// without mutating global state — making arm-specific behaviour
    /// deterministic and parallel-safe for tests and harnesses. When
    /// <see langword="null"/> (the default) the process-wide subsystem applies.
    /// </summary>
    public RenderSubsystemKind? RenderSubsystemOverride { get; init; }

    /// <summary>
    /// A cached SVG symbol: its Mapsui <c>svg-content://</c> source URI plus
    /// the pivot-to-bounds-centre offset recovered from the raw SVG before
    /// <see cref="SvgProcessor"/> stripped its layout elements.  The relative
    /// offset (in fractions of viewBox size) is what Mapsui's
    /// <c>RelativeOffset</c> consumes; the millimetre offset is retained in
    /// case a future code path needs an absolute-pixel translation.
    /// </summary>
    internal readonly record struct SymbolEntry(
        string? Source,
        double PivotOffsetXMm,
        double PivotOffsetYMm,
        double RelativeOffsetX,
        double RelativeOffsetY);

    /// <summary>
    /// Renders the supplied display list against the geometry provided by
    /// <paramref name="geometryProvider"/>, returning a Mapsui memory layer.
    /// </summary>
    /// <remarks>
    /// Drawing instructions whose feature reference cannot be resolved by
    /// the provider are silently skipped; this lets callers pre-process the
    /// list (e.g. merging S-101 SAFCON labels) without worrying about
    /// synthesised feature references.
    /// </remarks>
    public ILayer Render(
        IReadOnlyList<DrawingInstruction> instructions,
        IFeatureGeometryProvider geometryProvider)
    {
        ArgumentNullException.ThrowIfNull(instructions);
        ArgumentNullException.ThrowIfNull(geometryProvider);

        using var __activity = S100Diag.Telemetry.ActivitySource.StartActivity("s100.render.frame");
        __activity?.SetTag("s100.render.target", "mapsui");
        __activity?.SetTag("s100.render.instructions.count", instructions.Count);
        var renderStart = Stopwatch.GetTimestamp();

        S100Diag.Telemetry.InstructionsProcessed.Add(instructions.Count);

        // Ensure the custom pattern fill renderer is registered before Mapsui
        // encounters any AnchoredPatternFillStyle instances.
        AnchoredPatternFillRenderer.Register();

        // Ensure the vector snapshot custom layer renderer is registered before a
        // layer tagged with its CustomLayerRendererName reaches Mapsui. Without
        // this, any consumer of the Mapsui renderer that does not call
        // S100VectorSnapshotRenderer.Register() at startup (e.g. the headless
        // visual-regression harness) would have the tagged vector layer silently
        // skipped, producing a blank chart. Idempotent and a no-op when the
        // snapshot is disabled.
        S100VectorSnapshotRenderer.Register();

        // Likewise register the TiledScene ("B") custom layer renderers (both the
        // Phase-1 single-surface and Phase-2 tiled arms) so a layer tagged for
        // either portrays when that subsystem is active. Idempotent.
        S100VectorSceneRenderer.Register();
        S100VectorTileRenderer.Register();

        // 1. Sort instructions by rendering order: areas first, then lines, then points/text
        //    Within same type, sort by DrawingPriority
        var sorted = instructions
            .OrderBy(i => i.Plane == Pipelines.Vector.DisplayPlane.OverRadar ? 1 : 0)
            .ThenBy(i => i switch
            {
                AreaInstruction => 0,
                LineInstruction => 1,
                PointInstruction => 2,
                TextInstruction => 3,
                _ => 4,
            })
            .ThenBy(i => i.DrawingPriority)
            .ToList();

        // 2. Lower the non-pattern instructions into the shared, backend-agnostic
        //    VectorScene. All S-100 Part 9 correctness (draw ordering, colour /
        //    mm→px / symbol / line-style / text-anchor resolution, and the
        //    lat/lon → EPSG:3857 projection half) now lives in VectorSceneBuilder;
        //    the Mapsui-specific feature/style construction below merely consumes
        //    that IR. Pattern fills are intentionally NOT represented in the IR for
        //    this slice — they keep their dedicated collection / priority-clip /
        //    insert phase below, so Mapsui pattern output is byte-for-byte
        //    unchanged.
        var builder = new Scene.VectorSceneBuilder
        {
            ResolveColor = Scene.ColorResolver.Create(Palette),
            SymbolResolver = ResolveSymbolAsset,
            LineStyleProvider = LineStyleProvider,
            SymbolScale = SymbolScale,
            TextScale = TextScale,
        };
        var scene = builder.Build(instructions, geometryProvider);

        var mapFeatures = new List<IFeature>(scene.Ops.Count);
        // Tracks the (feature reference, EPSG:3857 X, Y) anchors that have
        // already been given a pick-target rectangle, so composite point
        // symbology emits exactly one rectangle per anchor (see the build loop).
        var pointHitRectKeys = new HashSet<(string FeatureReference, double X, double Y)>();
        var patternEntries = new List<(string PatternRef, int Priority, List<Polygon> Polygons)>();
        var nonPatternedColorFillPolygons = new List<Polygon>();
        int lastColorFillIndex = -1;

        // Select the base-plane render subsystem up front (design §4/§5). The
        // pattern bookkeeping, priority clip, and pattern-fill feature insertion
        // below feed the Mapsui feature ("A") arm exclusively: those features are
        // never rendered by the TiledScene ("B") arm (which paints patterns from
        // the IR scene bound to the layer) and carry no pick identity, so they are
        // pure dead weight there. Compute the arm now so the whole pattern phase
        // can be skipped when the B arm is active.
        var useTiledScene =
            (RenderSubsystemOverride ?? RenderingOptimizations.RenderSubsystem)
                == RenderSubsystemKind.TiledScene;

        // 3a. Pattern bookkeeping (A arm only): collect pattern polygons grouped
        //     by (pattern, priority), plus the non-patterned colour fills (e.g.
        //     land) that clip them. Pattern fills are merged per unique pattern so
        //     overlapping polygons with the same globally-anchored pattern are
        //     drawn exactly once. This mirrors the legacy single-pass collection
        //     and is kept separate from the IR for this slice.
        if (!useTiledScene)
        {
            var featuresWithPatterns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var instruction in sorted)
            {
                if (instruction is AreaInstruction { AreaFillReference: not null } pa)
                    featuresWithPatterns.Add(pa.FeatureReference);
            }

            foreach (var instruction in sorted)
            {
                var geom = geometryProvider.GetGeometry(instruction.FeatureReference);

                // LineInstructions with CoordinatesOverride carry their own
                // synthetic geometry (from augmented rays/arcs) and don't need
                // the feature's natural geometry to have coordinates.
                bool hasAugmentedLine = instruction is LineInstruction { CoordinatesOverride: not null };
                if (!hasAugmentedLine && (geom is null || geom.Coordinates.Count == 0))
                    continue;

                // Defer pattern fills for merging
                if (instruction is AreaInstruction { AreaFillReference: { } patternRef } areaPattern && geom is not null)
                {
                    // Inclusion gate: only collect the entry when the pattern
                    // resolves to a tile under the current palette (patterns with
                    // no resolvable asset are dropped, exactly as before). The
                    // resolved tile is discarded here; grouping/merging keys on the
                    // palette-independent pattern reference so the clip result is
                    // palette-independent and cacheable. The tile is re-resolved
                    // under the active palette after clipping.
                    if (GetPatternTilePng(patternRef) is not null)
                    {
                        var polygon = CreatePolygonFromGeometry(geom);
                        if (polygon is not null)
                        {
                            // Find existing entry with the same pattern reference and priority, or create a new one.
                            // OrdinalIgnoreCase matches MapsuiRenderAssetCache's tile-resolution
                            // comparer, so this grouping is exactly equivalent to the previous
                            // ReferenceEquals(TilePng) grouping (same fillName -> same byte[] ref).
                            var existing = patternEntries.Find(e =>
                                string.Equals(e.PatternRef, patternRef, StringComparison.OrdinalIgnoreCase)
                                && e.Priority == areaPattern.DrawingPriority);
                            if (existing.PatternRef is not null)
                            {
                                existing.Polygons.Add(polygon);
                            }
                            else
                            {
                                patternEntries.Add((patternRef, areaPattern.DrawingPriority, new List<Polygon> { polygon }));
                            }
                        }
                    }
                    continue;
                }

                // Track non-patterned color fills (e.g. land areas) for pattern clipping
                if (instruction is AreaInstruction { FillColor: not null } colorFill
                    && geom is not null
                    && !featuresWithPatterns.Contains(colorFill.FeatureReference))
                {
                    var polygon = CreatePolygonFromGeometry(geom);
                    if (polygon is not null)
                        nonPatternedColorFillPolygons.Add(polygon);
                }
            }
        }


        // 3b. Build Mapsui features from the IR, in Part 9 draw order. Solid-area
        //     ops mark the colour-fill boundary so merged patterns are inserted
        //     after them (preventing a solid fill from occluding a pattern). The
        //     scene contains the same non-pattern ops, in the same order, as the
        //     legacy single-pass loop produced.
        foreach (var op in scene.Ops)
        {
            // A composite point symbol (e.g. a multi-digit sounding) emits one
            // PointPaintOp per glyph, all sharing the same feature reference and
            // EPSG:3857 anchor. Each op would otherwise carry its own faint
            // pick-target rectangle; stacked, those rectangles darken into a
            // visible box. Emit the rectangle only for the first symbol at each
            // (feature, anchor) so picking still works without the artifact.
            var includePointHitRect = true;
            if (op is Scene.PointPaintOp { Symbol: not null } point)
                includePointHitRect = pointHitRectKeys.Add(
                    (point.FeatureReference, point.World.X, point.World.Y));

            var mapFeature = CreateMapFeature(op, includePointHitRect);
            if (mapFeature is null)
                continue;

            mapFeatures.Add(mapFeature);
            if (op is Scene.AreaPaintOp)
                lastColorFillIndex = mapFeatures.Count;
        }

        // Clip lower-priority pattern groups to exclude areas covered by
        // higher-priority patterns so that, e.g., DIAMOND1 (priority 9)
        // diamonds do not show through DQUAL (priority 12) pattern zones.
        // Also clips all patterns against non-patterned color fill areas
        // (e.g. land) so patterns don't bleed over land. A-arm only: the B arm
        // renders patterns from the IR scene and skips this phase entirely.
        if (!useTiledScene)
        {
            patternEntries.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            var clippedPatterns = PatternClipCache is not null && PatternClipCacheKey is not null
                ? PatternClipCache.GetOrCompute(
                    PatternClipCacheKey,
                    () => ClipPatternsByPriority(patternEntries, nonPatternedColorFillPolygons))
                : ClipPatternsByPriority(patternEntries, nonPatternedColorFillPolygons);

            // Insert merged pattern fill features after all color fills but before
            // lines/points/text. This ensures no solid fill can occlude a pattern.
            // The tile is re-resolved here under the active palette (the clip
            // geometry is palette-independent and may have come from the cache,
            // which is shared across palettes).
            int insertAt = lastColorFillIndex >= 0 ? lastColorFillIndex : 0;
            foreach (var (patternRef, _, geometry) in clippedPatterns)
            {
                var tile = GetPatternTilePicture(patternRef);
                if (tile is null)
                    continue;

                var feature = new GeometryFeature(geometry);
                feature.Styles.Add(new AnchoredPatternFillStyle
                {
                    Tile = tile.Value.Picture,
                    TileRect = tile.Value.Rect,
                });
                mapFeatures.Insert(insertAt, feature);
                insertAt++;
            }
        }

        S100Diag.Telemetry.StylesApplied.Add(mapFeatures.Sum(f => f.Styles.Count));
        S100Diag.Telemetry.FrameDuration.Record(
            (Stopwatch.GetTimestamp() - renderStart) * 1000.0 / Stopwatch.Frequency);

        // When the TiledScene ("B") arm is active, the layer is portrayed by
        // S100VectorSceneRenderer rasterising the VectorScene IR directly on a
        // worker — so build a *pattern-complete* scene (the Mapsui lowering above
        // deliberately omits patterns; the B arm renders them from the IR) and
        // bind it to the layer. Otherwise the snapshot ("A") arm (or the plain
        // per-feature path) renders the Mapsui features built above (including the
        // clipped pattern fills inserted in the A-arm-only phase).

        // Within the TiledScene subsystem, the Phase-2 tiled renderer is the
        // default; S100_VECTOR_SCENE_MODE=single selects the Phase-1
        // single-surface arm for A/B comparison. Both consume the same scene.
        var tiledRendererName = TiledSceneModeIsTiled
            ? S100VectorTileRenderer.RendererName
            : S100VectorSceneRenderer.RendererName;

        var layer = new InstrumentedMemoryLayer(Product)
        {
            Name = LayerName,
            Features = mapFeatures,
            Style = null,
            // Route the settled vector layer through the picture-snapshot
            // custom layer renderer when enabled, so pans replay a recorded
            // SKPicture instead of re-iterating every feature. No-op (null)
            // when the snapshot is disabled, leaving the normal per-feature
            // path (with the translation-invariant path cache) in place. When
            // the TiledScene subsystem is active it takes precedence.
            CustomLayerRendererName = useTiledScene
                ? tiledRendererName
                : S100VectorSnapshotRenderer.Enabled
                    ? S100VectorSnapshotRenderer.RendererName
                    : null,
        };

        if (useTiledScene)
        {
            var sceneBuilder = new Scene.VectorSceneBuilder
            {
                ResolveColor = Scene.ColorResolver.Create(Palette),
                SymbolResolver = ResolveSymbolAsset,
                LineStyleProvider = LineStyleProvider,
                PatternResolver = GetPatternTilePng,
                PatternClipCache = BuildPatternClipMemoizer(),
                SymbolScale = SymbolScale,
                TextScale = TextScale,
                OutOfBandMinDisplayScale = OutOfBandMinDisplayScale,
            };
            var builtScene = sceneBuilder.Build(instructions, geometryProvider);
            if (TiledSceneModeIsTiled)
            {
                S100VectorTileRenderer.BindScene(
                    layer,
                    builtScene,
                    productLayerSet: Product ?? LayerName,
                    styleStateHash: ComputeStyleStateHash(instructions));
            }
            else
            {
                S100VectorSceneRenderer.BindScene(layer, builtScene);
            }
        }

        return layer;
    }

    /// <summary>
    /// Computes the <c>styleStateHash</c> that keys the persistent tile disk
    /// cache (design §3.4). It must change whenever <em>anything</em> that alters
    /// the rasterised tile pixels changes, so a warm tile is never reused for a
    /// different style state. It folds:
    /// <list type="bullet">
    /// <item>the resolved drawing-instruction list — which already encodes the
    /// active display category, safety contour, and every other mariner setting
    /// that selects which features and which portrayal are drawn — serialized
    /// deterministically via <see cref="DrawingInstructionSerializer"/>;</item>
    /// <item>the colour palette (Day/Dusk/Night), which the scene builder applies
    /// on top of the instruction colour tokens;</item>
    /// <item>the symbol and text scale factors.</item>
    /// </list>
    /// The instruction-serializer format version is implicitly folded in (it is
    /// the first field of the serialized frame), so a serialization change also
    /// invalidates the namespace.
    /// </summary>
    private string ComputeStyleStateHash(IReadOnlyList<DrawingInstruction> instructions)
    {
        var instructionBytes = DrawingInstructionSerializer.Serialize(instructions);

        var styleHeader =
            $"palette:{DescribePalette(Palette)}" +
            $"|symbolScale:{SymbolScale.ToString("R", CultureInfo.InvariantCulture)}" +
            $"|textScale:{TextScale.ToString("R", CultureInfo.InvariantCulture)}";
        var headerBytes = System.Text.Encoding.UTF8.GetBytes(styleHeader);

        using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(
            System.Security.Cryptography.HashAlgorithmName.SHA256);
        sha.AppendData(headerBytes);
        sha.AppendData(instructionBytes);
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>
    /// Produces a deterministic fingerprint of a colour palette for the
    /// <c>styleStateHash</c>. <see cref="ColorPalette"/> does not override
    /// <see cref="object.ToString"/>, so using the instance directly collapses
    /// Day/Dusk/Night (and any two palettes) to the same type-name string —
    /// which made the tile disk-cache namespace palette-insensitive and caused a
    /// Night render to serve the previously-persisted Day tiles. The fingerprint
    /// folds the palette name <em>and</em> its resolved colour entries (ordered)
    /// so any difference in palette identity or content invalidates the cache.
    /// </summary>
    internal static string DescribePalette(ColorPalette? palette)
    {
        if (palette is null)
        {
            return "none";
        }

        var builder = new System.Text.StringBuilder(palette.Name);
        foreach (var entry in palette.Colors.OrderBy(c => c.Key, StringComparer.Ordinal))
        {
            builder.Append('|').Append(entry.Key).Append('=').Append(entry.Value);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Whether the TiledScene subsystem uses the Phase-2 tiled renderer (default)
    /// or the Phase-1 single-surface renderer. Sourced live from
    /// <see cref="RenderingOptimizations.SceneMode"/> (seeded from
    /// <c>S100_VECTOR_SCENE_MODE</c>; <c>single</c> selects the Phase-1 arm), so a
    /// runtime change applies on the next re-render.
    /// </summary>
    private static bool TiledSceneModeIsTiled =>
        RenderingOptimizations.SceneMode == VectorSceneMode.Tiled;

    private static MapsuiColor ToMapsui(RgbaColor c) => new(c.R, c.G, c.B, c.A);

    /// <summary>
    /// Resolves a symbol name to a processed-SVG asset for the shared
    /// <see cref="Scene.VectorSceneBuilder"/>, routing through the renderer's
    /// cache-backed <see cref="GetSymbolEntry"/> so symbol processing is reused
    /// across re-renders. The cached source carries the Mapsui
    /// <c>svg-content://</c> prefix; the IR stores the raw processed SVG, so the
    /// prefix is stripped here and re-applied by <see cref="CreatePointFeature"/>.
    /// </summary>
    private Scene.SymbolAsset? ResolveSymbolAsset(string symbolRef)
    {
        var entry = GetSymbolEntry(symbolRef);
        if (entry.Source is null)
            return null;

        const string prefix = "svg-content://";
        var processed = entry.Source.StartsWith(prefix, StringComparison.Ordinal)
            ? entry.Source[prefix.Length..]
            : entry.Source;

        return new Scene.SymbolAsset(processed, entry.RelativeOffsetX, entry.RelativeOffsetY);
    }

    /// <summary>
    /// Converts a single backend-agnostic <see cref="Scene.PaintOp"/> into a
    /// Mapsui <see cref="IFeature"/>, tagging it with the originating S-100
    /// feature reference and applying scale-visibility limits. Geometry in the
    /// op is already projected to EPSG:3857; sizes are already in display pixels.
    /// </summary>
    /// <param name="includePointHitRect">
    /// When the op is a <see cref="Scene.PointPaintOp"/>, controls whether the
    /// near-invisible pick-target rectangle is emitted. The caller suppresses it
    /// for every symbol after the first at a given feature/anchor so that
    /// composite point symbology (e.g. multi-digit soundings, where one anchor
    /// spawns several stacked symbols) does not accumulate the rectangle's
    /// faint fill into a visible box.
    /// </param>
    private IFeature? CreateMapFeature(Scene.PaintOp op, bool includePointHitRect = true)
    {
        IFeature? feature = op switch
        {
            Scene.AreaPaintOp area => CreateAreaFeature(area),
            Scene.LinePaintOp line => CreateLineFeature(line),
            Scene.PointPaintOp point => CreatePointFeature(point, includePointHitRect),
            Scene.TextPaintOp text => CreateTextFeature(text),
            _ => null,
        };

        if (feature is not null)
        {
            feature[FeatureRefKey] = op.FeatureReference;
            ApplyScaleVisibility(feature, op.ScaleMinimum, op.ScaleMaximum);
        }

        return feature;
    }

    /// <summary>
    /// S-100 Part 9 scale denominator → ground metres per display pixel at
    /// 96 DPI: 1 px = 0.28 mm = 0.00028 m on the nominal display surface, so
    /// <i>ground</i> resolution ≈ scaleDenominator × 0.00028. To obtain the
    /// Mapsui EPSG:3857 resolution (metres/pixel at the equator) this must be
    /// divided by <c>cos(latitude)</c> to undo web-mercator scale distortion —
    /// see <see cref="DenominatorToResolution"/>.
    /// </summary>
    public const double DenomToResolutionMetres = 0.00028;

    /// <summary>Earth radius (m) of the EPSG:3857 web-mercator sphere, matching
    /// <see cref="Scene.WebMercator.EarthRadius"/>.</summary>
    private const double WebMercatorEarthRadius = 6378137.0;

    /// <summary>
    /// Converts an EPSG:3857 northing (metres) to its geodetic latitude in
    /// radians, used to undo web-mercator scale distortion when mapping an
    /// S-100 true-scale denominator to a Mapsui resolution.
    /// </summary>
    internal static double WebMercatorYToLatitudeRadians(double y)
        => 2.0 * Math.Atan(Math.Exp(y / WebMercatorEarthRadius)) - Math.PI / 2.0;

    /// <summary>
    /// Converts an S-100 Part 9 §11.1 scale denominator (a <i>true-scale</i>
    /// value, e.g. SCAMIN / <c>minimumDisplayScale</c>) to the equivalent Mapsui
    /// EPSG:3857 resolution (metres/pixel at the equator) at
    /// <paramref name="latitudeRadians"/>. Because web-mercator inflates ground
    /// distances by <c>1/cos φ</c>, the equator-referenced resolution that
    /// corresponds to a true-scale denominator is
    /// <c>denom × 0.00028 / cos φ</c>. Omitting the <c>cos φ</c> term (the prior
    /// behaviour) is only correct on the equator and biases scale-visibility
    /// cutoffs toward hiding detail at finer zooms as latitude increases — at
    /// φ ≈ 50.8° (≈ 1/cos φ = 1.58) a cell's detail was suppressed roughly
    /// two-thirds of a zoom level too early. Matches the Skia headless backend,
    /// which already applies <c>cos(midLat)</c> (see
    /// <see cref="Scene.HeadlessVectorRenderer"/>).
    /// </summary>
    /// <param name="scaleDenominator">The S-100 true-scale denominator.</param>
    /// <param name="latitudeRadians">
    /// The representative latitude (radians) of the feature/cell the limit
    /// applies to; <c>0</c> (the equator) yields the uncorrected conversion.
    /// </param>
    /// <returns>The Mapsui EPSG:3857 resolution (m/px at the equator).</returns>
    internal static double DenominatorToResolution(double scaleDenominator, double latitudeRadians)
    {
        var cos = Math.Cos(latitudeRadians);
        if (cos < 1e-6)
        {
            // Guard against the poles / invalid latitudes (EPSG:3857 is clamped
            // to ±85.06°, where cos ≈ 0.087, so this only trips on bad input).
            cos = 1e-6;
        }

        return scaleDenominator * DenomToResolutionMetres / cos;
    }

    /// <summary>
    /// The representative latitude (radians) of a feature, taken from the centre
    /// of its EPSG:3857 extent. Geometry-less features (no extent) fall back to
    /// the equator, i.e. no web-mercator correction.
    /// </summary>
    private static double FeatureLatitudeRadians(IFeature feature)
    {
        var extent = feature.Extent;
        return extent is null
            ? 0.0
            : WebMercatorYToLatitudeRadians((extent.MinY + extent.MaxY) / 2.0);
    }

    /// <summary>
    /// Maps the S-100 Part 9 §11.1 scale denominators carried on a
    /// <see cref="Scene.PaintOp"/> onto each Mapsui style.  <c>ScaleMinimum</c>
    /// is the most zoomed-out limit (largest allowed denominator) and maps to
    /// Mapsui's <c>MaxVisible</c>; <c>ScaleMaximum</c> is the most zoomed-in
    /// limit (smallest allowed denominator) and maps to <c>MinVisible</c>. Both
    /// denominators are converted at the feature's latitude so the web-mercator
    /// resolution cutoffs line up with the feature's true scale.
    /// </summary>
    private static void ApplyScaleVisibility(IFeature feature, double? scaleMinimum, double? scaleMaximum)
    {
        if (!scaleMinimum.HasValue && !scaleMaximum.HasValue)
            return;

        var latitudeRadians = FeatureLatitudeRadians(feature);

        double? maxRes = scaleMinimum.HasValue
            ? DenominatorToResolution(scaleMinimum.Value, latitudeRadians)
            : (double?)null;
        double? minRes = scaleMaximum.HasValue
            ? DenominatorToResolution(scaleMaximum.Value, latitudeRadians)
            : (double?)null;

        foreach (var style in feature.Styles)
        {
            if (style is null) continue;
            if (maxRes.HasValue) style.MaxVisible = maxRes.Value;
            if (minRes.HasValue) style.MinVisible = minRes.Value;
        }
    }

    private static IFeature? CreateAreaFeature(Scene.AreaPaintOp op)
    {
        var polygon = CreatePolygonFromWorld(op.WorldShell, op.WorldHoles);
        if (polygon is null)
            return null;

        var style = new VectorStyle
        {
            Fill = new Brush { Color = ToMapsui(op.Fill) },
            Outline = new Pen { Color = ToMapsui(op.OutlineColor), Width = op.OutlineWidthPx },
        };

        var feature = new GeometryFeature(polygon);
        feature.Styles.Add(style);
        return feature;
    }

    private static IFeature? CreateLineFeature(Scene.LinePaintOp op)
    {
        if (op.World.Count < 2)
            return null;

        var coords = new Coordinate[op.World.Count];
        for (int i = 0; i < op.World.Count; i++)
            coords[i] = new Coordinate(op.World[i].X, op.World[i].Y);
        var lineString = new LineString(coords);

        var pen = new Pen
        {
            Color = ToMapsui(op.Color),
            Width = op.WidthPx,
        };
        if (op.DashArrayPx is { Count: > 0 } dashes)
        {
            pen.DashArray = dashes.ToArray();
            pen.PenStyle = PenStyle.UserDefined;
        }
        else if (op.DefaultDash)
        {
            pen.PenStyle = PenStyle.Dash;
        }

        var style = new VectorStyle
        {
            Line = pen,
            Fill = null,
            Outline = null,
        };

        var feature = new GeometryFeature(lineString);
        feature.Styles.Add(style);
        return feature;
    }

    private static IFeature CreatePointFeature(Scene.PointPaintOp op, bool includeHitRect = true)
    {
        var feature = new PointFeature(op.World.X, op.World.Y);

        var hasSymbolOffset = op.OffsetXpx != 0 || op.OffsetYpx != 0;

        if (op.Symbol is { } sym)
        {
            // The IR stores the raw processed SVG; Mapsui consumes it via the
            // svg-content:// pseudo-scheme.
            var svgSource = "svg-content://" + sym.ProcessedSvg;
            var svgScale = sym.Scale;

            // Mapsui's RelativeOffset uses +Y = up (map frame); the IR carries
            // the pivot in screen-space (+Y = down), so the Y component is
            // negated here.
            var pivotRelX = sym.PivotRelativeX;
            var pivotRelY = -sym.PivotRelativeY;
            var hasPivotRelative = pivotRelX != 0 || pivotRelY != 0;

            // Add a nearly-invisible rectangle as a hit-test area so that
            // tapping on a transparent portion of the SVG still picks this
            // feature.  The rectangle is slightly larger than the SVG to
            // provide a comfortable tap target.  Mapsui's pick is pixel-based,
            // so the fill must paint at least one alpha step; that means every
            // emitted rectangle contributes a faint darkening.  Composite point
            // symbology (e.g. a multi-digit sounding) places several symbols on
            // the same anchor, so the caller emits the rectangle for only the
            // first symbol at each feature/anchor (see the render loop's
            // dedupe).  Without that, the stacked rectangles accumulate into a
            // visible box around the sounding.
            if (includeHitRect)
            {
                var hitStyle = new SymbolStyle
                {
                    SymbolType = SymbolType.Rectangle,
                    SymbolScale = svgScale * 1.2,
                    Fill = new Brush { Color = new MapsuiColor(0, 0, 0, 1) },
                    Line = null,
                    Outline = null,
                };
                if (op.Rotation.HasValue)
                    hitStyle.SymbolRotation = op.Rotation.Value;
                if (hasSymbolOffset)
                    hitStyle.Offset = new Offset(op.OffsetXpx, op.OffsetYpx);
                if (hasPivotRelative)
                    hitStyle.RelativeOffset = new RelativeOffset(pivotRelX, pivotRelY);
                feature.Styles.Add(hitStyle);
            }

            var style = new ImageStyle
            {
                Image = new Image { Source = svgSource, RasterizeSvg = true },
            };
            style.SymbolScale = svgScale;
            if (op.Rotation.HasValue)
                style.SymbolRotation = op.Rotation.Value;
            if (hasSymbolOffset)
                style.Offset = new Offset(op.OffsetXpx, op.OffsetYpx);
            if (hasPivotRelative)
                style.RelativeOffset = new RelativeOffset(pivotRelX, pivotRelY);
            feature.Styles.Add(style);
        }
        else
        {
            // Fallback: colored dot
            var style = new SymbolStyle
            {
                SymbolScale = op.FallbackScale,
                Fill = new Brush { Color = ToMapsui(op.FallbackColor) },
                Line = null,
            };
            if (op.Rotation.HasValue)
                style.SymbolRotation = op.Rotation.Value;
            if (hasSymbolOffset)
                style.Offset = new Offset(op.OffsetXpx, op.OffsetYpx);
            feature.Styles.Add(style);
        }

        return feature;
    }

    private static IFeature CreateTextFeature(Scene.TextPaintOp op)
    {
        var style = new LabelStyle
        {
            Text = op.Text,
            ForeColor = ToMapsui(op.ForeColor),
            Font = new Font { Size = op.FontSizePx },
            HorizontalAlignment = MapHAlign(op.HorizontalAlignment),
            VerticalAlignment = MapVAlign(op.VerticalAlignment),
            Offset = new Offset(op.OffsetXpx, op.OffsetYpx),
            BackColor = op.BackColor is { } b ? new Brush { Color = ToMapsui(b) } : null,
        };

        var feature = new PointFeature(op.World.X, op.World.Y);
        feature.Styles.Add(style);
        return feature;
    }

    private static LabelStyle.HorizontalAlignmentEnum MapHAlign(TextHorizontalAlignment a) => a switch
    {
        TextHorizontalAlignment.Start => LabelStyle.HorizontalAlignmentEnum.Left,
        TextHorizontalAlignment.End => LabelStyle.HorizontalAlignmentEnum.Right,
        _ => LabelStyle.HorizontalAlignmentEnum.Center,
    };

    private static LabelStyle.VerticalAlignmentEnum MapVAlign(TextVerticalAlignment a) => a switch
    {
        TextVerticalAlignment.Top => LabelStyle.VerticalAlignmentEnum.Top,
        TextVerticalAlignment.Bottom => LabelStyle.VerticalAlignmentEnum.Bottom,
        _ => LabelStyle.VerticalAlignmentEnum.Center,
    };

    // ── Coordinate projection ──────────────────────────────────────────

    private static Polygon? CreatePolygonFromGeometry(FeatureGeometry geometry)
    {
        var shell = BuildLinearRing(geometry.Coordinates);
        if (shell is null)
            return null;

        if (geometry.InteriorRings.Count == 0)
            return new Polygon(shell);

        var holes = new List<LinearRing>(geometry.InteriorRings.Count);
        foreach (var hole in geometry.InteriorRings)
        {
            var ring = BuildLinearRing(hole);
            if (ring is not null)
                holes.Add(ring);
        }

        return holes.Count == 0
            ? new Polygon(shell)
            : new Polygon(shell, holes.ToArray());
    }

    private static LinearRing? BuildLinearRing(IReadOnlyList<GeoPosition> coords)
    {
        if (coords.Count < 3)
            return null;

        var projected = ProjectCoordinates(coords);

        // Close the ring if not already closed
        if (projected.Count > 0 && !projected[0].Equals2D(projected[^1]))
            projected.Add(new Coordinate(projected[0].X, projected[0].Y));

        if (projected.Count < 4)
            return null;

        return new LinearRing(projected.ToArray());
    }

    private static List<Coordinate> ProjectCoordinates(IReadOnlyList<GeoPosition> coords)
    {
        var result = new List<Coordinate>(coords.Count);
        foreach (var (lat, lon) in coords)
        {
            var (mx, my) = SphericalMercator.FromLonLat(lon, lat);
            result.Add(new Coordinate(mx, my));
        }
        return result;
    }

    /// <summary>
    /// Builds an NTS polygon from already-projected EPSG:3857 ring coordinates
    /// carried by an <see cref="Scene.AreaPaintOp"/>. Mirrors
    /// <see cref="CreatePolygonFromGeometry"/> (closing + minimum-vertex guards)
    /// but skips the lat/lon → EPSG:3857 projection, which the IR already
    /// performed, so degenerate rings are dropped identically to the legacy path.
    /// </summary>
    private static Polygon? CreatePolygonFromWorld(
        IReadOnlyList<(double X, double Y)> shell,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> holes)
    {
        var shellRing = BuildLinearRingFromWorld(shell);
        if (shellRing is null)
            return null;

        if (holes.Count == 0)
            return new Polygon(shellRing);

        var holeRings = new List<LinearRing>(holes.Count);
        foreach (var hole in holes)
        {
            var ring = BuildLinearRingFromWorld(hole);
            if (ring is not null)
                holeRings.Add(ring);
        }

        return holeRings.Count == 0
            ? new Polygon(shellRing)
            : new Polygon(shellRing, holeRings.ToArray());
    }

    private static LinearRing? BuildLinearRingFromWorld(IReadOnlyList<(double X, double Y)> coords)
    {
        if (coords.Count < 3)
            return null;

        var ring = new List<Coordinate>(coords.Count + 1);
        foreach (var (x, y) in coords)
            ring.Add(new Coordinate(x, y));

        // Close the ring if not already closed.
        if (ring.Count > 0 && !ring[0].Equals2D(ring[^1]))
            ring.Add(new Coordinate(ring[0].X, ring[0].Y));

        if (ring.Count < 4)
            return null;

        return new LinearRing(ring.ToArray());
    }

    /// <summary>
    /// Returns a cached <see cref="SymbolEntry"/> for the given symbol name,
    /// processing and caching the raw SVG on first access.  The entry's
    /// <c>Source</c> is <c>null</c> when no <see cref="SymbolProvider"/> is
    /// configured or the symbol cannot be resolved.
    /// </summary>
    private SymbolEntry GetSymbolEntry(string? symbolRef)
    {
        if (string.IsNullOrEmpty(symbolRef) || SymbolProvider is null)
            return default;

        var resolveStart = Stopwatch.GetTimestamp();
        var cache = AssetCache ?? _localAssetCache;

        var entry = cache.GetOrAddSymbol(Palette, symbolRef, out var wasCached, ProduceSymbolEntry);

        // Tag cache + resolve metrics with the active product (when known)
        // so dashboards can attribute hit/miss counts to S-101 vs. S-131
        // vs. an unconfigured caller.
        var productTag = new KeyValuePair<string, object?>(TelemetryTags.Product, Product);

        if (wasCached)
        {
            S100Diag.Telemetry.SymbolCacheHit.Add(1, productTag);
            S100Diag.Telemetry.SymbolResolveDuration.Record(
                (Stopwatch.GetTimestamp() - resolveStart) * 1000.0 / Stopwatch.Frequency,
                productTag,
                new KeyValuePair<string, object?>(TelemetryTags.SymbolResult, "hit"));
            return entry;
        }

        S100Diag.Telemetry.SymbolCacheMiss.Add(1, productTag);
        S100Diag.Telemetry.SymbolResolveDuration.Record(
            (Stopwatch.GetTimestamp() - resolveStart) * 1000.0 / Stopwatch.Frequency,
            productTag,
            new KeyValuePair<string, object?>(TelemetryTags.SymbolResult, entry.Source is null ? "fallback" : "miss"));
        return entry;
    }

    private SymbolEntry ProduceSymbolEntry(string symbolRef)
    {
        try
        {
            var svgContent = SymbolProvider!(symbolRef);
            if (svgContent is not null)
            {
                // Recover S-100 Part 9 §11.5 pivot placement from the *raw*
                // SVG before SvgProcessor strips the pivotPoint layout
                // element.  Without this, Mapsui centres the SVG bbox on the
                // anchor and composite symbols (e.g. multi-digit soundings)
                // collapse onto the same point.
                var pivot = SvgPivotMetrics.TryParse(svgContent);
                var processed = SvgProcessor.Process(svgContent, Palette);
                return new SymbolEntry(
                    "svg-content://" + processed,
                    pivot?.PivotToBoundsCenterMm.X ?? 0.0,
                    pivot?.PivotToBoundsCenterMm.Y ?? 0.0,
                    pivot?.RelativeOffset.X ?? 0.0,
                    pivot?.RelativeOffset.Y ?? 0.0);
            }
        }
        catch
        {
            // Symbol not found or malformed — fall back to dot
        }

        return default;
    }

    // ── Pattern tile rasterization ─────────────────────────────────────

    /// <summary>
    /// Returns rasterized pattern tile PNG bytes for the given area fill name,
    /// processing and caching on first access.
    /// </summary>
    private byte[]? GetPatternTilePng(string? fillName)
    {
        if (string.IsNullOrEmpty(fillName) || AreaFillProvider is null || SymbolProvider is null)
            return null;

        var cache = AssetCache ?? _localAssetCache;
        var tile = cache.GetOrAddPatternTile(Palette, fillName, out var wasCached, ProducePatternTile);

        // Pattern cache counters (PR-CACHE-7 in the asset-caching audit) so
        // every reuse of a rasterised pattern tile is visible alongside the
        // symbol cache counters.
        var productTag = new KeyValuePair<string, object?>(TelemetryTags.Product, Product);
        if (wasCached)
        {
            S100Diag.Telemetry.PatternCacheHit.Add(1, productTag);
        }
        else
        {
            S100Diag.Telemetry.PatternCacheMiss.Add(1, productTag);
        }
        return tile;
    }

    private byte[]? ProducePatternTile(string fillName)
    {
        try
        {
            var areaFill = AreaFillProvider!(fillName);
            if (areaFill?.PatternSymbol is not null)
            {
                var svgContent = SymbolProvider!(areaFill.PatternSymbol);
                if (svgContent is not null)
                {
                    var processed = SvgProcessor.Process(svgContent, Palette);
                    return SkiaSvgRasterizer.RasterizePatternTile(processed, areaFill);
                }
            }
        }
        catch
        {
            // Area fill or symbol not found — skip pattern
        }

        return null;
    }

    /// <summary>
    /// In-memory cache of resolution-independent pattern tile pictures, keyed by
    /// palette identity and area-fill name. Pictures are reused across the whole
    /// render so the SVG is parsed and recorded only once per pattern.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (SkiaSharp.SKPicture Picture, SkiaSharp.SKRect Rect)?> _patternPictureCache = new();

    /// <summary>
    /// Returns the resolution-independent pattern tile picture (and its millimetre
    /// repeat rectangle) for the given area-fill name, building and caching on
    /// first access. Returns <c>null</c> when the fill or its symbol cannot be
    /// resolved under the active palette.
    /// </summary>
    private (SkiaSharp.SKPicture Picture, SkiaSharp.SKRect Rect)? GetPatternTilePicture(string? fillName)
    {
        if (string.IsNullOrEmpty(fillName) || AreaFillProvider is null || SymbolProvider is null)
            return null;

        var paletteKey = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Palette!);
        var key = string.Concat(paletteKey.ToString(System.Globalization.CultureInfo.InvariantCulture), "|", fillName);
        return _patternPictureCache.GetOrAdd(key, _ => ProducePatternTilePicture(fillName));
    }

    private (SkiaSharp.SKPicture Picture, SkiaSharp.SKRect Rect)? ProducePatternTilePicture(string fillName)
    {
        try
        {
            var areaFill = AreaFillProvider!(fillName);
            if (areaFill?.PatternSymbol is not null)
            {
                var svgContent = SymbolProvider!(areaFill.PatternSymbol);
                if (svgContent is not null)
                {
                    var processed = SvgProcessor.Process(svgContent, Palette);
                    var picture = SkiaSvgRasterizer.BuildPatternTilePicture(processed, areaFill, out var rect);
                    if (picture is not null)
                        return (picture, rect);
                }
            }
        }
        catch
        {
            // Area fill or symbol not found — skip pattern
        }

        return null;
    }

    /// <summary>
    /// Clips lower-priority pattern groups by higher-priority pattern areas and by
    /// the opaque non-patterned colour fills (e.g. land), so a lower-priority
    /// pattern does not show through a higher-priority zone and no pattern bleeds
    /// over land. Delegates to the shared, backend-neutral
    /// <see cref="Scene.PatternPriorityClipper"/> so the Mapsui feature path and
    /// the <see cref="Scene.VectorScene"/> IR path (headless Skia + TiledScene)
    /// clip identically.
    /// </summary>
    /// <remarks>Entries must be sorted by ascending priority before calling;
    /// results are returned in the same order.</remarks>
    /// <summary>
    /// Adapts this renderer's <see cref="PatternClipCache"/> (an
    /// <see cref="IPatternClipCache"/> keyed by <see cref="PatternClipCacheKey"/>)
    /// into the <see cref="Scene.PatternClipMemoizer"/> consumed by
    /// <see cref="Scene.VectorSceneBuilder"/>, so the default TiledScene ("B") arm
    /// memoizes its pattern priority-clip exactly like the Mapsui feature ("A")
    /// arm. Returns <see langword="null"/> when no cache or key is configured, in
    /// which case the builder clips on every build. The clip result is
    /// palette-independent, so a Day/Dusk/Night switch (which recolours the
    /// pattern tiles applied after clipping) reuses the cached geometry.
    /// </summary>
    private Scene.PatternClipMemoizer? BuildPatternClipMemoizer()
    {
        if (PatternClipCache is not { } cache || PatternClipCacheKey is not { } key)
            return null;

        return compute => cache
            .GetOrCompute(key, () =>
            {
                var clipped = compute();
                var tuples = new List<(string PatternRef, int Priority, Geometry Geometry)>(clipped.Count);
                foreach (var c in clipped)
                    tuples.Add((c.PatternRef, c.Priority, c.Geometry));
                return tuples;
            })
            .Select(t => new Scene.PatternPriorityClipper.ClippedPattern(t.PatternRef, t.Priority, t.Geometry))
            .ToList();
    }

    private static List<(string PatternRef, int Priority, Geometry Geometry)> ClipPatternsByPriority(
        List<(string PatternRef, int Priority, List<Polygon> Polygons)> entries,
        List<Polygon> nonPatternedColorFills)
    {
        var groups = new List<Scene.PatternPriorityClipper.PatternGroup>(entries.Count);
        foreach (var entry in entries)
            groups.Add(new Scene.PatternPriorityClipper.PatternGroup(
                entry.PatternRef, entry.Priority, entry.Polygons));

        var clipped = Scene.PatternPriorityClipper.Clip(groups, nonPatternedColorFills);

        var result = new List<(string PatternRef, int Priority, Geometry Geometry)>(clipped.Count);
        foreach (var c in clipped)
            result.Add((c.PatternRef, c.Priority, c.Geometry));
        return result;
    }
}
