using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using EncDotNet.S100.Diagnostics;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Nts;
using Mapsui.Rendering;
using Mapsui.Rendering.Skia;
using Mapsui.Rendering.Skia.SkiaStyles;
using Mapsui.Styles;
using SkiaSharp;

namespace EncDotNet.S100.Viewer.Diagnostics;

/// <summary>
/// Wraps Mapsui's per-style <see cref="IStyleRenderer"/> registrations
/// so that every style draw is timed and counted, and the
/// per-paint totals are emitted to OpenTelemetry tagged by style
/// type. Combined with <see cref="InstrumentedMapControl"/>'s
/// per-paint markers, this apportions the wall-clock paint
/// duration across <c>VectorStyle</c>, <c>LabelStyle</c>,
/// <c>SymbolStyle</c>, etc., so we can see which style class
/// dominates a paint.
/// </summary>
/// <remarks>
/// <para>
/// Mapsui's <c>MapRenderer</c> stores its style renderers in a
/// private static dictionary keyed by style type. The runtime
/// public surface (<c>MapRenderer.RegisterStyleRenderer</c>) lets
/// callers add new renderers but does not expose the existing
/// registrations, so we use reflection to read out the defaults
/// and re-register each as a wrapped <see cref="CountingStyleRenderer"/>.
/// All style draws run on the compositor render thread between
/// the start and end paint markers, so the accumulator is single-
/// threaded and lock-free.
/// </para>
/// <para>
/// The wrapping is one-shot: <see cref="Install"/> is idempotent
/// and a no-op after the first call.
/// </para>
/// </remarks>
internal static class MapPaintInstrumentation
{
    private static readonly object Sync = new();
    private static bool s_installed;

    /// <summary>
    /// When set (env <c>S100_MEASURE_VECTOR_SPLIT</c> = <c>1</c>/<c>true</c>),
    /// the <c>VectorStyle</c> renderer is wrapped so each draw is issued
    /// twice for the same feature/viewport: the first draw misses
    /// Mapsui's extent-keyed <c>VectorCache</c> (so it pays the full
    /// <c>ToSkiaPath</c> projection + <c>SKPath</c> build cost plus the
    /// draw-issue cost), while the immediate second draw hits the cache
    /// (paying only the draw-issue cost). The per-paint difference
    /// apportions the <c>VectorStyle</c> CPU cost into a "build" half
    /// (projection / path construction — what a translation-invariant
    /// path cache would eliminate on pans) and a "fill" half (the
    /// remaining draw-issue cost). Off by default; gated to a
    /// diagnostics run because it double-draws vectors.
    /// </summary>
    private static readonly bool s_measureSplit =
        (Environment.GetEnvironmentVariable("S100_MEASURE_VECTOR_SPLIT") ?? string.Empty)
            is "1" or "true" or "TRUE" or "True";

    private static double s_vectorBuildMs;
    private static double s_vectorFillMs;
    private static long s_vectorSplitCalls;

    private static readonly Meter Meter =
        S100Telemetry.CreateMeter(typeof(MapPaintInstrumentation));

    private static readonly Histogram<long> StyleCallsPerPaint =
        Meter.CreateHistogram<long>(
            name: "s100.map.paint.style.calls",
            unit: "{calls}",
            description: "Number of style-renderer Draw calls per paint, tagged by style type.");

    private static readonly Histogram<double> StyleDurationPerPaint =
        Meter.CreateHistogram<double>(
            name: "s100.map.paint.style.duration",
            unit: "ms",
            description: "Cumulative duration of style-renderer Draw calls per paint, tagged by style type.");

    /// <summary>
    /// Per (style-type, layer) accumulator for the in-flight paint.
    /// Mutated only on the compositor render thread (between
    /// <see cref="BeginPaint"/> and <see cref="EndPaintAndEmit"/>),
    /// so no locking is required.
    /// </summary>
    private static readonly Dictionary<(string Style, string Layer, string PointBucket), StyleStats> s_perPaint = new();

    private sealed class StyleStats
    {
        public long Calls;
        public double DurationMs;
    }

