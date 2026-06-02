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
