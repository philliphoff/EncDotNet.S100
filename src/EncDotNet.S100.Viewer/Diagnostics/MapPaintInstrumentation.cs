using System;
using System.Collections.Generic;
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
                dict[pair.Key] = new CountingStyleRenderer(pair.Key, pair.Value);
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
            var stats = GetStats(_styleName, layer?.Name ?? "(unnamed)", BucketPoints(pointCount));
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
}