    public static void Install()
    {
        lock (Sync)
        {
            if (s_installed) return;
            s_installed = true;

            // Make sure our pattern-fill renderer is in the dict
            // before we wrap. Subsequent registrations of the same
            // style type overwrite, so this is safe even if Render()
            // hasn't run yet.
            EncDotNet.S100.Renderers.Mapsui.AnchoredPatternFillRenderer.Register();

            var dictField = typeof(MapRenderer).GetField(
                "_styleRenderers",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (dictField is null)
            {
                Console.Error.WriteLine(
                    "[MapPaintInstrumentation] could not find MapRenderer._styleRenderers; instrumentation disabled.");
                return;
            }

            if (dictField.GetValue(null) is not IDictionary<Type, IStyleRenderer> dict)
            {
                Console.Error.WriteLine(
                    "[MapPaintInstrumentation] _styleRenderers had unexpected type; instrumentation disabled.");
                return;
            }

            // Snapshot first because we'll mutate the dict.
            var snapshot = new List<KeyValuePair<Type, IStyleRenderer>>(dict);
            foreach (var pair in snapshot)
            {
                if (pair.Value is CountingStyleRenderer) continue;

                var inner = pair.Value;
                // In split-measurement mode, interpose a double-draw
                // measuring renderer between the counter and Mapsui's
                // real VectorStyle renderer so the build/fill split is
                // captured for the same draws the counter sees.
                if (s_measureSplit
                    && pair.Key == typeof(VectorStyle)
                    && inner is ISkiaStyleRenderer skiaInner)
                {
                    inner = new MeasuringVectorStyleRenderer(skiaInner);
                }

                dict[pair.Key] = new CountingStyleRenderer(pair.Key, inner);
            }
        }
    }

    /// <summary>Called from <see cref="InstrumentedMapControl"/>'s start marker.</summary>
    public static void BeginPaint()
    {
        // Reset accumulators in place to avoid GC churn.
        foreach (var stats in s_perPaint.Values)
        {
            stats.Calls = 0;
            stats.DurationMs = 0;
        }

        if (s_measureSplit)
        {
            s_vectorBuildMs = 0;
            s_vectorFillMs = 0;
            s_vectorSplitCalls = 0;
        }
    }

    /// <summary>Called from <see cref="InstrumentedMapControl"/>'s end marker.</summary>
    public static void EndPaintAndEmit()
    {
        foreach (var (key, stats) in s_perPaint)
        {
            if (stats.Calls == 0) continue;
            var styleTag = new KeyValuePair<string, object?>("style", key.Style);
            var layerTag = new KeyValuePair<string, object?>("layer", key.Layer);
            var bucketTag = new KeyValuePair<string, object?>("points", key.PointBucket);
            StyleCallsPerPaint.Record(stats.Calls, styleTag, layerTag, bucketTag);
            StyleDurationPerPaint.Record(stats.DurationMs, styleTag, layerTag, bucketTag);
        }
    }

    /// <summary>
    /// Builds an immutable, by-style aggregated snapshot of the paint
    /// that just completed, ordered by descending duration. Returns a
    /// fresh array of copied values so callers can retain it safely
    /// while the next paint mutates / resets the in-place accumulators.
    /// </summary>
    /// <remarks>
    /// Valid to call after <see cref="EndPaintAndEmit"/> and before the
    /// next <see cref="BeginPaint"/> (accumulators are reset on the next
    /// paint's start marker, not here). Runs on the render thread; the
    /// caller should only invoke it when a sink is attached so idle /
    /// stats-free runs pay no allocation.
    /// </remarks>
    public static IReadOnlyList<EncDotNet.S100.Viewer.Services.RenderStyleStat> CollectStyleSnapshot()
    {
        var byStyle = new Dictionary<string, (long Calls, double DurationMs)>();
        foreach (var (key, stats) in s_perPaint)
        {
            if (stats.Calls == 0) continue;
            byStyle.TryGetValue(key.Style, out var acc);
            byStyle[key.Style] = (acc.Calls + stats.Calls, acc.DurationMs + stats.DurationMs);
        }

        var result = new List<EncDotNet.S100.Viewer.Services.RenderStyleStat>(byStyle.Count);
        foreach (var (style, acc) in byStyle)
        {
            result.Add(new EncDotNet.S100.Viewer.Services.RenderStyleStat(style, acc.Calls, acc.DurationMs));
        }
        result.Sort(static (a, b) => b.DurationMs.CompareTo(a.DurationMs));

        // In split-measurement mode, surface the build/fill apportionment
        // as two synthetic entries so they flow through the existing
        // get_render_stats payload unchanged. Calls are reported as 0 so
        // the differential's extra draws don't inflate TotalDrawCalls;
        // the real VectorStyle call count is available from its own entry.
        if (s_measureSplit && s_vectorSplitCalls > 0)
        {
            result.Add(new EncDotNet.S100.Viewer.Services.RenderStyleStat("VectorBuild", 0, s_vectorBuildMs));
            result.Add(new EncDotNet.S100.Viewer.Services.RenderStyleStat("VectorFill", 0, s_vectorFillMs));
        }

        return result;
    }

    /// <summary>
    /// Bucket vertex count into a small, ordered set of labels so OTel
    /// histogram cardinality stays bounded. Buckets are powers of 10.
    /// </summary>
    private static string BucketPoints(int n) => n switch
    {
        < 0 => "n/a",
        0 => "0",
        < 10 => "1-9",
        < 100 => "10-99",
        < 1_000 => "100-999",
        < 10_000 => "1k-10k",
        < 100_000 => "10k-100k",
        _ => "100k+",
    };

    private static StyleStats GetStats(string styleName, string layerName, string pointBucket)
    {
        var key = (styleName, layerName, pointBucket);
        if (!s_perPaint.TryGetValue(key, out var stats))
        {
            stats = new StyleStats();
            s_perPaint[key] = stats;
        }
        return stats;
    }

    /// <summary>
    /// Wraps an inner <see cref="IStyleRenderer"/>, timing each
    /// <c>Draw</c> call and accumulating into the per-paint
    /// dictionary keyed by the wrapped style type's name.
    /// </summary>
    private sealed class CountingStyleRenderer : ISkiaStyleRenderer
    {
        private readonly string _styleName;
        private readonly IStyleRenderer _inner;
        private readonly ISkiaStyleRenderer? _innerSkia;

        public CountingStyleRenderer(Type styleType, IStyleRenderer inner)
        {
            _styleName = styleType.Name;
            _inner = inner;
            _innerSkia = inner as ISkiaStyleRenderer;
        }

        public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer,
            IFeature feature, IStyle style, RenderService renderService, long iteration)
        {
            // Mapsui dispatches via the ISkiaStyleRenderer interface
            // when targeting Skia; non-Skia renderers cannot be timed
            // through this path, so fall back to the base interface
            // (no-op for our purposes since the Skia pipeline is
            // exclusive on this control).
            if (_innerSkia is null) return false;

            var pointCount = (feature is GeometryFeature gf && gf.Geometry is not null)
                ? gf.Geometry.NumPoints
                : -1;
            var stats = GetStats(_styleName, layer.Name ?? "(unnamed)", BucketPoints(pointCount));
            var startTimestamp = Stopwatch.GetTimestamp();
            try
            {
                return _innerSkia.Draw(canvas, viewport, layer, feature, style, renderService, iteration);
            }
            finally
            {
                stats.Calls++;
                stats.DurationMs += Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
            }
        }
    }

