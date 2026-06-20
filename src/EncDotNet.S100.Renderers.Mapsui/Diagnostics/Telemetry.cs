using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using EncDotNet.S100.Diagnostics;

namespace EncDotNet.S100.Renderers.Mapsui.Diagnostics;

/// <summary>Per-assembly <see cref="ActivitySource"/> and <see cref="Meter"/> for <c>EncDotNet.S100.Renderers.Mapsui</c>.</summary>
internal static class Telemetry
{
    public static readonly ActivitySource ActivitySource =
        S100Telemetry.CreateActivitySource(typeof(Telemetry));

    public static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(Telemetry));

    /// <summary>
    /// Per-product (or untagged) call-rate state used to derive
    /// <c>s100.layer.getfeatures.fps</c> as an <see cref="ObservableGauge{T}"/>.
    /// Each <see cref="InstrumentedMemoryLayer.GetFeatures"/> invocation
    /// calls <see cref="RecordGetFeaturesCall"/>, which atomically bumps
    /// <see cref="CallStats.Count"/> for the matching product key. The
    /// gauge callback (registered below) computes <c>delta-count /
    /// delta-time</c> across export intervals to emit a true rate.
    /// </summary>
    private sealed class CallStats
    {
        public long Count;
        public long LastReadCount;
        public long LastReadTimestamp = Stopwatch.GetTimestamp();
    }

    private static readonly ConcurrentDictionary<string, CallStats> s_callStats =
        new ConcurrentDictionary<string, CallStats>();

    /// <summary>
    /// Records a single <c>GetFeatures</c> invocation for fps accounting.
    /// Pass <see langword="null"/> when no product is configured; the
    /// stats are aggregated under an empty key (emitted as an untagged
    /// gauge measurement).
    /// </summary>
    internal static void RecordGetFeaturesCall(string? product)
    {
        var key = product ?? string.Empty;
        var stats = s_callStats.GetOrAdd(key, static _ => new CallStats());
        Interlocked.Increment(ref stats.Count);
    }

    public static readonly Histogram<double> FrameDuration =
        Meter.CreateHistogram<double>(
            name: "s100.render.frame.duration",
            unit: "ms",
            description: "Wall-clock duration of a Mapsui display-list render pass.");

    public static readonly Counter<long> InstructionsProcessed =
        Meter.CreateCounter<long>(
            name: "s100.render.instructions.processed.count",
            unit: "{instructions}",
            description: "Drawing instructions processed per Mapsui render pass.");

    public static readonly Counter<long> StylesApplied =
        Meter.CreateCounter<long>(
            name: "s100.render.styles.applied.count",
            unit: "{styles}",
            description: "Mapsui styles applied per render pass.");

    public static readonly Histogram<double> SymbolResolveDuration =
        Meter.CreateHistogram<double>(
            name: "s100.symbol.resolve.duration",
            unit: "ms",
            description: "Duration of a single SVG symbol resolution (hit, miss, or fallback).");

    public static readonly Counter<long> SymbolCacheHit =
        Meter.CreateCounter<long>(
            name: "s100.symbol.cache.hit.count",
            unit: "{hits}",
            description: "Symbol cache hits during symbol resolution. Tagged with s100.product when the renderer is configured by a dataset processor.");

    public static readonly Counter<long> SymbolCacheMiss =
        Meter.CreateCounter<long>(
            name: "s100.symbol.cache.miss.count",
            unit: "{misses}",
            description: "Symbol cache misses during symbol resolution. Tagged with s100.product when the renderer is configured by a dataset processor.");

    /// <summary>
    /// Pattern-tile cache hits during area-fill resolution. Tagged with
    /// <c>s100.product</c> when the renderer is configured by a dataset
    /// processor. Wired in <c>MapsuiDisplayListRenderer.GetPatternTilePng</c>
    /// (per the asset-caching audit's PR-CACHE-7 recommendation).
    /// </summary>
    public static readonly Counter<long> PatternCacheHit =
        Meter.CreateCounter<long>(
            name: "s100.pattern.cache.hit.count",
            unit: "{hits}",
            description: "Pattern-tile cache hits during area-fill resolution. Tagged with s100.product when the renderer is configured by a dataset processor.");

    /// <inheritdoc cref="PatternCacheHit"/>
    public static readonly Counter<long> PatternCacheMiss =
        Meter.CreateCounter<long>(
            name: "s100.pattern.cache.miss.count",
            unit: "{misses}",
            description: "Pattern-tile cache misses during area-fill resolution (tile rasterisation triggered). Tagged with s100.product when the renderer is configured by a dataset processor.");

    /// <summary>
    /// Hits in the <see cref="CachedVectorStyleRenderer"/> path cache. Recorded
    /// per cached geometry per rendered frame; the hit-rate
    /// (hits / (hits + misses)) measures steady-state pan effectiveness — a pan
    /// at constant resolution should be all hits. Tagged with <c>s100.product</c>.
    /// </summary>
    public static readonly Counter<long> SimplifyCacheHit =
        Meter.CreateCounter<long>(
            name: "s100.simplify.cache.hit.count",
            unit: "{hits}",
            description: "Hits in the CachedVectorStyleRenderer path cache. Tagged with s100.product.");

    /// <inheritdoc cref="SimplifyCacheHit"/>
    public static readonly Counter<long> SimplifyCacheMiss =
        Meter.CreateCounter<long>(
            name: "s100.simplify.cache.miss.count",
            unit: "{misses}",
            description: "Misses in the CachedVectorStyleRenderer path cache (path build triggered). Tagged with s100.product.");

    /// <summary>
    /// Running total of coordinates retained in the
    /// <see cref="CachedVectorStyleRenderer"/> path cache. Bounded by the
    /// renderer's coordinate budget; this is the signal to watch for
    /// cache-pressure tuning. Increments on cache adds, decrements on eviction.
    /// </summary>
    public static readonly UpDownCounter<long> SimplifyCacheCoordsTracked =
        Meter.CreateUpDownCounter<long>(
            name: "s100.simplify.cache.coords.tracked",
            unit: "{coordinates}",
            description: "Total coordinates currently retained in the CachedVectorStyleRenderer path cache. Tagged with s100.product.");

    /// <summary>
    /// Wall-clock duration of a single Mapsui <c>GetFeatures(rect,resolution)</c>
    /// call on an <see cref="InstrumentedMemoryLayer"/>. Mapsui invokes this
    /// once per visible layer per rendered frame, so the histogram reflects
    /// the per-frame extent-filter cost. Tagged with <c>s100.product</c>
    /// when the renderer is configured by a dataset processor.
    /// </summary>
    public static readonly Histogram<double> LayerGetFeaturesDuration =
        Meter.CreateHistogram<double>(
            name: "s100.layer.getfeatures.duration",
            unit: "ms",
            description: "Wall-clock duration of MemoryLayer.GetFeatures (per-frame extent filter cost).");

    /// <summary>
    /// Number of features returned by a single <c>GetFeatures</c> call —
    /// i.e. the count of features whose extent intersects the visible
    /// viewport (K). Combined with <see cref="LayerGetFeaturesTotalCount"/>
    /// this gives the K/N selectivity that determines how much a spatial
    /// index would help: low K/N at a given zoom = big win.
    /// </summary>
    public static readonly Histogram<long> LayerGetFeaturesVisibleCount =
        Meter.CreateHistogram<long>(
            name: "s100.layer.getfeatures.visible.count",
            unit: "{features}",
            description: "Features returned by MemoryLayer.GetFeatures (visible/in-extent count K per frame).");

    /// <summary>
    /// Total feature count scanned by a <c>GetFeatures</c> call (N).
    /// This is the size of the layer's feature list, identical for every
    /// frame until the layer is re-rendered. Recorded per call so the
    /// histogram reports it alongside the visible count without the
    /// caller needing to correlate metrics.
    /// </summary>
    public static readonly Histogram<long> LayerGetFeaturesTotalCount =
        Meter.CreateHistogram<long>(
            name: "s100.layer.getfeatures.total.count",
            unit: "{features}",
            description: "Total feature count scanned by MemoryLayer.GetFeatures (N per frame).");

    /// <summary>
    /// Cumulative count of <c>GetFeatures</c> calls per product. Most
    /// observability backends derive a per-second rate from a counter
    /// automatically; the dedicated <c>s100.layer.getfeatures.fps</c>
    /// gauge below provides the same value pre-computed for the
    /// console exporter, where counters are reported as totals over the
    /// export interval.
    /// </summary>
    public static readonly Counter<long> LayerGetFeaturesCalls =
        Meter.CreateCounter<long>(
            name: "s100.layer.getfeatures.calls.count",
            unit: "{calls}",
            description: "GetFeatures call count per product (each call ~= one rendered frame for that product).");

    /// <summary>
    /// Per-product frame rate (calls/second of <c>GetFeatures</c>),
    /// computed from the rolling call counter on each meter export.
    /// For a single visible layer per product this matches the map
    /// control's effective fps; if N visible layers of the same product
    /// are stacked, divide by N for the actual frame rate.
    /// </summary>
    /// <remarks>
    /// Implemented as a multi-measurement <see cref="ObservableGauge{T}"/>
    /// so a single instrument emits one sample per active product on
    /// every collection cycle. Untagged measurements (empty product
    /// key) are emitted with no <c>s100.product</c> tag, preserving
    /// legacy behaviour for ad-hoc callers.
    /// </remarks>
    public static readonly ObservableGauge<double> LayerGetFeaturesFps =
        Meter.CreateObservableGauge<double>(
            name: "s100.layer.getfeatures.fps",
            observeValues: ObserveLayerGetFeaturesFps,
            unit: "{frames}/s",
            description: "Average GetFeatures call rate per product (~fps for single-layer maps).");

    /// <summary>
    /// Inter-frame interval recorded on each <c>GetFeatures</c> entry
    /// (gap since the previous call returned), in milliseconds.
    /// Subtracting the matching
    /// <see cref="LayerGetFeaturesDuration"/> sample yields the
    /// render-plus-paint slice of frame time — i.e. everything Mapsui
    /// and Skia did between two consecutive feature pulls. Idle gaps
    /// (longer than ~500 ms) are not recorded so the histogram
    /// reflects active rendering only.
    /// </summary>
    public static readonly Histogram<double> LayerFrameInterval =
        Meter.CreateHistogram<double>(
            name: "s100.layer.frame.interval",
            unit: "ms",
            description: "Inter-frame interval (gap between consecutive GetFeatures calls). Subtract LayerGetFeaturesDuration to estimate render+paint cost.");

    /// <summary>
    /// Per-call duration of <c>AnchoredPatternFillRenderer.Draw</c>, in
    /// milliseconds. Pattern fills are the most expensive S-101 style
    /// we render ourselves; this histogram quantifies their share of
    /// post-filter frame time. Combined with
    /// <see cref="LayerFrameInterval"/> minus
    /// <see cref="LayerGetFeaturesDuration"/>, the residue is time
    /// Mapsui's stock style renderers (vector / label / symbol) spent
    /// on the layer.
    /// </summary>
    public static readonly Histogram<double> PatternFillDrawDuration =
        Meter.CreateHistogram<double>(
            name: "s100.style.pattern_fill.draw.duration",
            unit: "ms",
            description: "Per-call duration of AnchoredPatternFillRenderer.Draw (one call per pattern-fill feature per frame).");

    /// <summary>
    /// Wall-clock duration of a single off-thread <c>VectorScene</c>
    /// rasterisation by the <c>TiledScene</c> ("B") render subsystem
    /// (<see cref="S100VectorSceneRenderer"/>) — the worker-thread cost of
    /// turning the scene IR into one device-resolution <c>SKImage</c> for the
    /// whole viewport plus over-render margin. This is the cost prediction and
    /// tiling (Phases&#160;2–3) exist to amortise; on the "A" arm the comparable
    /// work is the synchronous per-feature paint reflected in
    /// <see cref="FrameDuration"/> / the viewer's map-paint histogram. See
    /// <c>docs/design/S100-Render-Subsystem-Design.md</c> §4.
    /// </summary>
    public static readonly Histogram<double> SceneRasterizeDuration =
        Meter.CreateHistogram<double>(
            name: "s100.render.scene.rasterize.duration",
            unit: "ms",
            description: "Wall-clock duration of one off-thread VectorScene rasterisation by the TiledScene render subsystem (whole viewport + margin).");

    /// <summary>
    /// Wall-clock duration of a single UI-thread composite (translated
    /// <c>SKImage</c> blit) by the <c>TiledScene</c> ("B") render subsystem
    /// (<see cref="S100VectorSceneRenderer"/>). This is the per-frame work that
    /// stays on the render thread during a pan; keeping it bounded and
    /// independent of feature count is the whole point of the subsystem. See
    /// <c>docs/design/S100-Render-Subsystem-Design.md</c> §3.5.
    /// </summary>
    public static readonly Histogram<double> SceneCompositeDuration =
        Meter.CreateHistogram<double>(
            name: "s100.render.scene.composite.duration",
            unit: "ms",
            description: "Wall-clock duration of one UI-thread composite (translated SKImage blit) by the TiledScene render subsystem.");

    /// <summary>
    /// Wall-clock duration of one off-thread tile rasterisation (core + gutter)
    /// by the tiled <c>TiledScene</c> arm (<see cref="S100VectorTileRenderer"/>,
    /// Phase&#160;2). A constant-zoom pan rasterises only newly-exposed
    /// perimeter tiles, so the count of these per gesture — not their individual
    /// cost — is what bounds pan work. See
    /// <c>docs/design/S100-Render-Subsystem-Design.md</c> §3.3.
    /// </summary>
    public static readonly Histogram<double> TileRasterizeDuration =
        Meter.CreateHistogram<double>(
            name: "s100.render.tile.rasterize.duration",
            unit: "ms",
            description: "Wall-clock duration of one off-thread base-plane tile rasterisation (core + gutter) by the tiled TiledScene render subsystem.");

    /// <summary>
    /// Wall-clock duration of one UI-thread tile composite pass (best-available
    /// blits of all visible tiles) by the tiled <c>TiledScene</c> arm
    /// (<see cref="S100VectorTileRenderer"/>). This is the per-frame work that
    /// stays on the render thread during a pan; tiling keeps it bounded by the
    /// visible tile count. See
    /// <c>docs/design/S100-Render-Subsystem-Design.md</c> §3.5.
    /// </summary>
    public static readonly Histogram<double> TileCompositeDuration =
        Meter.CreateHistogram<double>(
            name: "s100.render.tile.composite.duration",
            unit: "ms",
            description: "Wall-clock duration of one UI-thread tile composite pass (best-available blits of visible tiles) by the tiled TiledScene render subsystem.");

    /// <summary>
    /// Count of <b>visible exact-band tiles missing from the cache</b> at one
    /// composite pass (the tiled <c>TiledScene</c> arm, Phase&#160;3). This is the
    /// "cold-tile exposure" signal: every such tile is a slot the compositor has
    /// to fill from a scaled fallback band instead of the crisp target. The
    /// Phase&#160;3 exit criterion is that this drops to ≈0 during a scripted pan
    /// once prediction (<see cref="S100VectorTileRenderer"/>) pre-warms the
    /// perimeter. See <c>docs/design/S100-Render-Subsystem-Design.md</c> §3.6.
    /// </summary>
    public static readonly Histogram<int> TileColdExposure =
        Meter.CreateHistogram<int>(
            name: "s100.render.tile.cold.exposure",
            unit: "{tile}",
            description: "Number of visible exact-band tiles absent from cache at one composite pass by the tiled TiledScene render subsystem (cold-tile exposure).");

    /// <summary>
    /// Count of tiles rasterised <b>speculatively</b> (as part of the prediction
    /// warm set, not because they were visible) by the tiled <c>TiledScene</c>
    /// arm. The denominator of the prediction hit-rate. See §3.6.
    /// </summary>
    public static readonly Counter<long> TilePredictionRasterized =
        Meter.CreateCounter<long>(
            name: "s100.render.tile.prediction.rasterized",
            unit: "{tile}",
            description: "Tiles rasterised speculatively by the tiled TiledScene prediction warm set.");

    /// <summary>
    /// Count of speculatively-rasterised tiles that <b>subsequently became
    /// visible while still cached</b> — a successful prediction. Divided by
    /// <see cref="TilePredictionRasterized"/> this is the prediction hit-rate. See §3.6.
    /// </summary>
    public static readonly Counter<long> TilePredictionHits =
        Meter.CreateCounter<long>(
            name: "s100.render.tile.prediction.hits",
            unit: "{tile}",
            description: "Speculatively-rasterised tiles that later became visible while cached (prediction hits) in the tiled TiledScene render subsystem.");

    /// <summary>
    /// Count of visible/predicted tiles served from the persistent <b>disk
    /// cache</b> (Phase&#160;4) instead of being re-rasterised — a warm tile
    /// surviving a layer rebuild (palette flip-back) or a process restart. See §3.4.
    /// </summary>
    public static readonly Counter<long> TileDiskHits =
        Meter.CreateCounter<long>(
            name: "s100.render.tile.disk.hits",
            unit: "{tile}",
            description: "Tiles served from the persistent disk cache (warm) by the tiled TiledScene render subsystem, avoiding a re-rasterise.");

    /// <summary>
    /// Count of rasterised tiles written to the persistent <b>disk cache</b>
    /// (Phase&#160;4) for future warm reuse. See §3.4.
    /// </summary>
    public static readonly Counter<long> TileDiskWrites =
        Meter.CreateCounter<long>(
            name: "s100.render.tile.disk.writes",
            unit: "{tile}",
            description: "Tiles written to the persistent disk cache by the tiled TiledScene render subsystem.");

    private static IEnumerable<Measurement<double>> ObserveLayerGetFeaturesFps()
    {
        var measurements = new List<Measurement<double>>(s_callStats.Count);
        foreach (var pair in s_callStats)
        {
            var stats = pair.Value;
            var nowTimestamp = Stopwatch.GetTimestamp();
            var currentCount = Interlocked.Read(ref stats.Count);
            var deltaSeconds = Stopwatch.GetElapsedTime(stats.LastReadTimestamp).TotalSeconds;
            var deltaCalls = currentCount - stats.LastReadCount;

            // Mutated only from the meter's collection thread, so no
            // lock is needed; the call-side increments only touch
            // Count, never these fields.
            stats.LastReadCount = currentCount;
            stats.LastReadTimestamp = nowTimestamp;

            var fps = deltaSeconds > 0 ? deltaCalls / deltaSeconds : 0.0;
            measurements.Add(string.IsNullOrEmpty(pair.Key)
                ? new Measurement<double>(fps)
                : new Measurement<double>(fps,
                    new KeyValuePair<string, object?>("s100.product", pair.Key)));
        }
        return measurements;
    }
}