    /// <summary>
    /// Interposed before Mapsui's real <c>VectorStyle</c> renderer in
    /// split-measurement mode. Each <see cref="Draw"/> issues the inner
    /// renderer twice for the same feature and viewport. The first call
    /// misses Mapsui's extent-keyed <c>VectorCache</c> and pays the full
    /// projection + <c>SKPath</c> construction cost (plus the draw-issue
    /// cost); the immediate second call hits the cache and pays only the
    /// draw-issue cost. The difference is accumulated as the per-paint
    /// "build" cost and the second call as the "fill" cost, letting a
    /// diagnostics run quantify how much of <c>VectorStyle</c>'s CPU time
    /// a translation-invariant path cache could remove on pans.
    /// </summary>
    /// <remarks>
    /// Only the first draw's return value is propagated. The extra draw
    /// produces identical overdraw, so this must stay gated behind
    /// <see cref="s_measureSplit"/>. The differential is only meaningful
    /// while Mapsui's <c>VectorCache</c> is enabled; if it is disabled
    /// the second draw also rebuilds and the build half collapses toward
    /// zero, which the harness can detect.
    /// </remarks>
    private sealed class MeasuringVectorStyleRenderer : ISkiaStyleRenderer
    {
        private readonly ISkiaStyleRenderer _inner;

        public MeasuringVectorStyleRenderer(ISkiaStyleRenderer inner) => _inner = inner;

        public bool Draw(SKCanvas canvas, Viewport viewport, ILayer layer,
            IFeature feature, IStyle style, RenderService renderService, long iteration)
        {
            var firstStart = Stopwatch.GetTimestamp();
            var result = _inner.Draw(canvas, viewport, layer, feature, style, renderService, iteration);
            var firstMs = Stopwatch.GetElapsedTime(firstStart).TotalMilliseconds;

            var secondStart = Stopwatch.GetTimestamp();
            _inner.Draw(canvas, viewport, layer, feature, style, renderService, iteration);
            var secondMs = Stopwatch.GetElapsedTime(secondStart).TotalMilliseconds;

            s_vectorBuildMs += Math.Max(0, firstMs - secondMs);
            s_vectorFillMs += secondMs;
            s_vectorSplitCalls++;
            return result;
        }
    }
}
